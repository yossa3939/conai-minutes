async page => {
    const base = 'http://127.0.0.1:5099';

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Register`);
    await page.fill('#Input_Email', 'e2e-minutes@example.test');
    await page.fill('#Input_Password', 'E2e-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'E2e-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Register'), { timeout: 30000 });

    await page.goto(`${base}/Meetings/Create`);
    await page.fill('#Input_Title', 'E2E 議事録の編集');
    await page.click('main button[type="submit"]');
    await page.waitForURL(/\/Meetings\/Edit\//, { timeout: 30000 });
    const editUrl = page.url();

    // ---- (a) 議事録が無いときは編集タブが開いていて、プレビューは案内文を出す ----

    if (await page.getAttribute('#minutes-preview-tab', 'aria-pressed') !== 'false') {
        throw new Error('(a) 議事録が無いのにプレビュータブが押された状態になっている');
    }
    if (await page.getAttribute('#minutes-edit-tab', 'aria-pressed') !== 'true') {
        throw new Error('(a) 議事録が無いのに編集タブが押された状態になっていない');
    }
    if (!await page.isHidden('#minutes-preview')) {
        throw new Error('(a) 議事録が無いのにプレビューが表示されている');
    }
    if (await page.isHidden('#minutes-edit')) {
        throw new Error('(a) 議事録が無いのに編集欄が隠れている');
    }
    const placeholder = await page.textContent('#minutes-preview');
    if (!placeholder.includes('議事録はまだありません')) {
        throw new Error(`(a) 空のプレビューに案内文が出ていない: ${placeholder}`);
    }

    // ---- (b) Markdown を書いてヘッダの「保存」で保存すると、プレビューが描画済みの HTML に切り替わる ----

    const markdown = '# 決定事項\n\n- 次回は来週\n- 担当を割り当てる';
    await page.fill('#minutes', markdown);
    await page.click('#save-button');

    // 保存はフォームの POST で同じ URL へ再読み込みされる。知らせ（role="status"）は保存に成功した回だけ出る。
    await page.waitForFunction(
        () => document.querySelector('.alert-success[role="status"]')?.textContent.includes('保存しました'),
        null,
        { timeout: 30000 });

    // 保存して読み直すと、議事録があるのでプレビューが既定で開く。
    if (await page.getAttribute('#minutes-preview-tab', 'aria-pressed') !== 'true') {
        throw new Error('(b) 保存後にプレビュータブへ切り替わっていない');
    }
    if (!await page.isHidden('#minutes-edit')) {
        throw new Error('(b) 保存後も編集欄が表示されたまま');
    }
    if (await page.isHidden('#minutes-preview')) {
        throw new Error('(b) 保存後にプレビューが表示されていない');
    }

    // Markdown ではなく描画済みの HTML（見出し要素）が出ている。
    const previewHtml = await page.innerHTML('#minutes-preview');
    if (!previewHtml.includes('<h1>') || !previewHtml.includes('決定事項')) {
        throw new Error(`(b) プレビューが Markdown を描画していない: ${previewHtml}`);
    }

    // ---- (c) 再読み込みしても保存した議事録が残り、プレビューが既定で開く ----

    await page.reload();
    await page.waitForSelector('#minutes-block', { state: 'visible', timeout: 10000 });

    if (await page.getAttribute('#minutes-preview-tab', 'aria-pressed') !== 'true') {
        throw new Error('(c) 議事録があるのにプレビュータブが既定で開かない');
    }
    if (await page.isHidden('#minutes-preview')) {
        throw new Error('(c) 再読み込み後にプレビューが表示されていない');
    }
    const reloadedPreview = await page.innerHTML('#minutes-preview');
    if (!reloadedPreview.includes('決定事項')) {
        throw new Error(`(c) 保存した議事録が再読み込み後に残っていない: ${reloadedPreview}`);
    }

    // 編集欄にも Markdown が復元されている。
    const restored = await page.inputValue('#minutes');
    if (!restored.includes('# 決定事項') || !restored.includes('担当を割り当てる')) {
        throw new Error(`(c) 編集欄に Markdown が復元されていない: ${restored}`);
    }

    // ---- (d) タブで編集とプレビューを行き来できる ----

    await page.click('#minutes-edit-tab');
    if (await page.isHidden('#minutes-edit')) {
        throw new Error('(d) 編集タブを押しても編集欄が出ない');
    }
    if (!await page.isHidden('#minutes-preview')) {
        throw new Error('(d) 編集タブを押してもプレビューが隠れない');
    }

    await page.click('#minutes-preview-tab');
    if (await page.isHidden('#minutes-preview')) {
        throw new Error('(d) プレビュータブを押してもプレビューが出ない');
    }
    if (!await page.isHidden('#minutes-edit')) {
        throw new Error('(d) プレビュータブを押しても編集欄が隠れない');
    }

    console.log('07-minutes-editor: OK');
}
