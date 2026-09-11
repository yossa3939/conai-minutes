async page => {
    const base = 'http://127.0.0.1:5099';
    const meetingId = '00000000-0000-0000-0000-000000000001';

    await page.context().clearCookies();

    await page.goto(`${base}/Meetings`);
    if (!page.url().includes('/Identity/Account/Login')) {
        throw new Error(`未ログインで会議一覧に入れた: ${page.url()}`);
    }

    await page.goto(`${base}/Meetings/Edit/${meetingId}`);
    if (!page.url().includes('/Identity/Account/Login')) {
        throw new Error(`未ログインで編集画面に入れた: ${page.url()}`);
    }

    const api = await page.request.get(
        `${base}/api/meetings/${meetingId}/generation`,
        { maxRedirects: 0 });
    if (![301, 302, 401].includes(api.status())) {
        throw new Error(`API が未認証を拒否しなかった: ${api.status()}`);
    }

    console.log('01-anonymous: OK');
}
