async page => {
    const base = 'http://127.0.0.1:5099';

    // 一覧の行から Id を取る。行の先頭のセルが名前（Task 6 の Index.cshtml）。
    const templateId = name => page.evaluate(n => {
        const row = Array.from(document.querySelectorAll('tr[data-template-id]'))
            .find(tr => tr.cells[0].textContent.trim() === n);
        return row ? row.getAttribute('data-template-id') : null;
    }, name);

    const selectedTemplate = () => page.evaluate(() => {
        const el = document.getElementById('minutes-template');
        return el ? el.options[el.selectedIndex].textContent.trim() : null;
    });

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Register`);
    await page.fill('#Input_Email', 'e2e-template@example.test');
    await page.fill('#Input_Password', 'E2e-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'E2e-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Register'), { timeout: 30000 });

    // ---- (1) 初めて開くと「標準」（既定）と「簡潔」が並ぶ ----

    await page.goto(`${base}/Templates`);
    await page.waitForSelector('tr[data-template-id]', { timeout: 30000 });

    const standardId = await templateId('標準');
    const conciseId = await templateId('簡潔');
    if (!standardId || !conciseId) {
        throw new Error('(1) 初回の配布で「標準」と「簡潔」が並んでいない');
    }

    const standardRow = await page.textContent(`tr[data-template-id="${standardId}"]`);
    if (!standardRow.includes('既定')) {
        throw new Error(`(1) 「標準」に既定の印が無い: ${standardRow}`);
    }

    // ---- (2) テンプレートを作る ----

    await page.goto(`${base}/Templates/Create`);
    await page.fill('#Input_Name', 'E2E 用');
    await page.fill('#Input_Body', '## 要点\n- 要点の見本\n\n## 宿題\n- 宿題（担当者、期限）の見本');
    await page.click('main button[type="submit"]');
    await page.waitForURL(/\/Templates$/, { timeout: 30000 });

    const createdId = await templateId('E2E 用');
    if (!createdId) {
        throw new Error('(2) 作ったテンプレートが一覧に出ない');
    }

    // ---- (3) 会議を作り、添付を足して、テンプレートを「E2E 用」に変える ----

    await page.goto(`${base}/Meetings/Create`);
    if (await selectedTemplate() !== '標準') {
        throw new Error(`(3) 作成画面の初期選択が既定になっていない: ${await selectedTemplate()}`);
    }

    await page.fill('#Input_Title', 'E2E テンプレート指定');
    await page.click('main button[type="submit"]');
    await page.waitForURL(/\/Meetings\/Edit\//, { timeout: 30000 });
    const editUrl = page.url();

    await page.setInputFiles('#media-input', '__REPO_ROOT__/e2e/fixtures/meeting.wav');
    await page.waitForSelector('#media-list tr[data-file-id]', { timeout: 30000 });

    await page.selectOption('#minutes-template', { label: 'E2E 用' });

    // ---- (4) 「保存」を押さずに生成する ----

    await page.click('#generate-button');
    await page.waitForFunction(
        () => document.getElementById('minutes')?.value.includes('決定事項'),
        null,
        { timeout: 120000 });

    const status = await page.textContent('#generation-status');
    if (!status.includes('完了')) {
        throw new Error(`(4) 生成が完了になっていない: ${status}`);
    }

    // ---- (5) 再読み込みしても選択が「E2E 用」のまま ----

    await page.reload();
    await page.waitForSelector('#minutes-template', { timeout: 30000 });
    if (await selectedTemplate() !== 'E2E 用') {
        throw new Error(`(5) 生成で送ったテンプレートが残っていない: ${await selectedTemplate()}`);
    }

    // ---- (6) 既定は削除できない ----

    // 既定の行には削除の導線を出さないので、確認画面の URL を直接開く
    await page.goto(`${base}/Templates/Delete/${standardId}`);
    const defaultDelete = await page.textContent('main');
    if (!defaultDelete.includes('既定のテンプレートは削除できません。先に別のテンプレートを既定にしてください。')) {
        throw new Error(`(6) 既定の削除に断りの文言が出ない: ${defaultDelete}`);
    }

    // ---- (7) 使っているテンプレートを消すと、会議は既定に戻る ----

    await page.goto(`${base}/Templates/Delete/${createdId}`);
    const confirm = await page.textContent('main');
    if (!confirm.includes('このテンプレートを使う会議が 1 件あります。')) {
        throw new Error(`(7) 使用件数が出ていない: ${confirm}`);
    }

    await page.click('main button[type="submit"]');
    await page.waitForURL(/\/Templates$/, { timeout: 30000 });

    await page.goto(editUrl);
    await page.waitForSelector('#minutes-template', { timeout: 30000 });
    if (await selectedTemplate() !== '標準') {
        throw new Error(`(7) 削除後の会議が既定に戻っていない: ${await selectedTemplate()}`);
    }

    // ---- (8) 既定を変えると、次に作る会議の初期選択が変わる ----

    await page.goto(`${base}/Templates`);
    await page.click(`tr[data-template-id="${conciseId}"] button:has-text("既定にする")`);
    await page.waitForURL(/\/Templates$/, { timeout: 30000 });

    await page.goto(`${base}/Meetings/Create`);
    if (await selectedTemplate() !== '簡潔') {
        throw new Error(`(8) 既定を変えても作成画面の初期選択が変わらない: ${await selectedTemplate()}`);
    }

    console.log('09-minutes-template: OK');
}
