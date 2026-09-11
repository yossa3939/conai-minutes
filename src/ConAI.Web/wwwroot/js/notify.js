// テスト送信（通知の編集画面）と手動送信（会議の閲覧画面）のボタン。
// 通信と要素は引数で受け取るので、DOM の無い node:test でもそのまま試せる。

export const DEFAULT_ERROR_MESSAGE = '送信できませんでした。しばらくしてからもう一度お試しください。';
export const NETWORK_ERROR_MESSAGE = 'サーバに繋がりませんでした。通信を確かめてもう一度お試しください。';
export const RATE_LIMIT_MESSAGE = '送信の間隔が短すぎます。1 分ほど待ってからお試しください。';
export const SENDING_MESSAGE = '送信しています…';

const TONE_CLASSES = {
    note: 'alert-note',
    success: 'alert-success',
    error: 'alert-destructive'
};

/**
 * 送信を頼む。
 *
 * @param {object} options
 * @param {string} options.url 送り先
 * @param {string} options.csrfToken CSRF トークン
 * @param {(url: string, init: object) => Promise<{ ok: boolean, status: number, json: () => Promise<object> }>} options.fetch
 * @returns {Promise<{ ok: boolean, message: string }>}
 */
export async function postNotify({ url, csrfToken, fetch }) {
    let response;
    try {
        response = await fetch(url, {
            method: 'POST',
            headers: { 'X-CSRF-TOKEN': csrfToken }
        });
    } catch {
        return { ok: false, message: NETWORK_ERROR_MESSAGE };
    }

    // レート制限の応答は本文を持たない。既定の文言では原因が伝わらないので先に分ける。
    if (response.status === 429) {
        return { ok: false, message: RATE_LIMIT_MESSAGE };
    }

    let body = null;
    try {
        body = await response.json();
    } catch {
        // 本文が JSON でない応答（500 など）は既定の文言に落とす
    }

    const message = typeof body?.message === 'string' && body.message.length > 0
        ? body.message
        : DEFAULT_ERROR_MESSAGE;

    // 状態符号だけでは足りない。テスト送信は宛先に断られても 200 のまま ok:false を返す
    return { ok: response.ok && body?.ok === true, message };
}

/**
 * ボタン 1 つと結果の欄 1 つを受け持つ操作子。
 *
 * @param {object} options
 * @param {object} options.button 押すボタン
 * @param {object} options.result 結果を出す欄
 * @param {(url: string, init: object) => Promise<object>} options.fetch
 * @param {string} options.csrfToken
 * @param {string} options.url
 */
export function createNotifyController({ button, result, fetch, csrfToken, url }) {
    function show(message, tone) {
        result.textContent = message;
        result.classList.remove('hidden', TONE_CLASSES.note, TONE_CLASSES.success, TONE_CLASSES.error);
        result.classList.add(TONE_CLASSES[tone]);
    }

    async function run() {
        // 押した回数だけ送ると、同じ通知がチャットに並ぶ。応答が返るまで塞ぐ。
        button.disabled = true;
        show(SENDING_MESSAGE, 'note');

        const outcome = await postNotify({ url, csrfToken, fetch });

        button.disabled = false;
        show(outcome.message, outcome.ok ? 'success' : 'error');
    }

    return { run };
}

function start(root) {
    const button = root.querySelector('[data-notify-button]');
    const result = root.querySelector('[data-notify-result]');
    if (!button || !result) {
        return;
    }

    const controller = createNotifyController({
        button,
        result,
        fetch: window.fetch.bind(window),
        csrfToken: document.querySelector('meta[name="csrf"]')?.content ?? '',
        url: root.dataset.notifyUrl
    });

    button.addEventListener('click', () => controller.run());
}

// 起動は宣言をすべて済ませた末尾で行う（const を初期化前に触らないため）。
// node:test は document を持たず、素の参照は ReferenceError になるので typeof で確かめてから触る。
const notifyRoots = typeof document === 'undefined' ? [] : document.querySelectorAll('[data-notify]');
for (const root of notifyRoots) {
    start(root);
}
