import {
    CaptureError,
    acquireSources,
    createMixer,
    createSilenceGuard,
    isLoopbackDevice,
    listMicrophones,
    requestMicAccess,
    watchInputs
} from './audio-capture.js';
import { createBusy } from './busy.js';
import { pollGeneration } from './generation-poll.js';
import { buildLiveSocketUrl, openLiveSocket } from './live-socket.js';

const STATUS_LABELS = {
    None: '',
    Queued: '待機中',
    Running: '実行中',
    Succeeded: '完了',
    Failed: '失敗'
};

const UPLOAD_ERROR_KEY = 'conai:upload-error';
const RECORDER_MIME_TYPES = ['audio/webm', 'audio/mp4'];
// 停止時にサーバの close（最後の文字起こしを保存し終えた合図）を待つ上限。サーバ側の待ち上限 3 秒 + 保存の時間に余裕を持たせる。
const STOP_CLOSE_TIMEOUT_MS = 8000;
// MediaRecorder の stop イベントを待つ上限。来なくても手元のデータをまとめて先へ進み、保存処理を止めない。
const STOP_RECORDER_TIMEOUT_MS = 5000;
// AudioContext.close() を待つ上限。まれに解決しない環境でも停止処理を止めない。
const AUDIO_CLOSE_TIMEOUT_MS = 3000;
// 録音のアップロードを待つ上限。回線が詰まっても再読み込みまで進める。
const UPLOAD_TIMEOUT_MS = 30000;

const CAPTURE_SOURCE_KEY = 'conai:capture-source';
const CAPTURE_SOURCE_VALUES = ['mic', 'mic-tab', 'tab'];
const DISPLAY_CAPTURE_SOURCES = ['mic-tab', 'tab'];

const RECORDING_NOTICES = {
    'mic': 'マイクのみで録音しています。',
    'mic-tab': 'マイク＋PC 音声で録音しています。',
    'tab': 'PC 音声のみで録音しています。'
};

const INPUT_LOST_NOTICES = {
    tab: 'PC 音声の取り込みが止まりました。マイクのみで続けています。',
    mic: 'マイクの取り込みが止まりました。PC 音声のみで続けています。'
};

const CAPTURE_ERROR_MESSAGES = {
    'share-cancelled': '共有がキャンセルされました。',
    'no-tab-audio': 'PC の音声を取り込めませんでした。共有ダイアログでタブか画面全体を選び、音声の共有をオンにしてください（ウィンドウの共有は音声を取り込めません）。',
    'mic-unavailable': 'マイクを利用できません。'
};

const CAPTURE_SOURCE_NOTES = [
    '共有ダイアログでタブか画面全体を選び、音声の共有をオンにしてください（ウィンドウの共有は音声を取り込めません）',
    'ヘッドホンの利用を勧めます（スピーカーだと相手の声をマイクも拾い、二重になることがあります）'
];

const DISPLAY_CAPTURE_UNAVAILABLE_NOTE = 'PC 音声の取り込みは Chrome または Edge で利用できます';

const MIC_DEVICE_KEY = 'conai:mic-device';
// 一覧が空のときの注記。Chrome はマイクの利用を許可するまで装置名を隠す。
const MIC_PERMISSION_NOTE = 'マイクの利用を許可すると一覧が出ます。画面を読み直すと、もう一度確認が出ます。';
const MIC_BLOCKED_NOTE = 'マイクの利用がブロックされているため一覧を出せません。アドレスバーのサイト情報でマイクを許可し、画面を読み直してください。';
const MIC_NOT_FOUND_NOTE = 'マイクが見つかりません。マイクをつなぐと一覧に出ます。';
const EMPTY_MIC_LIST = { devices: [], defaultLabel: '', permission: 'unknown', hasInputs: true };
const LOOPBACK_DEVICE_REASON = 'PC の再生音を録るための入力で、マイクの声は入りません。';
const MIC_FALLBACK_NOTICE = '選んだマイクが見つからないため、ブラウザの既定のマイクで録音しています。';

// 録音開始からこの時間ずっと無音なら知らせる。ステレオ ミキサーのような声の入らない入力に気づくため。
const SILENCE_TIMEOUT_MS = 5000;
// Int16 で ±8（約 -72 dBFS）までは無音とみなす。マイクの底ノイズはこれより大きい。
const SILENCE_THRESHOLD = 8;
const SILENCE_NOTICES = {
    'mic': 'マイクから音が届いていません。マイクの選択を確かめてください。',
    'mic-tab': '音が届いていません。マイクの選択と、共有したタブや画面の音声を確かめてください。',
    'tab': 'PC 音声が届いていません。共有したタブや画面で音が鳴っているか確かめてください。'
};

function start(element) {
    const config = {
        meetingId: element.dataset.meetingId,
        apiBase: element.dataset.apiBase,
        wsPath: element.dataset.wsPath,
        workletPath: element.dataset.workletPath,
        live: element.dataset.live === 'true',
        translate: element.dataset.translate === 'true',
        targetLanguage: element.dataset.targetLanguage,
        generationStatus: element.dataset.generationStatus
    };

    const csrf = document.querySelector('meta[name="csrf"]')?.content ?? '';

    showStoredError();
    setUpFiles(config, csrf);
    setUpGeneration(config, csrf);

    if (config.live) {
        setUpRecording(config, csrf, element);
    }
}

