async page => {
    const base = 'http://127.0.0.1:5099';
    const url = 'https://hooks.slack.com/services/T00000000/B00000000/e2e00000000000000000001';

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Register`);
    await page.fill('#Input_Email', 'e2e-notify@example.test');
    await page.fill('#Input_Password', 'E2e-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'E2e-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(u => !u.toString().includes('/Register'), { timeout: 30000 });

    // ---- (a) 宛先を登録すると一覧に並ぶ ----

    await page.goto(`${base}/Notifications`);
    await page.click('a[href="/Notifications/Create"]');
    await page.waitForSelector('#Input_Name', { timeout: 30000 });

    await page.fill('#Input_Name', 'E2E 通知先');
    await page.selectOption('#Input_Kind', 'Slack');
    await page.fill('#Input_Url', url);
    await page.click('main button[type="submit"]');
    await page.waitForSelector('tr[data-endpoint-id]', { timeout: 30000 });

    const listed = await page.textContent('tr[data-endpoint-id]');
    if (!listed.includes('E2E 通知先')) {
        throw new Error(`(a) 登録した宛先が一覧に出ていない: ${listed}`);
    }

    if (!listed.includes('Slack')) {
        throw new Error(`(a) 種別が一覧に出ていない: ${listed}`);
    }

    // 平文の URL を画面に出さない
    if (listed.includes(url)) {
        throw new Error('(a) 宛先 URL が平文で一覧に出ている');
    }

    // ---- (b) テスト送信が成功として表示される ----

    await page.click('tr[data-endpoint-id] a[href^="/Notifications/Edit/"]');
    await page.waitForSelector('[data-notify-button]', { timeout: 30000 });

    await page.click('[data-notify-button]');
    await page.waitForFunction(
        () => {
            const result = document.querySelector('[data-notify-result]');

            return result !== null && result.classList.contains('alert-success');
        },
        null,
        { timeout: 30000 });

    const sent = await page.textContent('[data-notify-result]');
    if (!sent.includes('テスト通知を送りました')) {
        throw new Error(`(b) テスト送信の成功が出ていない: ${sent}`);
    }

    // ---- (c) 送信の結果が一覧に残る ----

    await page.goto(`${base}/Notifications`);
    await page.waitForSelector('tr[data-endpoint-id]', { timeout: 30000 });

    const recorded = await page.textContent('tr[data-endpoint-id]');
    if (!recorded.includes('成功')) {
        throw new Error(`(c) 最終送信の結果が一覧に残っていない: ${recorded}`);
    }

    // ---- (d) 削除すると一覧から消える ----

    await page.click('tr[data-endpoint-id] a[href^="/Notifications/Delete/"]');
    await page.waitForSelector('main button[type="submit"]', { timeout: 30000 });
    await page.click('main button[type="submit"]');
    await page.waitForFunction(
        () => document.querySelectorAll('tr[data-endpoint-id]').length === 0,
        null,
        { timeout: 30000 });

    console.log('12-notifications: OK');
}
