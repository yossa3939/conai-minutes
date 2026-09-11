async page => {
    // Issue 2 の回帰防止。録音ファイルのアップロードが失敗しても、
    //   ・編集画面へ必ず戻る（停止処理が finally で再読み込みまで進む）
    //   ・live で書き起こした本文は失われない（サーバが WebSocket 経由で保存済み）
    //   ・保存に失敗したことを利用者に知らせる
    // ことを確かめる。停止後に本文が空のまま止まる不具合を二度と通さないための番人。
    const base = 'http://127.0.0.1:5099';

    await page.addInitScript(() => {
        window.__conaiE2E = { marker: '' };
    });

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Register`);
    await page.fill('#Input_Email', 'e2e-resilience@example.test');
    await page.fill('#Input_Password', 'E2e-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'E2e-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Register'), { timeout: 30000 });

    await page.goto(`${base}/Meetings/Create`);
    await page.fill('#Input_Title', 'E2E 停止のレジリエンス');
    await page.check('#Input_LiveMode_live');
    await page.click('main button[type="submit"]');
    await page.waitForURL(/\/Meetings\/Edit\//, { timeout: 30000 });
    const editUrl = page.url();

    // 録音ファイルのアップロードだけを 500 で落とす。live の文字起こしは WebSocket なので影響を受けない。
    await page.route('**/api/meetings/*/recording', route => {
        if (route.request().method() === 'POST') {
            return route.fulfill({ status: 500, contentType: 'text/plain', body: 'fail' });
        }
        return route.continue();
    });

    await page.check('input[name="capture-source"][value="mic"]');
    await page.waitForSelector('#mic-device', { state: 'visible', timeout: 10000 });

    await page.click('#record-button');
    await page.waitForFunction(
        () => document.getElementById('transcription')?.value.includes('テスト文字起こし'),
        null,
        { timeout: 60000 });

    // 停止すると保存のあとに同じ URL を再読み込みする。再読み込みで __conaiE2E が作り直され marker が空に戻る。
    await page.evaluate(() => { window.__conaiE2E.marker = 'before-stop'; });
    await page.click('#record-button');

    let reloaded = false;
    for (let attempt = 0; attempt < 60; attempt++) {
        const marker = await page
            .evaluate(() => window.__conaiE2E?.marker ?? null)
            .catch(() => null);
        if (marker === '') {
            reloaded = true;
            break;
        }

        await page.waitForTimeout(1000);
    }

    if (!reloaded) {
        throw new Error('アップロードが失敗しても停止処理は編集画面を読み直すべき');
    }
    if (!page.url().startsWith(editUrl)) {
        throw new Error(`停止のあと編集画面に戻らない: ${page.url()}`);
    }

    // 保存に失敗したことを知らせている。
    await page.waitForFunction(
        () => {
            const box = document.getElementById('media-error');
            return box && !box.classList.contains('hidden') && box.textContent.includes('録音の保存に失敗しました');
        },
        null,
        { timeout: 10000 });

    // 肝心の文字起こしは失われていない（サーバが live で保存済み）。
    let transcription = '';
    for (let attempt = 0; attempt < 20; attempt++) {
        transcription = await page.inputValue('#transcription');
        if (transcription.includes('テスト文字起こし')) {
            break;
        }

        await page.waitForTimeout(1000);
        await page.reload();
    }

    if (!transcription.includes('テスト文字起こし')) {
        throw new Error(`アップロード失敗時に文字起こしが失われた: "${transcription}"`);
    }

    await page.unroute('**/api/meetings/*/recording');

    console.log('08-stop-resilience: OK');
}