function showStoredError() {
    const stored = sessionStorage.getItem(UPLOAD_ERROR_KEY);
    if (!stored) {
        return;
    }

    sessionStorage.removeItem(UPLOAD_ERROR_KEY);

    // どの欄のエラーかも覚えて出す。壊れた値（古い書式）でも音声・動画の欄へ出す。
    let box = 'media-error';
    let message = stored;
    try {
        const parsed = JSON.parse(stored);
        if (typeof parsed?.box === 'string' && typeof parsed?.message === 'string') {
            box = parsed.box;
            message = parsed.message;
        }
    } catch {
        // 覚えていた値が JSON でなければ、そのまま本文として出す。
    }

    showError(box, message);
}

function showError(boxId, message) {
    const box = document.getElementById(boxId);
    if (!box) {
        return;
    }

    box.textContent = message;
    box.classList.remove('hidden');
}

// 添付は音声・動画と参考資料の 2 つの欄に分かれる（RV 2）。kindName は選び違えたときの案内文に使う。
const FILE_CARDS = [
    {
        inputId: 'media-input',
        errorId: 'media-error',
        listId: 'media-list',
        kindName: '音声・動画',
        otherKindName: '参考資料'
    },
    {
        inputId: 'reference-input',
        errorId: 'reference-error',
        listId: 'reference-list',
        kindName: '参考資料',
        otherKindName: '音声・動画'
    }
];

function setUpFiles(config, csrf) {
    for (const card of FILE_CARDS) {
        setUpFileInput(config, csrf, card);
        setUpFileList(config, csrf, card);
    }
}

function setUpFileInput(config, csrf, card) {
    const input = document.getElementById(card.inputId);
    if (!input) {
        return;
    }

    const accepted = acceptExtensions(input);
    const other = FILE_CARDS.find(entry => entry !== card);
    const otherAccepted = acceptExtensions(other ? document.getElementById(other.inputId) : null);

    input.addEventListener('change', async () => {
        if (input.files.length === 0) {
            return;
        }

        // 拡張子を欄の accept と照らし、合うファイルだけを送る。合わないものは理由を 1 行ずつ出す。
        const uploads = [];
        const rejections = [];
        for (const file of input.files) {
            const message = fileRejection(file, accepted, otherAccepted, card);
            if (message === null) {
                uploads.push(file);
            } else {
                rejections.push(message);
            }
        }

        if (uploads.length === 0) {
            showError(card.errorId, rejections.join('\n'));
            input.value = '';
            return;
        }

        input.disabled = true;
        try {
            await uploadFiles(config, csrf, uploads, card.errorId, rejections);
        } finally {
            input.disabled = false;
        }
    });
}

function setUpFileList(config, csrf, card) {
    const list = document.getElementById(card.listId);
    if (!list) {
        return;
    }

    list.addEventListener('click', async event => {
        const button = event.target.closest('.file-delete');
        if (!button || !window.confirm('この添付ファイルを削除します。よろしいですか。')) {
            return;
        }

        const response = await fetch(`${config.apiBase}/files/${button.dataset.fileId}`, {
            method: 'DELETE',
            headers: { 'X-CSRF-TOKEN': csrf }
        });

        if (response.ok) {
            window.location.reload();
        } else {
            showError(card.errorId, '添付ファイルを削除できませんでした。');
        }
    });
}

// 選んだファイルがこの欄で受け付けないものなら、その理由の文を返す。受け付けるなら null。
function fileRejection(file, accepted, otherAccepted, card) {
    const extension = fileExtension(file.name);
    if (accepted.includes(extension)) {
        return null;
    }

    if (otherAccepted.includes(extension)) {
        return `${file.name}：この欄には${card.kindName}だけを追加できます。${card.otherKindName}は「${card.otherKindName}」の欄に追加してください。`;
    }

    return `${file.name}：対応していないファイルです。`;
}

// 拡張子を小文字で取り出す。ドットが無ければ空文字（どちらの一覧にも載らない）。
function fileExtension(name) {
    const dot = name.lastIndexOf('.');
    return dot === -1 ? '' : name.slice(dot).toLowerCase();
}

// 欄の accept 属性（.mp3,.m4a,…）を拡張子の一覧にする。
function acceptExtensions(input) {
    return (input?.getAttribute('accept') ?? '')
        .split(',')
        .map(extension => extension.trim().toLowerCase())
        .filter(extension => extension !== '');
}

async function uploadFiles(config, csrf, files, errorId, rejections) {
    const form = new FormData();
    for (const file of files) {
        form.append('files', file);
    }

    const response = await fetch(`${config.apiBase}/files`, {
        method: 'POST',
        headers: { 'X-CSRF-TOKEN': csrf },
        body: form
    });

    const payload = await readJson(response);
    const messages = [...rejections, ...(payload?.rejected ?? []).map(item => `${item.fileName}：${item.message}`)];

    if (!response.ok && messages.length === 0) {
        messages.push('アップロードに失敗しました。');
    }

    if ((payload?.accepted ?? []).length > 0) {
        if (messages.length > 0) {
            // 再読み込みをまたぐので、出す欄ごと覚えておく。
            rememberUploadError(errorId, messages.join('\n'));
        }

        window.location.reload();
        return;
    }

    showError(errorId, messages.join('\n'));
}

