async page => {
    const base = 'http://127.0.0.1:5099';
    const heldAtPattern = /^\d{4}\/\d{2}\/\d{2} \d{2}:\d{2}:\d{2}$/;

    await page.context().clearCookies();

    await page.goto(`${base}/Identity/Account/Register`);
    await page.fill('#Input_Email', 'e2e-heldat@example.test');
    await page.fill('#Input_Password', 'E2e-Passw0rd!');
    await page.fill('#Input_ConfirmPassword', 'E2e-Passw0rd!');
    await page.click('main button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Register'), { timeout: 30000 });

    // ---- (a) 作成画面。開催日時は文字入力で、クリックすると flatpickr のカレンダーが開く ----

    await page.goto(`${base}/Meetings/Create`);
    await page.fill('#Input_Title', 'E2E 開催日時');

    const inputType = await page.getAttribute('#Input_HeldAt', 'type');
    if (inputType !== 'text') {
        throw new Error(`(a) 開催日時が文字入力になっていない: type=${inputType}`);
    }

    await page.click('#Input_HeldAt');
    await page.waitForSelector('.flatpickr-calendar.open', { state: 'visible', timeout: 10000 });

    // 曜日の見出しが日本語なら l10n/ja.js が効いている。
    const weekdays = await page.textContent('.flatpickr-calendar.open .flatpickr-weekdays');
    if (!weekdays.includes('日') || !weekdays.includes('月')) {
        throw new Error(`(a) カレンダーが日本語になっていない: ${weekdays}`);
    }

    // 当月の日を 1 つ選ぶと、表示と同じ yyyy/MM/dd HH:mm:ss で値が入る（曜日の「()」は出ない）。
    await page.locator('.flatpickr-calendar.open .flatpickr-day:not(.prevMonthDay):not(.nextMonthDay)').first().click();
    const picked = await page.inputValue('#Input_HeldAt');
    if (!heldAtPattern.test(picked)) {
        throw new Error(`(a) 選んだ日時の書式が違う: ${picked}`);
    }

    // ---- (b) 手で打った値がそのまま保存され、編集画面にも同じ書式で入る ----

    await page.fill('#Input_HeldAt', '2026/08/28 09:05:07');

    // 時刻も選べるカレンダーは日を選んでも閉じず、下の欄とボタンを覆う。外をクリックして閉じ、打った値が保たれることを見る。
    await page.click('h1.page-title');
    await page.waitForSelector('.flatpickr-calendar.open', { state: 'hidden', timeout: 10000 });
    const typed = await page.inputValue('#Input_HeldAt');
    if (typed !== '2026/08/28 09:05:07') {
        throw new Error(`(b) カレンダーを閉じたときに打った値が変わった: ${typed}`);
    }

    await page.check('#Input_LiveMode_live');
    await page.click('main button[type="submit"]');
    await page.waitForURL(/\/Meetings\/Edit\//, { timeout: 30000 });

    const editType = await page.getAttribute('#Basic_HeldAt', 'type');
    if (editType !== 'text') {
        throw new Error(`(b) 編集画面の開催日時が文字入力になっていない: type=${editType}`);
    }

    const stored = await page.inputValue('#Basic_HeldAt');
    if (stored !== '2026/08/28 09:05:07') {
        throw new Error(`(b) 編集画面の開催日時が保存した値と違う: ${stored}`);
    }

    await page.click('#Basic_HeldAt');
    await page.waitForSelector('.flatpickr-calendar.open', { state: 'visible', timeout: 10000 });

    // ---- (c) 一覧も秒まで同じ書式で出す ----

    await page.goto(`${base}/Meetings`);
    const list = await page.textContent('main');
    if (!list.includes('2026/08/28 09:05:07')) {
        throw new Error('(c) 一覧に開催日時が秒まで出ていない');
    }

    console.log('05-held-at-picker: OK');
}
