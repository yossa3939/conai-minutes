async page => {
    const base = 'http://127.0.0.1:5099';

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Register`);
    await page.fill('#Input_Email', 'e2e-live@example.test');
    await page.fill('#Input_Password', 'E2e-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'E2e-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Register'), { timeout: 30000 });

    await page.goto(`${base}/Meetings/Create`);
    await page.fill('#Input_Title', 'E2E Live 録音');
    await page.check('#Input_LiveMode_live');
    await page.click('main button[type="submit"]');
    await page.waitForURL(/\/Meetings\/Edit\//, { timeout: 30000 });

    const editUrl = page.url();

    await page.click('#record-button');
    // live の文字は保存欄（#transcription）へ直接追記する。録音カードの別枠は持たない。
    await page.waitForFunction(
        () => document.getElementById('transcription')?.value.includes('テスト文字起こし'),
        null,
        { timeout: 60000 });

    const recording = await page.evaluate(() => ({
        liveBox: document.getElementById('live-transcript') !== null,
        readOnly: document.getElementById('transcription').readOnly,
        saveDisabled: document.getElementById('save-button').disabled
    }));
    if (recording.liveBox) {
        throw new Error('別枠の live 表示（#live-transcript）が残っている');
    }
    if (!recording.readOnly || !recording.saveDisabled) {
        throw new Error(`録音中に文字起こし欄を手で編集できる: ${JSON.stringify(recording)}`);
    }

    await page.click('#record-button');
    await page.waitForURL(editUrl, { timeout: 60000 });

    let saved = false;
    for (let attempt = 0; attempt < 20; attempt++) {
        const rows = await page.textContent('#media-list');
        const transcription = await page.inputValue('#transcription');
        if (rows.includes('recording-') && transcription.includes('テスト文字起こし')) {
            saved = true;
            break;
        }

        await page.waitForTimeout(1000);
        await page.reload();
    }

    if (!saved) {
        throw new Error('録音ファイルと文字起こしが保存されなかった');
    }

    if (await page.evaluate(() => document.getElementById('transcription').readOnly)) {
        throw new Error('停止したあとも文字起こし欄が読み取り専用のまま');
    }

    console.log('03-live: OK');
}