// 再読み込みをまたいで出すエラーを、出す欄ごと覚える（RV 2 で欄が 2 つに増えたため）。
function rememberUploadError(box, message) {
    sessionStorage.setItem(UPLOAD_ERROR_KEY, JSON.stringify({ box, message }));
}

async function readJson(response) {
    try {
        return await response.json();
    } catch {
        return null;
    }
}

function dirtyFields() {
    return ['transcription', 'translated-transcription', 'minutes', 'minutes-template']
        .map(id => document.getElementById(id))
        .filter(field => field && isDirty(field));
}

// select には defaultValue が無い。保存済みの値は、HTML の selected 属性が付いた選択肢（defaultSelected）が持つ。
function isDirty(field) {
    if (field.tagName !== 'SELECT') {
        return field.value !== field.defaultValue;
    }

    const saved = Array.from(field.options).find(option => option.defaultSelected);
    return field.value !== (saved ? saved.value : field.options[0]?.value);
}

// 生成の依頼でサーバ側に保存されたので、保存済みの印を今の選択に合わせる。
function markSelectSaved(field) {
    if (!field) {
        return;
    }

    for (const option of field.options) {
        option.defaultSelected = option.selected;
    }
}

function setUpGeneration(config, csrf) {
    const button = document.getElementById('generate-button');
    const status = document.getElementById('generation-status');
    if (!button) {
        return;
    }

    button.addEventListener('click', async () => {
        if (button.dataset.hasMinutes === 'true'
            && !window.confirm('すでにある議事録を上書きします。よろしいですか。')) {
            return;
        }

        setBusy('generating', true);
        const template = document.getElementById('minutes-template');
        const response = await fetch(`${config.apiBase}/generation`, {
            method: 'POST',
            headers: {
                'X-CSRF-TOKEN': csrf,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ minutesTemplateId: template ? template.value : null })
        });

        if (!response.ok) {
            setBusy('generating', false);
            setText(status, response.status === 409
                ? 'すでに生成中です。'
                : '生成を開始できませんでした。');
            return;
        }

        markSelectSaved(template);
        setText(status, STATUS_LABELS.Queued);
        startPolling(config, status);
    });

    if (config.generationStatus === 'Queued' || config.generationStatus === 'Running') {
        setBusy('generating', true);
        startPolling(config, status);
    }
}

// 録音中も生成中も、「保存」と「議事録を生成」は押させない。
// 生成中の保存は議事録の上書きが混ざり、録音中の生成は Live の追記を壊すためである。
// 二つの流れが別々にボタンを書き換えると、あとに書いたほうが相手の判断を消すので、判断は busy.js に一本化する。
const setBusy = createBusy(enabled => {
    for (const id of ['save-button', 'generate-button']) {
        const button = document.getElementById(id);
        if (button) {
            button.disabled = !enabled;
        }
    }
});

// 走っている監視を止める関数。録音を始めるときに呼ぶ。
let stopPolling = null;

function startPolling(config, status) {
    stopPolling?.();
    stopPolling = pollGeneration({
        url: `${config.apiBase}/generation`,
        fetch: (url, init) => window.fetch(url, init),
        setInterval: (handler, interval) => window.setInterval(handler, interval),
        clearInterval: handle => window.clearInterval(handle),
        onStatus: payload => setText(status, STATUS_LABELS[payload.status] ?? payload.status),
        onSettled: payload => finishGeneration(payload, status),
        onGiveUp: () => setText(status, '生成の状況を確認できません。ページを再読み込みしてください。')
    });
}

function finishGeneration(payload, status) {
    setBusy('generating', false);

    if (payload.status === 'Failed') {
        setText(status, payload.error || STATUS_LABELS.Failed);
        return;
    }

    applyGenerated(payload);

    if (dirtyFields().length > 0) {
        // 未保存の編集を消さない。再読込は利用者に委ねる。
        setText(status, '議事録ができました。編集中の内容はそのまま残しています。保存してから、ページを再読み込みしてください。');
        return;
    }

    window.location.reload();
}

// 生成の結果を、利用者が手を入れていない欄にだけ書き戻す。
// 書き戻さないと、このあとの「保存」が画面に残った古い内容でサーバの結果を上書きしてしまう。
function applyGenerated(payload) {
    setSavedValue(document.getElementById('transcription'), payload.transcription);
    setSavedValue(document.getElementById('translated-transcription'), payload.translatedTranscription);
    setSavedValue(document.getElementById('minutes'), payload.minutes);
}

// 保存済みの値ごと入れ替える。編集中の欄には触れない。
function setSavedValue(field, value) {
    if (!field || typeof value !== 'string' || isDirty(field)) {
        return;
    }

    field.value = value;
    field.defaultValue = value;
}

