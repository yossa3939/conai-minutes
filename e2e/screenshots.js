async page => {
    const base = 'http://127.0.0.1:5098';
    const outDir = '__REPO_ROOT__/e2e/.output/screenshots';
    const widths = [375, 768, 1024, 1440];

    const shoot = async name => {
        for (const width of widths) {
            await page.setViewportSize({ width, height: 900 });
            await page.waitForTimeout(150);

            const overflow = await page.evaluate(
                () => document.documentElement.scrollWidth - document.documentElement.clientWidth);
            if (overflow > 1) {
                throw new Error(`${name} の幅 ${width}px で横スクロールが出た: ${overflow}px`);
            }

            await page.screenshot({ path: `${outDir}/${name}-${width}.png`, fullPage: true });
        }
        await page.setViewportSize({ width: 1440, height: 900 });
    };

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Login`);
    await shoot('login');

    await page.goto(`${base}/Identity/Account/Register`);
    await shoot('register');

    await page.fill('#Input_Email', 'shot@example.test');
    await page.fill('#Input_Password', 'Shot-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'Shot-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Register'), { timeout: 30000 });

    await page.goto(`${base}/Meetings`);
    await shoot('meetings-empty');

    await page.goto(`${base}/Meetings/Create`);
    await shoot('meeting-create');

    // 会議名を空のまま送る。作成ページは _ValidationScriptsPartial を持たないので、
    // サーバが検証して同じページを描き直す。その状態を撮る。
    await page.click('main button[type="submit"]');
    await page.waitForFunction(
        () => (document.getElementById('Input_Title-error')?.textContent ?? '').trim().length > 0,
        null,
        { timeout: 30000 });
    await shoot('meeting-create-error');

    const created = [];
    for (const [title, live] of [['定例会議', false], ['週次レビュー', false], ['録音の打ち合わせ', true]]) {
        await page.goto(`${base}/Meetings/Create`);
        await page.fill('#Input_Title', title);
        if (live) {
            await page.check('#Input_LiveMode_live');
        } else {
            await page.check('#Input_LiveMode_file');
        }
        await page.click('main button[type="submit"]');
        await page.waitForURL(/\/Meetings\/Edit\//, { timeout: 30000 });
        created.push(page.url().split('/').pop());
    }

    await page.goto(`${base}/Meetings`);
    await shoot('meetings-list');

    await page.goto(`${base}/Meetings/Edit/${created[2]}`);
    await shoot('meeting-edit-live');

    await page.goto(`${base}/Meetings/Details/${created[0]}`);
    await shoot('meeting-details');

    await page.goto(`${base}/Meetings/Delete/${created[1]}`);
    await shoot('meeting-delete');

    await page.goto(`${base}/Identity/Account/Manage/Index`);
    await shoot('manage-profile');

    await page.goto(`${base}/Identity/Account/Manage/ChangePassword`);
    await shoot('manage-password');

    await page.goto(`${base}/Identity/Account/Manage/TwoFactorAuthentication`);
    await shoot('manage-two-factor');

    console.log(`screenshots: OK (${outDir})`);
}
