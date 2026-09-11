async page => {
    const base = 'http://127.0.0.1:5099';

    // ページの読み込みごとに走る差し替え。アプリ側にテスト用の分岐は入れない。
    await page.addInitScript(() => {
        const e2e = {
            getUserMediaCalls: 0,
            marker: '',
            displayConstraints: null,
            stopTabTrack: () => {}
        };
        window.__conaiE2E = e2e;

        const media = navigator.mediaDevices;
        if (!media) {
            return;
        }

        if (typeof media.getUserMedia === 'function') {
            const realGetUserMedia = media.getUserMedia.bind(media);
            media.getUserMedia = async constraints => {
                e2e.getUserMediaCalls += 1;

                if (location.hash === '#silent-mic') {
                    // 何もつながっていない出力先のストリームは、ずっと無音のまま流れる（06 と同じ作り）。
                    const context = new AudioContext();
                    if (context.state === 'suspended') {
                        await context.resume();
                    }

                    return context.createMediaStreamDestination().stream;
                }

                return realGetUserMedia(constraints);
            };
        }

        // 取り込み元ごとの利得（PC 音声 0.5）を外から確かめるため、作られた GainNode を集めておく。
        e2e.gainNodes = [];
        e2e.gainValues = () => e2e.gainNodes.map(node => node.gain.value);
        const realCreateGain = BaseAudioContext.prototype.createGain;
        BaseAudioContext.prototype.createGain = function () {
            const node = realCreateGain.call(this);
            e2e.gainNodes.push(node);
            return node;
        };

        if (location.hash === '#no-display-media') {
            // getDisplayMedia は MediaDevices.prototype にある。
            // 自身のプロパティで undefined を被せて「持たないブラウザ」を作る。
            Object.defineProperty(media, 'getDisplayMedia', { value: undefined, configurable: true });
            return;
        }

        media.getDisplayMedia = async constraints => {
            // 「画面全体」でもシステム音声を取り込むため systemAudio: 'include' を渡しているかを記録する。
            e2e.displayConstraints = constraints ? JSON.parse(JSON.stringify(constraints)) : null;

            const context = new AudioContext();
            if (context.state === 'suspended') {
                await context.resume();
            }

            const oscillator = context.createOscillator();
            const destination = context.createMediaStreamDestination();
            oscillator.connect(destination);
            oscillator.start();

            const stream = destination.stream;
            const [track] = stream.getAudioTracks();
            e2e.stopTabTrack = () => {
                // track.stop() は ended を発火しない。実機の「共有を停止」に合わせて自分で送る。
                track.stop();
                track.dispatchEvent(new Event('ended'));
            };

            // 映像トラックは含めない。アプリは受け取った映像をその場で止める。
            return stream;
        };
    });

    async function waitForSaved() {
        for (let attempt = 0; attempt < 20; attempt++) {
            const rows = await page.textContent('#media-list');
            const transcription = await page.inputValue('#transcription');
            if (rows.includes('recording-') && transcription.includes('テスト文字起こし')) {
                return true;
            }

            await page.waitForTimeout(1000);
            await page.reload();
        }

        return false;
    }

    // 停止すると保存のあとに同じ URL を再読み込みする。再読み込みで __conaiE2E が作り直され、marker が空に戻るのを待つ。
    async function waitForReload(failure) {
        for (let attempt = 0; attempt < 60; attempt++) {
            const marker = await page
                .evaluate(() => window.__conaiE2E?.marker ?? null)
                .catch(() => null);
            if (marker === '') {
                return;
            }

            await page.waitForTimeout(1000);
        }

        throw new Error(failure);
    }

    // 0.5（-6 dB）の GainNode はマイクと一緒に取り込む PC 音声にだけ付く。その数で取り込み元ごとの利得を確かめる。
    async function countHalfGains() {
        return page.evaluate(() => window.__conaiE2E.gainValues().filter(value => value === 0.5).length);
    }

    async function createLiveMeeting(title) {
        await page.goto(`${base}/Meetings/Create`);
        await page.fill('#Input_Title', title);
        await page.check('#Input_LiveMode_live');
        await page.click('main button[type="submit"]');
        await page.waitForURL(/\/Meetings\/Edit\//, { timeout: 30000 });
        return page.url();
    }

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Register`);
    await page.fill('#Input_Email', 'e2e-tab@example.test');
    await page.fill('#Input_Password', 'E2e-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'E2e-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Register'), { timeout: 30000 });

    // ---- (a) マイク＋PC 音声。PC 音声が止まってもマイクで続く ----

    const mixedUrl = await createLiveMeeting('E2E マイクとPC音声');

    // 案内文は PC 音声を含む選択のときだけ出す。
    await page.check('input[name="capture-source"][value="mic"]');
    if (!await page.isHidden('#capture-source-note')) {
        throw new Error('(a) 「マイクのみ」なのに案内文が出ている');
    }

    await page.check('input[name="capture-source"][value="mic-tab"]');
    await page.waitForSelector('#capture-source-note', { state: 'visible', timeout: 30000 });

    await page.click('#record-button');

    await page.waitForFunction(
        () => document.getElementById('capture-notice')
            ?.textContent.includes('マイク＋PC 音声で録音しています'),
        null,
        { timeout: 30000 });

    const mixedNote = await page.textContent('#capture-source-note');
    for (const line of ['タブか画面全体を選び、音声の共有をオンにしてください', 'ヘッドホンの利用を勧めます']) {
        if (!mixedNote.includes(line)) {
            throw new Error(`(a) PC 音声選択時の案内文が出ない: ${mixedNote}`);
        }
    }

    // 録音中は取り込み元を切り替えられない。
    for (const value of ['mic', 'mic-tab', 'tab']) {
        if (!await page.isDisabled(`input[name="capture-source"][value="${value}"]`)) {
            throw new Error(`(a) 録音中に ${value} を切り替えられる`);
        }
    }

    // PC 音声の取り込みは、共有ダイアログへ systemAudio: 'include' を渡してこそ「画面全体」でも音を拾える。
    const displayConstraints = await page.evaluate(() => window.__conaiE2E.displayConstraints);
    if (displayConstraints?.systemAudio !== 'include') {
        throw new Error(`(a) getDisplayMedia に systemAudio: 'include' を渡していない: ${JSON.stringify(displayConstraints)}`);
    }
    if (!displayConstraints?.audio) {
        throw new Error(`(a) getDisplayMedia に音声の取り込みを要求していない: ${JSON.stringify(displayConstraints)}`);
    }

    // PC 音声はマイクより大きく途切れないので、等倍で足すとマイクの声が埋もれる。PC 音声だけを 0.5 で足す。
    const mixedHalfGains = await countHalfGains();
    if (mixedHalfGains !== 1) {
        throw new Error(`(a) PC 音声の利得 0.5 の GainNode が 1 つでない: ${mixedHalfGains}`);
    }

    await page.waitForFunction(
        () => document.getElementById('transcription')?.value.includes('テスト文字起こし'),
        null,
        { timeout: 60000 });

    // マイクにも PC 音声にも音が届いているので、無音の警告は出ない（開始から 5 秒で判定する）。
    await page.waitForTimeout(6000);
    const mixedNotice = await page.textContent('#capture-notice');
    if (mixedNotice.includes('音が届いていません')) {
        throw new Error(`(a) 音が届いているのに無音の警告が出た: ${mixedNotice}`);
    }

    await page.evaluate(() => window.__conaiE2E.stopTabTrack());

    await page.waitForFunction(
        () => document.getElementById('capture-notice')
            ?.textContent.includes('PC 音声の取り込みが止まりました'),
        null,
        { timeout: 30000 });

    const label = await page.textContent('#record-button');
    if (!label.includes('録音を停止')) {
        throw new Error(`(a) PC 音声が止まった後に録音が続いていない: ${label}`);
    }

    await page.click('#record-button');
    await page.waitForURL(mixedUrl, { timeout: 60000 });

    if (!await waitForSaved()) {
        throw new Error('(a) 録音ファイルと文字起こしが保存されなかった');
    }

    if (!await page.isChecked('input[name="capture-source"][value="mic-tab"]')) {
        throw new Error('(a) 前回の取り込み元が次回の既定になっていない');
    }

    // ---- (b) PC 音声のみ。マイクを開かず、共有が止まると自動で保存する ----

    const tabOnlyUrl = await createLiveMeeting('E2E PC音声のみ');

    await page.check('input[name="capture-source"][value="tab"]');
    await page.click('#record-button');

    await page.waitForFunction(
        () => document.getElementById('transcription')?.value.includes('テスト文字起こし'),
        null,
        { timeout: 60000 });

    const micCalls = await page.evaluate(() => window.__conaiE2E.getUserMediaCalls);
    if (micCalls !== 0) {
        throw new Error(`(b) PC 音声のみなのにマイクを開いた: ${micCalls} 回`);
    }

    const tabHalfGains = await countHalfGains();
    if (tabHalfGains !== 0) {
        throw new Error(`(b) PC 音声のみなのに利得 0.5 の GainNode がある: ${tabHalfGains}`);
    }

    await page.evaluate(() => { window.__conaiE2E.marker = 'before-auto-stop'; });
    await page.evaluate(() => window.__conaiE2E.stopTabTrack());
    await waitForReload('(b) 共有が止まっても自動で停止しなかった');

    if (!page.url().startsWith(tabOnlyUrl)) {
        throw new Error(`(b) 自動停止のあと編集画面に戻らない: ${page.url()}`);
    }

    if (!await waitForSaved()) {
        throw new Error('(b) 録音ファイルと文字起こしが保存されなかった');
    }

    // ---- (c) getDisplayMedia を持たないブラウザ ----

    await page.goto(`${base}/Meetings`);
    await page.goto(`${tabOnlyUrl}#no-display-media`);

    for (const value of ['mic-tab', 'tab']) {
        if (!await page.isDisabled(`input[name="capture-source"][value="${value}"]`)) {
            throw new Error(`(c) getDisplayMedia が無いのに ${value} を選べる`);
        }
    }

    if (await page.isDisabled('input[name="capture-source"][value="mic"]')) {
        throw new Error('(c) 「マイクのみ」まで非活性になっている');
    }

    if (!await page.isChecked('input[name="capture-source"][value="mic"]')) {
        throw new Error('(c) 記憶していた PC 音声の選択が「マイクのみ」に戻っていない');
    }

    await page.waitForSelector('#capture-source-note', { state: 'visible', timeout: 30000 });
    const note = await page.textContent('#capture-source-note');
    if (!note.includes('Chrome または Edge で利用できます')) {
        throw new Error(`(c) 非対応の案内が出ない: ${note}`);
    }

    // ---- (d) マイク＋PC 音声でマイクだけが無音。PC 音声が鳴り続けていても、マイクの無音を知らせる ----

    await page.goto(`${base}/Meetings`);
    await page.goto(`${mixedUrl}#silent-mic`);
    await page.check('input[name="capture-source"][value="mic-tab"]');
    await page.click('#record-button');

    await page.waitForFunction(
        () => document.getElementById('capture-notice')?.textContent.includes('マイクから音が届いていません'),
        null,
        { timeout: 30000 });

    // 混合後の信号には PC 音声が届いているので、両方を疑う警告にはしない。
    const silentNotice = await page.textContent('#capture-notice');
    if (silentNotice.includes('共有したタブや画面の音声を確かめてください')) {
        throw new Error(`(d) PC 音声は鳴っているのに、両方を疑う警告になっている: ${silentNotice}`);
    }

    await page.evaluate(() => { window.__conaiE2E.marker = 'before-stop'; });
    await page.click('#record-button');
    await waitForReload('(d) 停止しても編集画面を読み直さない');

    console.log('04-live-tab-audio: OK');
}