function setUpRecording(config, csrf, element) {
    const button = document.getElementById('record-button');
    if (!button) {
        return;
    }

    // 「PC 音声のみ」はマイクを使わないので、マイクの選択欄を隠す。
    const micDevice = setUpMicDevice();
    const capture = setUpCaptureSource(source => micDevice.setVisible(source !== 'tab'));
    micDevice.setVisible(capture.current() !== 'tab');

    const state = {
        recording: false,
        stopping: false,
        source: 'mic',
        sources: [],
        capture,
        micDevice,
        silence: null,
        // 「マイク＋PC 音声」でマイクの入力だけを覗くワークレットと、その無音の監視。
        micTap: null,
        micSilence: null,
        silent: { mixed: false, mic: false },
        notice: emptyNotice(),
        audioContext: null,
        mixer: null,
        workletNode: null,
        silentGain: null,
        socket: null,
        recorder: null,
        chunks: [],
        mimeType: ''
    };

    button.addEventListener('click', async () => {
        if (state.recording) {
            await stopRecording(config, csrf, state, button, element);
        } else {
            await startRecording(config, csrf, state, button, element);
        }
    });
}

// onChange は利用者が取り込み元を変えたときに、新しい値で呼ばれる。
function setUpCaptureSource(onChange) {
    const fieldset = document.getElementById('capture-source');
    const radios = fieldset
        ? [...fieldset.querySelectorAll('input[name="capture-source"]')]
        : [];
    const supported = typeof navigator.mediaDevices?.getDisplayMedia === 'function';

    const controller = {
        current: () => radios.find(radio => radio.checked)?.value ?? 'mic',
        setDisabled: recording => {
            for (const radio of radios) {
                radio.disabled = recording
                    || (!supported && DISPLAY_CAPTURE_SOURCES.includes(radio.value));
            }
        }
    };

    if (radios.length === 0) {
        return controller;
    }

    let restored = readStoredSource();
    if (!supported && DISPLAY_CAPTURE_SOURCES.includes(restored)) {
        // 記憶していた選択をこのブラウザでは使えない。既定に戻す。
        restored = 'mic';
    }

    const selected = radios.find(radio => radio.value === restored) ?? radios[0];
    selected.checked = true;

    for (const radio of radios) {
        radio.addEventListener('change', () => {
            if (!radio.checked) {
                return;
            }

            writeStored(CAPTURE_SOURCE_KEY, radio.value);
            updateCaptureSourceNote(controller.current(), supported);
            onChange?.(controller.current());
        });
    }

    controller.setDisabled(false);
    updateCaptureSourceNote(controller.current(), supported);
    return controller;
}

// 録音に使うマイクの選択。Windows は既定の録音デバイスが「ステレオ ミキサー」のことがあり、
// そのままではマイクの声が入らない。利用者が一覧から選べるようにし、選択は localStorage に覚える。
// Chrome はマイクの許可を保存していないと、読み込みごとに deviceId を作り直す。
// id で見つからなければ装置名で探せるよう、選んだときに両方を覚える。
function readStoredMicDevice() {
    try {
        const parsed = JSON.parse(readStored(MIC_DEVICE_KEY) || 'null');
        return {
            deviceId: typeof parsed?.deviceId === 'string' ? parsed.deviceId : '',
            label: typeof parsed?.label === 'string' ? parsed.label : ''
        };
    } catch {
        return { deviceId: '', label: '' };
    }
}

function findMicDevice(devices, wanted) {
    if (!wanted) {
        return null;
    }

    return devices.find(device => device.deviceId === wanted.deviceId)
        ?? (wanted.label ? devices.find(device => device.label === wanted.label) : null)
        ?? null;
}

