async page => {
    const base = 'http://127.0.0.1:5099';

    // ページの読み込みごとに走る差し替え。アプリ側にテスト用の分岐は入れない。
    await page.addInitScript(() => {
        const e2e = { constraints: null, marker: '', requests: 0 };
        window.__conaiE2E = e2e;

        const media = navigator.mediaDevices;
        if (!media || typeof media.getUserMedia !== 'function') {
            return;
        }

        // URL のハッシュで Chrome の状態を演じ分ける。
        //   #prompt-mic: 許可を保存していない（装置名は空。許可を求めて通れば出る）
        //   #denied-mic: ブロック中（装置名は空。許可を求めても通らない）
        //   #silent-mic: 無音のマイク
        const mode = location.hash;
        let granted = mode !== '#prompt-mic' && mode !== '#denied-mic';

        const realEnumerateDevices = media.enumerateDevices.bind(media);
        media.enumerateDevices = async () => {
            const devices = await realEnumerateDevices();
            if (granted) {
                return devices;
            }

            // 許可の前の Chrome は、種類ごとに deviceId と label が空の 1 件だけを返す。
            return devices.some(device => device.kind === 'audioinput')
                ? [{ kind: 'audioinput', deviceId: '', label: '', groupId: '' }]
                : [];
        };

        if (typeof navigator.permissions?.query === 'function') {
            const realQuery = navigator.permissions.query.bind(navigator.permissions);
            navigator.permissions.query = descriptor => descriptor?.name === 'microphone'
                ? Promise.resolve({ state: mode === '#denied-mic' ? 'denied' : granted ? 'granted' : 'prompt' })
                : realQuery(descriptor);
        }

        const realGetUserMedia = media.getUserMedia.bind(media);
        media.getUserMedia = async constraints => {
            e2e.constraints = JSON.parse(JSON.stringify(constraints));
            e2e.requests += 1;

            if (mode === '#denied-mic') {
                throw new DOMException('Permission denied', 'NotAllowedError');
            }

            granted = true;

            if (mode === '#silent-mic') {
                // 何もつながっていない出力先のストリームは、ずっと無音のまま流れる。
                const context = new AudioContext();
                if (context.state === 'suspended') {
                    await context.resume();
                }

                return context.createMediaStreamDestination().stream;
            }

            return realGetUserMedia(constraints);
        };

        // 取り込み元ごとの利得を外から確かめるため、作られた GainNode を集めておく（04 と同じ作り）。
        e2e.gainNodes = [];
        e2e.gainValues = () => e2e.gainNodes.map(node => node.gain.value);
        const realCreateGain = BaseAudioContext.prototype.createGain;
        BaseAudioContext.prototype.createGain = function () {
            const node = realCreateGain.call(this);
            e2e.gainNodes.push(node);
            return node;
        };
    });

    // live の文字は保存欄（#transcription）へ直接追記する。
    async function waitForTranscript() {
        await page.waitForFunction(
            () => document.getElementById('transcription')?.value.includes('テスト文字起こし'),
            null,
            { timeout: 60000 });
    }

    async function waitForDeviceOptions(scenario) {
        try {
            await page.waitForFunction(
                () => [...document.querySelectorAll('#mic-device option')].some(option => option.textContent.includes('Fake')),
                null,
                { timeout: 30000 });
        } catch {
            throw new Error(`${scenario} 一覧に名前入りのマイクが並ばない`);
        }
    }

    // 停止すると保存のあとに同じ URL を再読み込みする。URL は変わらないので、再読み込みで __conaiE2E が作り直されるのを待つ。
    async function stopAndReturn(editUrl) {
        await page.evaluate(() => { window.__conaiE2E.marker = 'before-stop'; });
        await page.click('#record-button');

        let reloaded = false;
        for (let attempt = 0; attempt < 60; attempt++) {
            const marker = await page
                .evaluate(() => window.__conaiE2E?.marker ?? null)
                .catch(() => null);
            if (marker === '') {
                reloaded = true;
                break;
            }

            await page.waitForTimeout(1000);
        }

        if (!reloaded) {
            throw new Error('停止しても編集画面を読み直さない');
        }

        if (!page.url().startsWith(editUrl)) {
            throw new Error(`停止のあと編集画面に戻らない: ${page.url()}`);
        }
    }

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Register`);
    await page.fill('#Input_Email', 'e2e-mic@example.test');
    await page.fill('#Input_Password', 'E2e-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'E2e-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Register'), { timeout: 30000 });

    await page.goto(`${base}/Meetings/Create`);
    await page.fill('#Input_Title', 'E2E マイク選択');
    await page.check('#Input_LiveMode_live');
    await page.click('main button[type="submit"]');
    await page.waitForURL(/\/Meetings\/Edit\//, { timeout: 30000 });
    const editUrl = page.url();

    // ---- (a) 選択欄。画面を開いた時点で選べて、一覧に装置名が並ぶ。「PC 音声のみ」ではマイクを使わないので隠す ----

    // 取り込み元は localStorage に残る（前のシナリオが「PC 音声のみ」で終わる）ので、先に「マイクのみ」に揃える。
    await page.check('input[name="capture-source"][value="mic"]');
    await page.waitForSelector('#mic-device', { state: 'visible', timeout: 10000 });
    const firstOption = await page.textContent('#mic-device option >> nth=0');
    if (!firstOption.includes('ブラウザの既定のマイク')) {
        throw new Error(`(a) 先頭の選択肢が既定のマイクでない: ${firstOption}`);
    }

    // 録音を始めなくても、一覧に名前入りの選択肢が並び、選べる状態になっている。
    await waitForDeviceOptions('(a)');
    if (await page.isDisabled('#mic-device')) {
        throw new Error('(a) 録音していないのにマイクの選択欄が非活性になっている');
    }

    await page.check('input[name="capture-source"][value="tab"]');
    if (!await page.isHidden('#mic-device-field')) {
        throw new Error('(a) 「PC 音声のみ」なのにマイクの選択欄が出ている');
    }

    await page.check('input[name="capture-source"][value="mic"]');
    await page.waitForSelector('#mic-device-field', { state: 'visible', timeout: 10000 });

    // ---- (b) 既定のマイクで録音。使っているマイクの名前が出て、録音中は選び直せない ----

    await page.click('#record-button');
    await page.waitForFunction(
        () => document.getElementById('capture-notice')?.textContent.includes('使っているマイク: '),
        null,
        { timeout: 30000 });

    if (!await page.isDisabled('#mic-device')) {
        throw new Error('(b) 録音中にマイクを選び直せる');
    }

    // 0.5（-6 dB）の利得は PC 音声にだけ付ける。マイクのみでは等倍で送る。
    const halfGains = await page.evaluate(() => window.__conaiE2E.gainValues().filter(value => value === 0.5).length);
    if (halfGains !== 0) {
        throw new Error(`(b) マイクのみなのに利得 0.5 の GainNode がある: ${halfGains}`);
    }

    await waitForTranscript();

    // 音が届いているので、無音の警告は出ない（開始から 5 秒で判定する）。
    await page.waitForTimeout(6000);
    const notice = await page.textContent('#capture-notice');
    if (notice.includes('音が届いていません')) {
        throw new Error(`(b) 音が届いているのに無音の警告が出た: ${notice}`);
    }

    await stopAndReturn(editUrl);

    // ---- (c) マイクを選ぶと、その deviceId で開き、選択は次回も残る ----

    await waitForDeviceOptions('(c)');
    const chosen = await page.evaluate(() => {
        const select = document.getElementById('mic-device');
        const option = [...select.options].find(item => item.value !== '');
        return { value: option.value, label: option.textContent };
    });
    await page.selectOption('#mic-device', chosen.value);

    await page.reload();
    await waitForDeviceOptions('(c)');

    // Chrome は許可を保存していないと読み込みごとに deviceId を作り直す。装置名で選び直されることを見る。
    const restored = await page.evaluate(() => {
        const select = document.getElementById('mic-device');
        return { value: select.value, label: select.selectedOptions[0]?.textContent ?? '' };
    });
    if (restored.value === '' || restored.label !== chosen.label) {
        throw new Error(`(c) 選んだマイクが次回の既定になっていない: ${JSON.stringify(restored)}`);
    }

    await page.click('#record-button');
    await page.waitForFunction(
        label => document.getElementById('capture-notice')?.textContent.includes(`使っているマイク: ${label}`),
        chosen.label,
        { timeout: 30000 });

    const constraints = await page.evaluate(() => window.__conaiE2E.constraints);
    if (constraints?.audio?.deviceId?.exact !== restored.value) {
        throw new Error(`(c) 選んだマイクの deviceId で開いていない: ${JSON.stringify(constraints)}`);
    }

    await waitForTranscript();
    await stopAndReturn(editUrl);

    // ---- (d) 無音のマイク。開始から 5 秒で警告する ----

    await page.goto(`${base}/Meetings`);
    await page.goto(`${editUrl}#silent-mic`);
    await page.waitForSelector('#mic-device', { state: 'visible', timeout: 10000 });
    await page.selectOption('#mic-device', '');

    await page.click('#record-button');
    await page.waitForFunction(
        () => document.getElementById('capture-notice')?.textContent.includes('マイクから音が届いていません'),
        null,
        { timeout: 30000 });

    await stopAndReturn(editUrl);

    // ---- (e) 許可を保存していない Chrome。画面を開いた時点で一度だけ許可を求め、一覧を埋める ----

    await page.goto(`${base}/Meetings`);
    await page.goto(`${editUrl}#prompt-mic`);
    await page.waitForSelector('#mic-device', { state: 'visible', timeout: 10000 });
    await waitForDeviceOptions('(e)');

    const prompted = await page.evaluate(() => ({
        disabled: document.getElementById('mic-device').disabled,
        requests: window.__conaiE2E.requests,
        audio: window.__conaiE2E.constraints?.audio ?? null
    }));
    if (prompted.disabled) {
        throw new Error('(e) 許可を求めたあとマイクの選択欄が非活性のまま');
    }
    if (prompted.requests !== 1 || prompted.audio === null) {
        throw new Error(`(e) 一覧を出すためのマイクの利用の要求が 1 回でない: ${JSON.stringify(prompted)}`);
    }

    // 許可のあとに一覧が埋まれば、注記は要らない（偽マイクの既定はステレオ ミキサーではない）。
    if (!await page.isHidden('#mic-device-note')) {
        throw new Error(`(e) 一覧が出たのに注記が残っている: ${await page.textContent('#mic-device-note')}`);
    }

    // ---- (f) マイクの利用がブロックされた Chrome。許可は求めず、一覧を出せない理由を注記する ----

    await page.goto(`${base}/Meetings`);
    await page.goto(`${editUrl}#denied-mic`);
    await page.waitForSelector('#mic-device', { state: 'visible', timeout: 10000 });
    await page.waitForFunction(
        () => document.getElementById('mic-device-note')?.textContent.includes('ブロック'),
        null,
        { timeout: 10000 });

    const blocked = await page.evaluate(() => ({
        disabled: document.getElementById('mic-device').disabled,
        options: document.querySelectorAll('#mic-device option').length,
        requests: window.__conaiE2E.requests,
        note: document.getElementById('mic-device-note').textContent
    }));
    if (blocked.disabled) {
        throw new Error('(f) ブロック中でもマイクの選択欄は活性のままにする');
    }
    if (blocked.options !== 1 || blocked.requests !== 0) {
        throw new Error(`(f) ブロック中の一覧か許可の要求がおかしい: ${JSON.stringify(blocked)}`);
    }
    if (!blocked.note.includes('サイト情報')) {
        throw new Error(`(f) 許可の直し方を注記していない: ${blocked.note}`);
    }

    console.log('06-mic-device: OK');
}
