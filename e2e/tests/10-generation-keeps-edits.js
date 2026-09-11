async page => {
    const base = 'http://127.0.0.1:5099';

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Register`);
    await page.fill('#Input_Email', 'e2e-generation-edits@example.test');
    await page.fill('#Input_Password', 'E2e-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'E2e-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Register'), { timeout: 30000 });

    await page.goto(`${base}/Meetings/Create`);
    await page.fill('#Input_Title', 'E2E 生成中の編集');
    await page.click('main button[type="submit"]');
    await page.waitForURL(/\/Meetings\/Edit\//, { timeout: 30000 });

    await page.setInputFiles('#media-input', '__REPO_ROOT__/e2e/fixtures/meeting.wav');
    await page.waitForSelector('#media-list tr[data-file-id]', { timeout: 30000 });

    // 生成を待つ間に文字起こしを直す。保存はしないので、この欄だけが編集中になる。
    const edited = '利用者が手で直した文字起こし';
    await page.fill('#transcription', edited);

    await page.click('#generate-button');

    // 編集中の欄があるので、再読み込みはせず知らせだけを出す道に入る。
    await page.waitForFunction(
        () => document.getElementById('generation-status')?.textContent.includes('議事録ができました'),
        null,
        { timeout: 120000 });

    // ---- (a) 手を入れていない議事録の欄には、生成した結果が入っている ----

    const minutes = await page.inputValue('#minutes');
    if (!minutes.includes('決定事項')) {
        throw new Error(`(a) 生成した議事録が編集欄に入っていない: ${minutes}`);
    }

    // ---- (b) 編集中の文字起こしは、生成の結果で上書きされない ----

    if (await page.inputValue('#transcription') !== edited) {
        throw new Error('(b) 編集中の文字起こしが生成の結果で上書きされた');
    }

    // ---- (c) 知らせのとおり保存しても、生成した議事録は消えない ----

    await page.click('#save-button');
    await page.waitForFunction(
        () => document.querySelector('.alert-success[role="status"]')?.textContent.includes('保存しました'),
        null,
        { timeout: 30000 });

    await page.reload();
    await page.waitForSelector('#minutes-block', { state: 'visible', timeout: 10000 });

    const savedMinutes = await page.inputValue('#minutes');
    if (!savedMinutes.includes('決定事項')) {
        throw new Error(`(c) 保存で生成した議事録が消えた: ${savedMinutes}`);
    }

    if (await page.inputValue('#transcription') !== edited) {
        throw new Error('(c) 保存で編集中の文字起こしが残っていない');
    }

    console.log('10-generation-keeps-edits: OK');
}