function setUpMicDevice() {
    const field = document.getElementById('mic-device-field');
    const select = document.getElementById('mic-device');
    // 先頭の「ブラウザの既定のマイク」はサーバが描く。一覧を作り直しても残す。
    const defaultOption = select?.options[0] ?? null;
    let listed = EMPTY_MIC_LIST;
    let requested = false;

    const controller = {
        current: () => select?.value ?? '',
        defaultName: () => defaultOption?.textContent ?? '',
        setDisabled: recording => {
            if (select) {
                select.disabled = recording;
            }
        },
        // 選択欄が見えるようになった時点（画面を開いた直後を含む）で一覧が空なら、許可を求めて埋める。
        setVisible: visible => {
            field?.classList.toggle('hidden', !visible);
            if (visible && listed.devices.length === 0) {
                refresh({ request: true });
            }
        },
        refresh: () => refresh()
    };

    if (!select) {
        return controller;
    }

    async function refresh({ request = false } = {}) {
        listed = await list();

        // Chrome は許可するまで装置名を隠す。ブロック中でなければ一度だけ許可を求め、マイクをすぐ閉じて一覧を作り直す。
        // 断られた（閉じられた）ときは読み直すまで求め直さない。
        if (request && !requested && listed.devices.length === 0 && listed.hasInputs && listed.permission !== 'denied') {
            requested = true;
            await requestMicAccess().catch(() => {});
            listed = await list();
        }

        const { devices } = listed;

        // 画面で選んだ値があればそれを、無ければ覚えている値を選び直す。一覧に無いときは既定に戻す。
        // 録音中（非活性）は覚えている値を当てない。許可の直後に一覧ができても、録っているのは開始時の装置のままだからである。
        const wanted = select.value
            ? { deviceId: select.value, label: select.selectedOptions[0]?.textContent ?? '' }
            : select.disabled ? null : readStoredMicDevice();
        // 既定の選択肢はサーバが描くが、描かれていない画面でも一覧だけは作れるようにする。
        select.replaceChildren(...(defaultOption ? [defaultOption] : []), ...devices.map(device => {
            const option = document.createElement('option');
            option.value = device.deviceId;
            option.textContent = device.label || 'マイク';
            return option;
        }));
        select.value = findMicDevice(devices, wanted)?.deviceId ?? '';
        updateNote();
    }

    function list() {
        return listMicrophones().catch(() => EMPTY_MIC_LIST);
    }

    function updateNote() {
        const note = document.getElementById('mic-device-note');
        renderLines(note, [micDeviceNote(select, listed)].filter(line => line !== ''));
    }

    select.addEventListener('change', () => {
        writeStored(MIC_DEVICE_KEY, JSON.stringify({
            deviceId: select.value,
            label: select.value ? select.selectedOptions[0]?.textContent ?? '' : ''
        }));
        updateNote();
    });

    // 抜き差しで一覧が変わる。ブラウザが知らせてくれるので、そのたびに作り直す。
    // 最初の一覧は setVisible が作る（「PC 音声のみ」で隠れているあいだは許可を求めない）。
    navigator.mediaDevices?.addEventListener?.('devicechange', () => refresh());
    return controller;
}

function micDeviceNote(select, { devices, defaultLabel, permission, hasInputs }) {
    if (devices.length === 0) {
        if (!hasInputs) {
            return MIC_NOT_FOUND_NOTE;
        }

        return permission === 'denied' ? MIC_BLOCKED_NOTE : MIC_PERMISSION_NOTE;
    }

    if (select.value === '') {
        return isLoopbackDevice(defaultLabel)
            ? `ブラウザの既定のマイクは「${defaultLabel}」です。${LOOPBACK_DEVICE_REASON}一覧から使うマイクを選んでください。`
            : '';
    }

    const label = select.selectedOptions[0]?.textContent ?? '';
    return isLoopbackDevice(label)
        ? `「${label}」は ${LOOPBACK_DEVICE_REASON}別のマイクを選んでください。`
        : '';
}

function readStoredSource() {
    const stored = readStored(CAPTURE_SOURCE_KEY);
    return CAPTURE_SOURCE_VALUES.includes(stored) ? stored : 'mic';
}

function readStored(key) {
    try {
        return window.localStorage.getItem(key) ?? '';
    } catch {
        return '';
    }
}

function writeStored(key, value) {
    try {
        window.localStorage.setItem(key, value);
    } catch {
        // 保存できないブラウザ設定では、次回の既定に戻るだけで済ませる。
    }
}

function updateCaptureSourceNote(source, supported) {
    const note = document.getElementById('capture-source-note');
    if (!note) {
        return;
    }

    const lines = [];
    if (!supported) {
        // 選択肢が選べない理由なので、いまの選択に関わらず出す。
        lines.push(DISPLAY_CAPTURE_UNAVAILABLE_NOTE);
    } else if (DISPLAY_CAPTURE_SOURCES.includes(source)) {
        lines.push(...CAPTURE_SOURCE_NOTES);
    }

    renderLines(note, lines);
}

// 1 行 1 段落で描く。空なら要素ごと隠す。
function renderLines(element, lines) {
    if (!element) {
        return;
    }

    element.replaceChildren(...lines.map(line => {
        const paragraph = document.createElement('p');
        paragraph.textContent = line;
        return paragraph;
    }));
    element.classList.toggle('hidden', lines.length === 0);
}

// 録音中の案内は 4 行まで：取り込み元、使っているマイク、既定のマイクで開き直した旨、無音の警告。
function emptyNotice() {
    return { main: '', mic: '', fallback: '', silence: '' };
}

function renderCaptureNotice(state) {
    const { main, mic, fallback, silence } = state.notice;
    renderLines(document.getElementById('capture-notice'), [main, mic, fallback, silence].filter(line => line !== ''));
}

// 無音の警告。混合後の信号が無音なら取り込み元に応じた文、混合後には音があってマイクだけが無音ならマイクの文。
function silenceNotice(state) {
    if (state.silent.mixed) {
        return SILENCE_NOTICES[state.source];
    }

    return state.silent.mic ? SILENCE_NOTICES.mic : '';
}

function setSilent(state, part, silent) {
    state.silent[part] = silent;
    state.notice.silence = silenceNotice(state);
    renderCaptureNotice(state);
}

// 録音中は live の文字を追記する欄を手で編集させない（追記と手の編集が混ざるのを防ぐ）。停止後の再読み込みで元に戻る。
function setTranscriptEditable(editable) {
    for (const id of ['transcription', 'translated-transcription']) {
        const field = document.getElementById(id);
        if (field) {
            field.readOnly = !editable;
        }
    }
}

