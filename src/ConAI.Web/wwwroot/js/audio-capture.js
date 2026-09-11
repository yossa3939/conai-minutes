// マイクと会議タブの音声を取り込み、1 本の信号に混ぜる。
// DOM と画面の文言には触れない。失敗は CaptureError の kind で伝え、文言は meeting.js が決める。

// Chrome は映像の要求を外せないので video を必ず渡す。映像は取得直後に止める。
// video の displaySurface: 'browser' でダイアログをタブ一覧から開き、
// audio の suppressLocalAudioPlayback: false で共有中も利用者に会議の音を聞かせ続ける。
// トップレベルの selfBrowserSurface: 'exclude' で ConAI 自身のタブを候補から隠し、
// systemAudio: 'include' で「画面全体」を選ばれたときにシステム音声も取り込む。
// 音声トラックが無ければ acquireTab が no-tab-audio で弾くので、音の出ない「ウィンドウ」共有は続行しない。
const DISPLAY_CONSTRAINTS = {
    video: { displaySurface: 'browser' },
    audio: { suppressLocalAudioPlayback: false },
    selfBrowserSurface: 'exclude',
    systemAudio: 'include'
};

// 会議の音は同じブラウザが鳴らしている。エコーキャンセルが相手の声の回り込みを抑える。
const MIC_CONSTRAINTS = {
    audio: {
        echoCancellation: true,
        noiseSuppression: true,
        channelCount: 1
    }
};

// Chrome は実デバイスのほかに「既定」「通信」という別名の項目を並べる。一覧には実デバイスだけを出す。
const ALIAS_DEVICE_IDS = ['default', 'communications'];
const DEFAULT_LABEL_PREFIX = /^(既定|default|communications|通信)\s*-\s*/i;

// Windows の「ステレオ ミキサー」は PC の再生音を録るための入力で、マイクの声は入らない。
const LOOPBACK_DEVICE_PATTERN = /ステレオ\s*ミキサー|Stereo Mix/i;

// 選んだマイクが抜かれていたときにブラウザが返すエラー。既定のマイクで開き直す。
const RETRY_WITH_DEFAULT_ERRORS = ['OverconstrainedError', 'NotFoundError'];

export class CaptureError extends Error {
    constructor(kind, message) {
        super(message);
        this.name = 'CaptureError';
        this.kind = kind;
    }
}

export function isLoopbackDevice(label) {
    return LOOPBACK_DEVICE_PATTERN.test(label ?? '');
}

// マイクの一覧。label と deviceId は、マイクの利用を許可するまで空になる（ブラウザの仕様）。
// permission は Permissions API が返す許可の状態（granted / prompt / denied）。使えないブラウザでは unknown。
// hasInputs はマイクが 1 つでもつながっているか（許可の前でも、名前の無い 1 件が並ぶので分かる）。
// defaultLabel はブラウザ既定のマイクの名前（「既定 - 」の前置きは外す）。分からなければ空。
export async function listMicrophones() {
    if (typeof navigator.mediaDevices?.enumerateDevices !== 'function') {
        return { devices: [], defaultLabel: '', permission: 'unknown', hasInputs: false };
    }

    const inputs = (await navigator.mediaDevices.enumerateDevices())
        .filter(device => device.kind === 'audioinput');

    return {
        devices: inputs
            .filter(device => device.deviceId !== '' && !ALIAS_DEVICE_IDS.includes(device.deviceId))
            .map(device => ({ deviceId: device.deviceId, label: device.label })),
        defaultLabel: (inputs.find(device => device.deviceId === 'default')?.label ?? '').replace(DEFAULT_LABEL_PREFIX, ''),
        permission: await queryMicPermission(),
        hasInputs: inputs.length > 0
    };
}

// 装置名を出すためだけにマイクを開き、すぐ閉じる。許可のダイアログはここで出る。
// 許可されなければ getUserMedia のエラーがそのまま伝わる。
export async function requestMicAccess() {
    const stream = await openMic('');
    for (const track of stream.getTracks()) {
        track.stop();
    }
}

// Firefox は microphone の問い合わせで例外を投げる。状態が分からないときは unknown。
async function queryMicPermission() {
    try {
        return (await navigator.permissions.query({ name: 'microphone' })).state;
    } catch {
        return 'unknown';
    }
}

// 開始から timeoutMs の間、ワークレットが送る PCM がずっとほぼ 0 なら onSilent を呼ぶ。
// 音が届いた時点（警告の前でも後でも）で onSound を一度だけ呼び、それ以降は見ない。
export function createSilenceGuard({ timeoutMs, threshold, onSilent, onSound }) {
    let heard = false;
    const timer = setTimeout(() => {
        if (!heard) {
            onSilent();
        }
    }, timeoutMs);

    return {
        observe(buffer) {
            if (heard) {
                return;
            }

            const samples = new Int16Array(buffer);
            if (samples.some(sample => Math.abs(sample) > threshold)) {
                heard = true;
                clearTimeout(timer);
                onSound();
            }
        },
        stop() {
            heard = true;
            clearTimeout(timer);
        }
    };
}

// options.micDeviceId は使うマイクの deviceId。空ならブラウザ既定のマイク。
export async function acquireSources(source, options = {}) {
    const sources = [];

    if (source === 'mic-tab' || source === 'tab') {
        sources.push(await acquireTab());
    }

    if (source === 'mic' || source === 'mic-tab') {
        try {
            sources.push(await acquireMic(options.micDeviceId ?? ''));
        } catch (error) {
            // 「マイク＋PC 音声」でマイクが開けないときは、PC 音声だけで続けずに中止する。
            stopSources(sources);
            throw error;
        }
    }

    return sources;
}

