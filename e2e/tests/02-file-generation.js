async page => {
    const base = 'http://127.0.0.1:5099';

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Register`);
    await page.fill('#Input_Email', 'e2e-file@example.test');
    await page.fill('#Input_Password', 'E2e-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'E2e-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Register'), { timeout: 30000 });

    await page.goto(`${base}/Meetings/Create`);
    await page.fill('#Input_Title', 'E2E 添付から生成');
    await page.click('main button[type="submit"]');
    await page.waitForURL(/\/Meetings\/Edit\//, { timeout: 30000 });

    await page.setInputFiles('#media-input', '__REPO_ROOT__/e2e/fixtures/meeting.wav');
    await page.waitForSelector('#media-list tr[data-file-id]', { timeout: 30000 });

    const rows = await page.textContent('#media-list');
    if (!rows.includes('meeting.wav')) {
        throw new Error(`添付が一覧に出ない: ${rows}`);
    }

    await page.click('#generate-button');
    await page.waitForFunction(
        () => document.getElementById('minutes')?.value.includes('決定事項'),
        null,
        { timeout: 120000 });

    const transcription = await page.inputValue('#transcription');
    if (transcription.trim().length === 0) {
        throw new Error('文字起こしが空のまま');
    }

    console.log('02-file-generation: OK');
}