async function startRecording(config, csrf, state, button, element) {
    button.disabled = true;
    let started = false;

    try {
        state.source = state.capture.current();
        setText(document.getElementById('live-status'), '音声の取り込みを準備しています。');

        try {
            // 共有ダイアログはクリックから続く短い時間内にしか開けない。ここを最初に置く。
            state.sources = await acquireSources(state.source, { micDeviceId: state.micDevice.current() });
        } catch (error) {
            setText(document.getElementById('live-status'), captureErrorMessage(error));
            return;
        }

        // マイクの利用を許可した直後から一覧に名前が入る。ここで作り直す（初回の録音で一覧が埋まる）。
        state.micDevice.refresh();

        try {
            state.socket = await openSocket(config, state);
        } catch {
            setText(document.getElementById('live-status'), 'サーバに接続できませんでした。');
            return;
        }

        state.audioContext = new AudioContext({ sampleRate: 16000 });
        await state.audioContext.audioWorklet.addModule(config.workletPath);

        state.mixer = createMixer(state.audioContext, state.sources);
        state.workletNode = new AudioWorkletNode(state.audioContext, 'pcm-processor');
        state.silent = { mixed: false, mic: false };
        state.silence = createSilenceGuard({
            timeoutMs: SILENCE_TIMEOUT_MS,
            threshold: SILENCE_THRESHOLD,
            onSilent: () => setSilent(state, 'mixed', true),
            onSound: () => setSilent(state, 'mixed', false)
        });
        state.workletNode.port.onmessage = event => {
            if (state.socket?.readyState === WebSocket.OPEN) {
                state.socket.send(event.data);
            }

            state.silence?.observe(event.data);
        };

        // 出力に何もつながないとブラウザがワークレットを止めるため、無音の経路で destination へつなぐ。
        state.silentGain = state.audioContext.createGain();
        state.silentGain.gain.value = 0;
        state.mixer.output.connect(state.workletNode);
        state.workletNode.connect(state.silentGain);
        state.silentGain.connect(state.audioContext.destination);

        // 「マイク＋PC 音声」では混合後の信号が PC 音声で鳴り続けるため、マイクだけの無音を混合後からは見分けられない。
        // マイクの入力を別のワークレットで覗き、マイクだけが無音なら知らせる。
        const micInput = state.mixer.inputs.find(input => input.kind === 'mic');
        if (state.source === 'mic-tab' && micInput) {
            state.micTap = new AudioWorkletNode(state.audioContext, 'pcm-processor');
            state.micSilence = createSilenceGuard({
                timeoutMs: SILENCE_TIMEOUT_MS,
                threshold: SILENCE_THRESHOLD,
                onSilent: () => setSilent(state, 'mic', true),
                onSound: () => setSilent(state, 'mic', false)
            });
            state.micTap.port.onmessage = event => state.micSilence?.observe(event.data);
            micInput.node.connect(state.micTap);
            state.micTap.connect(state.silentGain);
        }

        state.mimeType = RECORDER_MIME_TYPES.find(type => MediaRecorder.isTypeSupported(type)) ?? '';
        state.chunks = [];
        if (state.mimeType) {
            // 入力は混合後のストリーム。録音は全モードで 16 kHz モノラルになる。
            state.recorder = new MediaRecorder(state.mixer.recorderStream, { mimeType: state.mimeType });
            state.recorder.addEventListener('dataavailable', event => {
                if (event.data.size > 0) {
                    state.chunks.push(event.data);
                }
            });
            state.recorder.start(1000);
        }

        state.recording = true;
        started = true;
        button.textContent = '録音を停止';
        button.setAttribute('aria-label', '録音を停止');
        element.classList.add('conai-recording');
        state.capture.setDisabled(true);
        state.micDevice.setDisabled(true);
        setTranscriptEditable(false);
        setBusy('recording', true);

        // 生成の監視が回ったままだと、録音中に生成が終わったときに文字起こしの欄を書き換え、
        // Live の追記を壊す。停止後の再読み込みで、そのときの状況からやり直す。
        stopPolling?.();

        const mic = state.sources.find(source => source.kind === 'mic');
        state.notice = {
            main: RECORDING_NOTICES[state.source],
            mic: mic ? `使っているマイク: ${mic.label || state.micDevice.defaultName()}` : '',
            fallback: mic?.fallback ? MIC_FALLBACK_NOTICE : '',
            silence: ''
        };
        renderCaptureNotice(state);

        watchInputs(state.sources, (kind, remaining) =>
            handleInputLost(config, csrf, state, button, element, kind, remaining));
    } catch (error) {
        // AudioContext・ワークレット・MediaRecorder のいずれかが投げた。開始前の表示に戻す。
        console.error(error);
        setText(document.getElementById('live-status'), '録音を開始できませんでした。');
    } finally {
        if (!started) {
            await abortStart(state);
        }

        // 開始直後に入力が失われて stopRecording が走っている最中は、保存中のボタンを戻さない。
        if (!state.stopping) {
            button.disabled = false;
        }
    }
}

