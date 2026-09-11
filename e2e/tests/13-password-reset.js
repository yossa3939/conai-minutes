async page => {
    const base = 'http://127.0.0.1:5099';

    await page.context().clearCookies();

    // E2E は Smtp を設定せずに起動するため、未設定の側の経路を確認する

    await page.goto(`${base}/Identity/Account/Login`);

    const link = await page.$('#forgot-password');
    if (link === null) {
        throw new Error('ログイン画面から再設定へ行けない: #forgot-password が無い');
    }

    await link.click();
    await page.waitForURL(u => u.toString().includes('/Identity/Account/ForgotPassword'), { timeout: 30000 });
    if (!page.url().includes('/Identity/Account/ForgotPassword')) {
        throw new Error(`再設定画面に遷移しない: ${page.url()}`);
    }

    const notice = await page.textContent('.alert-note');
    if (!notice.includes('この環境ではメールでの再設定が設定されていません')) {
        throw new Error(`未設定の案内が出ていない: ${notice}`);
    }

    const emailField = await page.$('#Input_Email');
    if (emailField !== null) {
        throw new Error('未設定なのにメールアドレスの入力フォームが出ている');
    }

    console.log('13-password-reset: OK');
}