// 取り込み元ごとの利得。PC 音声（動画や会議の再生音）はマイクの声より大きく途切れないため、マイクと同時に
// 等倍で足すとコンプレッサが PC 音声に合わせて縮み、マイクの声が埋もれて文字起こしから落ちる
// （2026-08-29 の実機で確認）。PC 音声を 0.5（-6 dB）に下げるのはマイクがいるときだけ。
// マイクがいない「PC 音声のみ」で下げると信号が Gemini の発話検出に届かず、録音中に文字起こしが返らなくなる。
const TAB_LEVEL_WITH_MIC = 0.5;
const FULL_LEVEL = 1;

// 利得は取り込み元の組み合わせで決める。下げるのは「マイクと一緒の PC 音声」だけで、それ以外は等倍。
export function mixLevel(kind, sources) {
    if (kind === 'tab' && sources.some(source => source.kind === 'mic')) {
        return TAB_LEVEL_WITH_MIC;
    }

    return FULL_LEVEL;
}

// 戻り値の inputs は取り込み元ごとの入力ノード。マイクだけの無音を見るために、混合前の信号を覗く口として使う。
export function createMixer(audioContext, sources) {
    const gain = audioContext.createGain();
    gain.gain.value = 1;

    // 2 系統を足した信号が Int16 変換の上限を超えて割れるのを防ぐ。既定値のまま使う。
    const compressor = audioContext.createDynamicsCompressor();
    const destination = audioContext.createMediaStreamDestination();
    const nodes = [gain, compressor, destination];
    const inputs = [];

    for (const source of sources) {
        const node = audioContext.createMediaStreamSource(source.stream);
        const level = audioContext.createGain();
        level.gain.value = mixLevel(source.kind, sources);
        node.connect(level);
        level.connect(gain);
        nodes.push(node, level);
        inputs.push({ kind: source.kind, node });
    }

    gain.connect(compressor);
    compressor.connect(destination);

    return { output: compressor, recorderStream: destination.stream, inputs, nodes };
}

export function watchInputs(sources, onLost) {
    const remaining = () => sources.filter(source => source.track.readyState === 'live').length;

    for (const source of sources) {
        if (source.track.readyState !== 'live') {
            // 監視を始める前に終わったトラック。ended はもう来ないので、その場で知らせる。
            onLost(source.kind, remaining());
            continue;
        }

        source.track.addEventListener('ended', () => onLost(source.kind, remaining()), { once: true });
    }
}

async function acquireTab() {
    let stream = null;
    try {
        stream = await navigator.mediaDevices.getDisplayMedia(DISPLAY_CONSTRAINTS);
    } catch {
        // 利用者のキャンセルは NotAllowedError で来る。他の DOMException も同じ扱いにする。
        throw new CaptureError('share-cancelled', '共有ダイアログが閉じられた');
    }

    // 画面の内容は文字起こしにも録音にも使わない。受け取った直後に止める。
    for (const video of stream.getVideoTracks()) {
        video.stop();
    }

    const [track, ...extras] = stream.getAudioTracks();
    if (!track) {
        // タブでも画面全体でも音声を共有しなかった（または音の出ないウィンドウを選んだ）場合はここで弾く。
        throw new CaptureError('no-tab-audio', 'PC 音声のトラックが無い');
    }

    // 使うのは 1 本目だけ。残りを放っておくと誰も止められず、共有中の表示が残る。
    for (const extra of extras) {
        extra.stop();
    }

    return { kind: 'tab', track, stream: new MediaStream([track]) };
}

// 戻り値の label は開いたマイクの名前、fallback は選んだマイクが見つからず既定で開き直したかどうか。
async function acquireMic(deviceId) {
    let stream = null;
    let fallback = false;
    try {
        stream = await openMic(deviceId);
    } catch (error) {
        if (deviceId === '' || !RETRY_WITH_DEFAULT_ERRORS.includes(error?.name)) {
            throw new CaptureError('mic-unavailable', 'マイクを開けない');
        }

        // 選んだマイクが抜かれていた。中止せず既定のマイクで続け、そのことを呼び出し側に伝える。
        fallback = true;
        try {
            stream = await openMic('');
        } catch {
            throw new CaptureError('mic-unavailable', 'マイクを開けない');
        }
    }

    const [track, ...extras] = stream.getAudioTracks();
    if (!track) {
        for (const other of stream.getTracks()) {
            other.stop();
        }

        throw new CaptureError('mic-unavailable', 'マイクの音声トラックが無い');
    }

    // 使うのは 1 本目だけ。残りは acquireTab と同じく止める。
    for (const extra of extras) {
        extra.stop();
    }

    return { kind: 'mic', track, stream, label: track.label, fallback };
}

function openMic(deviceId) {
    const audio = { ...MIC_CONSTRAINTS.audio };
    if (deviceId !== '') {
        // exact でないと、ブラウザは黙って別のマイクで開くことがある。
        audio.deviceId = { exact: deviceId };
    }

    return navigator.mediaDevices.getUserMedia({ audio });
}

function stopSources(sources) {
    for (const source of sources) {
        source.track.stop();
    }
}