function captureErrorMessage(error) {
    // acquireSources が投げるのは CaptureError の 3 種類だけ。
    if (error instanceof CaptureError && CAPTURE_ERROR_MESSAGES[error.kind]) {
        return CAPTURE_ERROR_MESSAGES[error.kind];
    }

    return CAPTURE_ERROR_MESSAGES['mic-unavailable'];
}

async function handleInputLost(config, csrf, state, button, element, kind, remaining) {
    if (!state.recording) {
        return;
    }

    if (remaining > 0) {
        // 残りの入力で録音を続ける。グラフは組み替えない。
        state.notice.main = INPUT_LOST_NOTICES[kind];
        if (kind === 'mic') {
            state.notice.mic = '';
            state.notice.fallback = '';
            // マイクが無くなったので、マイクだけの無音はもう見ない。
            state.micSilence?.stop();
            state.micSilence = null;
            state.silent.mic = false;
            state.notice.silence = silenceNotice(state);
        }

        renderCaptureNotice(state);
        return;
    }

    await stopRecording(config, csrf, state, button, element);
}

async function openSocket(config, state) {
    let socket = null;
    socket = await openLiveSocket({
        url: buildLiveSocketUrl(window.location, config),
        onMessage: handleServerMessage,
        // 知らせるのは、今つないでいるソケットが落ちたときだけ。停止と開始の失敗は
        // 閉じる前に state.socket を外すので、ここで自分を指していない。
        // 「片付けの最中かどうか」の印で見分けると、片付けが終わったあとに遅れて届く
        // close や、やり直しで開き直したあとの古い close を取り違える。
        onClose: () => {
            if (state.socket === socket) {
                setText(document.getElementById('live-status'), '切断しました。');
            }
        }
    });

    return socket;
}

function handleServerMessage(event) {
    if (typeof event.data !== 'string') {
        return;
    }

    let frame = null;
    try {
        frame = JSON.parse(event.data);
    } catch {
        return;
    }

    // live の文字は保存欄へ直接追記する。サーバも同じ文字を DB へ追記しているので、停止後の再読み込みで DB の値に置き換わる。
    const transcript = document.getElementById('transcription');
    const translation = document.getElementById('translated-transcription');

    switch (frame.type) {
        case 'Transcript':
            appendText(transcript, frame.text);
            break;
        case 'Translation':
            appendText(translation, frame.text);
            break;
        case 'TurnComplete':
            appendText(transcript, '\n');
            appendText(translation, '\n');
            break;
        case 'Info':
        case 'Error':
            setText(document.getElementById('live-status'), frame.text);
            break;
    }
}

// close イベント（サーバが close を返した）か上限のどちらか早いほうで解決する。
function waitForClose(socket, timeoutMs) {
    if (!socket || socket.readyState === WebSocket.CLOSED) {
        return Promise.resolve();
    }

    return new Promise(resolve => {
        const timer = setTimeout(resolve, timeoutMs);
        socket.addEventListener('close', () => {
            clearTimeout(timer);
            resolve();
        }, { once: true });
    });
}

async function stopRecording(config, csrf, state, button, element) {
    state.stopping = true;
    state.recording = false;
    button.disabled = true;
    setText(document.getElementById('live-status'), '保存しています。');

    // 片付けのどの待ちが解けなくても、finally で必ず再読み込みまで進める。
    // 再読み込みは保存済みの文字起こしを DB から読み直すので、停止後に本文が空のまま止まる不具合を防ぐ。
    try {
        // 先に AudioContext を閉じると録音のトラックが切れ、最後のデータを取りこぼす。
        // stop イベントが来なくても上限で手元のデータをまとめ、ここで止まらないようにする。
        const recorded = await stopRecorder(state, STOP_RECORDER_TIMEOUT_MS);

        // 自分で閉じるソケットは、閉じる前に state から外す。これで close の知らせが
        // 「保存しています。」を「切断しました。」で上書きしない。
        const socket = state.socket;
        state.socket = null;

        // close を送る前に待ち受けを始めないと、待つ前に close が来て取りこぼす。
        const closed = waitForClose(socket, STOP_CLOSE_TIMEOUT_MS);

        if (socket?.readyState === WebSocket.OPEN) {
            socket.close(1000, 'stopped');
        }

        disconnectAudio(state);
        await withTimeout(state.audioContext?.close(), AUDIO_CLOSE_TIMEOUT_MS);
        stopTracks(state);

        state.mixer = null;
        state.audioContext = null;
        state.workletNode = null;
        state.micTap = null;
        state.silentGain = null;
        state.recorder = null;
        element.classList.remove('conai-recording');
        button.textContent = '録音を開始';
        button.setAttribute('aria-label', '録音を開始');
        state.capture.setDisabled(false);
        state.micDevice.setDisabled(false);
        setTranscriptEditable(true);
        state.notice = emptyNotice();
        renderCaptureNotice(state);

        // 録音は取り直しがきかないので、サーバの close を待つ前に送る。
        // close を待つあいだに利用者が画面を離れると、後回しにした送信はそのまま失われる。
        // uploadRecording は内部でタイムアウトと失敗処理を持つため、ここでは投げない。
        if (recorded) {
            await uploadRecording(config, csrf, recorded);
        }

        // サーバは close を受けると audioStreamEnd を送り、最後の文字起こしを保存してから close を返す。
        // それを待ってから再読み込みしないと、最後の発話が画面に出ない。
        await closed;

        // 送信と close が済むまでボタンは戻さない。この間に生成を始めると、
        // サーバがまだ書いていない最後の発話を欠いたまま議事録を作ってしまう。
        setBusy('recording', false);
    } catch (error) {
        // 片付けのどこで失敗しても、必ず再読み込みまで進める（保存済みの文字起こしを欄に出すため）。
        console.error(error);
        rememberUploadError('media-error', '録音の保存に失敗しました。');
    } finally {
        window.location.reload();
    }
}

