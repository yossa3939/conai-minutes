async page => {
    const base = 'http://127.0.0.1:5099';

    page.on('dialog', dialog => dialog.accept());

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Register`);
    await page.fill('#Input_Email', 'e2e-chat@example.test');
    await page.fill('#Input_Password', 'E2e-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'E2e-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Register'), { timeout: 30000 });

    // 2 件作るのは、選抜が「関係するほうだけ」を選ぶことを確かめるためである。
    for (const title of ['E2E 予算会議', 'E2E 部門定例']) {
        await page.goto(`${base}/Meetings/Create`);
        await page.fill('#Input_Title', title);
        await page.click('main button[type="submit"]');
        await page.waitForURL(/\/Meetings\/Edit\//, { timeout: 30000 });

        await page.setInputFiles('#media-input', '__REPO_ROOT__/e2e/fixtures/meeting.wav');
        await page.waitForSelector('#media-list tr[data-file-id]', { timeout: 30000 });

        await page.click('#generate-button');

        // 生成した議事録はその時点でサーバに入っている。
        // 読み直しの後だけ真になるしるしを待って、次の操作とぶつからないようにする。
        await page.waitForFunction(
            () => document.getElementById('generate-button')?.dataset.hasMinutes === 'true',
            null,
            { timeout: 120000 });
    }

    // ---- (a) 探す画面に入力欄が出る ----

    await page.goto(`${base}/Chat`);
    await page.waitForSelector('#chat-input', { timeout: 30000 });

    // ---- (b) 語で検索すると、その語を含む会議だけが並ぶ ----

    // 索引はワーカーが作る。静止待ち 10 秒と巡回 10 秒で最大 20 秒遅れて現れるため、
    // 固定の待ち時間ではなく、該当するまで再試行する。
    const deadline = Date.now() + 60000;
    let titles = [];

    while (Date.now() < deadline) {
        await page.fill('#chat-input', '予算');
        await page.click('#chat-send');
        await page.waitForFunction(
            () => document.getElementById('chat-busy')?.classList.contains('hidden') !== false,
            null,
            { timeout: 30000 });

        titles = await page.locator('#search-results li').allTextContents();

        if (titles.length > 0) {
            break;
        }

        await page.waitForTimeout(2000);
    }

    if (titles.length === 0) {
        throw new Error('(b) 60 秒待っても検索が該当しなかった');
    }

    if (!titles.some(text => text.includes('E2E 予算会議'))) {
        throw new Error(`(b) 語を含む会議が出ていない: ${titles.join(' / ')}`);
    }

    if (titles.some(text => text.includes('E2E 部門定例'))) {
        throw new Error(`(b) 関係のない会議が混ざっている: ${titles.join(' / ')}`);
    }

    // ---- (c) 検索結果から回答を作ると、その会議を根拠にした答えが出る ----

    await page.fill('#chat-input', 'この会議で決まったことは何ですか');
    await page.click('#search-answer');
    await page.waitForSelector('#chat-turns [data-turn-id]', { timeout: 60000 });

    const answered = await page.textContent('#chat-turns [data-turn-id]');

    if (!answered.includes('E2E 予算会議')) {
        throw new Error(`(c) 根拠に会議名が出ていない: ${answered}`);
    }

    // ---- (d) 該当が 0 件でも、選抜から回答は作れる ----

    // 「回答を作る」は検索結果の枠の中にある。読み直した直後は枠ごと隠れているため、
    // まず当たらない語で検索して枠を出す。0 件でも押せることは決定どおりである。
    await page.reload();
    await page.waitForSelector('#chat-input', { timeout: 30000 });

    await page.fill('#chat-input', '昼食');
    await page.click('#chat-send');
    await page.waitForSelector('#search-answer:not(.hidden)', { timeout: 30000 });

    if (await page.locator('#search-results li').count() !== 0) {
        throw new Error('(d) 当たらないはずの語で会議が並んだ');
    }

    const summary = await page.textContent('#search-summary');
    if (!summary.includes('該当する会議はありません')) {
        throw new Error(`(d) 該当なしの案内が出ていない: ${summary}`);
    }

    await page.fill('#chat-input', 'E2E 予算会議 で決まったことは何ですか');
    await page.click('#search-answer');
    await page.waitForFunction(
        () => document.querySelectorAll('#chat-turns [data-turn-id]').length >= 2,
        null,
        { timeout: 60000 });

    // ---- (e) 読み直しても会話が残り、「会話を消す」で消える ----

    await page.reload();
    await page.waitForSelector('#chat-turns [data-turn-id]', { timeout: 30000 });

    await page.click('#chat-clear');
    await page.waitForFunction(
        () => document.querySelectorAll('#chat-turns [data-turn-id]').length === 0,
        null,
        { timeout: 30000 });

    await page.reload();
    await page.waitForSelector('#chat-input', { timeout: 30000 });

    if (await page.locator('#chat-turns [data-turn-id]').count() !== 0) {
        throw new Error('(e) 読み直すと消したはずの会話が戻った');
    }

    console.log('11-chat: OK');
}