// 無音の監視とワークレット、混合グラフを切る。停止と開始失敗の両方で使う。
function disconnectAudio(state) {
    state.silence?.stop();
    state.micSilence?.stop();
    for (const node of [state.workletNode, state.micTap]) {
        node?.port.close();
        node?.disconnect();
    }
    state.silentGain?.disconnect();
    state.mixer?.nodes.forEach(node => node.disconnect());

    state.silence = null;
    state.micSilence = null;
    state.silent = { mixed: false, mic: false };
}

// promise が上限までに解決・失敗しなければ、そのまま先へ進む（fallback で解決する）。
// 停止処理のどの待ちも無限に止まらないようにするための共通ヘルパ。
function withTimeout(promise, timeoutMs, fallback = undefined) {
    if (!promise) {
        return Promise.resolve(fallback);
    }

    return new Promise(resolve => {
        let settled = false;
        const finish = value => {
            if (settled) {
                return;
            }
            settled = true;
            clearTimeout(timer);
            resolve(value);
        };
        const timer = setTimeout(() => finish(fallback), timeoutMs);
        Promise.resolve(promise).then(value => finish(value), () => finish(fallback));
    });
}

// 開始に失敗したら、その時点までに開いたものをすべて閉じる。
// 録音はまだ始まっていないので、3.4 の停止順序から MediaRecorder だけを外した形になる。
async function abortStart(state) {
    // 停止と同じく、自分で閉じる前に state から外す。「録音を開始できませんでした。」を
    // このソケットの close で上書きしない。
    const socket = state.socket;
    state.socket = null;

    if (socket?.readyState === WebSocket.OPEN) {
        socket.close(1000, 'aborted');
    }

    disconnectAudio(state);

    if (state.audioContext?.state !== 'closed') {
        await withTimeout(state.audioContext?.close(), AUDIO_CLOSE_TIMEOUT_MS);
    }

    stopTracks(state);

    state.mixer = null;
    state.audioContext = null;
    state.workletNode = null;
    state.micTap = null;
    state.silentGain = null;
    state.recorder = null;
}

function stopRecorder(state, timeoutMs) {
    const buildBlob = () => state.chunks.length
        ? new Blob(state.chunks, { type: state.mimeType.split(';')[0] })
        : null;

    return new Promise(resolve => {
        if (!state.recorder || state.recorder.state === 'inactive') {
            resolve(buildBlob());
            return;
        }

        let done = false;
        const finish = () => {
            if (done) {
                return;
            }
            done = true;
            clearTimeout(timer);
            resolve(buildBlob());
        };

        // stop イベントが来なくても、上限で手元のチャンクをまとめて返す（無限に待たない）。
        const timer = setTimeout(finish, timeoutMs);
        state.recorder.addEventListener('stop', finish, { once: true });

        try {
            state.recorder.stop();
        } catch {
            finish();
        }
    });
}

async function uploadRecording(config, csrf, blob) {
    const extension = blob.type.includes('mp4') ? 'mp4' : 'webm';
    const form = new FormData();
    form.append('recording', blob, `recording.${extension}`);

    // 回線が詰まって応答が返らなくても、上限で中断して再読み込みまで進める。
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), UPLOAD_TIMEOUT_MS);

    try {
        const response = await fetch(`${config.apiBase}/recording`, {
            method: 'POST',
            headers: { 'X-CSRF-TOKEN': csrf },
            body: form,
            signal: controller.signal
        });

        if (!response.ok) {
            rememberUploadError('media-error', '録音の保存に失敗しました。');
        }
    } catch (error) {
        // 中断・回線断でも投げずに知らせだけ残す（呼び出し側の finally で必ず再読み込みする）。
        console.error(error);
        rememberUploadError('media-error', '録音の保存に失敗しました。');
    } finally {
        clearTimeout(timer);
    }
}

function stopTracks(state) {
    for (const source of state.sources) {
        source.track.stop();
    }

    state.sources = [];
}

// textarea の末尾に追記し、追記した行が見えるところまで送る。
function appendText(field, text) {
    if (!field || !text) {
        return;
    }

    field.value += text;
    field.scrollTop = field.scrollHeight;
}

function setText(element, text) {
    if (element) {
        element.textContent = text;
    }
}

// 起動は宣言をすべて済ませた末尾で行う（const を初期化前に触らないため）
const root = document.getElementById('conai-meeting');
if (root) {
    start(root);
}
