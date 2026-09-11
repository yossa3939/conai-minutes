// 議事録への質問の送受信と描画。
// 通信と描画は引数で受け取った fetch と doc を使うので、DOM の無い node:test でもそのまま試せる。

export const DEFAULT_ERROR_MESSAGE = '答えを作れませんでした。しばらくしてからもう一度お試しください。';
export const NETWORK_ERROR_MESSAGE = 'サーバに繋がりませんでした。通信を確かめてもう一度お試しください。';
export const RATE_LIMIT_MESSAGE = '質問の間隔が短すぎます。1 分ほど待ってからお試しください。';
export const NO_MEETINGS_MESSAGE = '議事録がまだありません。会議を作って議事録を生成すると質問できます。';
export const EMPTY_QUESTION_MESSAGE = '質問を入力してください。';
export const BUSY_MESSAGE = '関係する議事録を探しています…';
export const SEARCH_BUSY_MESSAGE = '議事録を探しています…';
export const EMPTY_SEARCH_MESSAGE = '検索語を入力してください。';
export const NO_RESULTS_MESSAGE =
    '該当する会議はありません。『回答を作る』を押すと、AI が全体から探します。';
export const CLEAR_CONFIRM = '会話をすべて消します。よろしいですか。';

/** 根拠にできる会議の数。サーバ側の SearchLimits.MaxAnswerMeetings と同じ値である。 */
export const MAX_ANSWER_MEETINGS = 10;

/** 「さらに表示」を止めるページ。サーバ側の SearchLimits.MaxPage と同じ値である。 */
const MAX_PAGE = 50;

/**
 * 該当が無かったときの案内。何を聞いたかを添えて、言い換えの手がかりにする。
 *
 * @param {string} question 送った質問
 * @returns {string}
 */
export function noMatchMessage(question) {
    return `「${question}」に関係する議事録が見つかりませんでした。`
        + '言い方を変えるか、会議名や時期を足してみてください。';
}

/**
 * 質問を送る。
 *
 * @param {object} options
 * @param {string} options.url 送り先
 * @param {string} options.csrfToken CSRF トークン
 * @param {string} options.question 利用者が入力した質問
 * @param {string[]} [options.meetingIds] 根拠にする会議の ID。空ならサーバが全体から選ぶ
 * @param {(url: string, init: object) => Promise<{ ok: boolean, status: number, json: () => Promise<object> }>} options.fetch
 * @returns {Promise<{ ok: true, status: string, turn: object | null, notes: string[] } | { ok: false, message: string }>}
 */
export async function postQuestion({ url, csrfToken, question, meetingIds = [], fetch }) {
    let response;
    try {
        response = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-CSRF-TOKEN': csrfToken
            },
            body: JSON.stringify({ question, meetingIds })
        });
    } catch {
        return { ok: false, message: NETWORK_ERROR_MESSAGE };
    }

    if (!response.ok) {
        return { ok: false, message: await readMessage(response) };
    }

    const body = await response.json();

    return {
        ok: true,
        status: body.status,
        turn: body.turn ?? null,
        notes: Array.isArray(body.notes) ? body.notes : []
    };
}

/**
 * 会話を全件消す。
 *
 * @param {object} options
 * @param {string} options.url 送り先
 * @param {string} options.csrfToken CSRF トークン
 * @param {(url: string, init: object) => Promise<{ ok: boolean, status: number }>} options.fetch
 * @returns {Promise<{ ok: true } | { ok: false, message: string }>}
 */
export async function clearChat({ url, csrfToken, fetch }) {
    let response;
    try {
        response = await fetch(url, {
            method: 'DELETE',
            headers: { 'X-CSRF-TOKEN': csrfToken }
        });
    } catch {
        return { ok: false, message: NETWORK_ERROR_MESSAGE };
    }

    return response.ok ? { ok: true } : { ok: false, message: await readMessage(response) };
}

/**
 * 検索を送る。失敗は戻り値で返さず Error で投げる。呼び出し側で文言を選んで出す。
 *
 * @param {object} options
 * @param {string} options.url 送り先
 * @param {string} options.csrfToken CSRF トークン
 * @param {string} options.query 検索語
 * @param {number} options.page ページ番号（1 始まり）
 * @param {(url: string, init: object) => Promise<{ ok: boolean, status: number, json: () => Promise<object> }>} options.fetch
 * @returns {Promise<object>} 検索の応答本文
 */
export async function postSearch({ url, csrfToken, query, page, fetch }) {
    const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken },
        body: JSON.stringify({ query, page })
    });

    if (!response.ok) {
        throw new Error(await readMessage(response));
    }

    return response.json();
}

/**
 * 押したときに実際に送る件数を名乗る。該当件数をそのまま書くと、全件を読んで答えると誤解させる。
 *
 * @param {number} loaded 表示中の結果の件数
 * @returns {string}
 */
export function answerButtonLabel(loaded) {
    // 0 件のときは選抜に任せるため、件数を名乗らない。
    return loaded === 0 ? '回答を作る' : `上位 ${Math.min(loaded, MAX_ANSWER_MEETINGS)} 件から回答を作る`;
}

// サーバは失敗を { message } で返す。本文を持たない応答もあるので、読めなければ既定の文言に落とす。
async function readMessage(response) {
    // レート制限の応答は本文を持たない。既定の文言では原因が伝わらないので先に分ける。
    if (response.status === 429) {
        return RATE_LIMIT_MESSAGE;
    }

    try {
        const body = await response.json();
        if (body && typeof body.message === 'string' && body.message.length > 0) {
            return body.message;
        }
    } catch {
        // 本文が JSON でない場合は既定の文言を使う
    }

    return DEFAULT_ERROR_MESSAGE;
}

/**
 * 1 往復を描く。
 *
 * @param {object} turn サーバが返した往復
 * @param {{ createElement: (tagName: string) => object }} doc 要素を作る道具
 * @returns {object}
 */
export function renderTurn(turn, doc) {
    const block = doc.createElement('div');
    block.className = 'border-l-2 border-border pl-4';
    block.dataset.turnId = turn.id;

    const time = doc.createElement('p');
    time.className = 'text-sm text-muted-foreground';
    time.textContent = turn.createdAtText;

    const question = doc.createElement('p');
    question.className = 'mt-1 font-medium';
    // textContent で入れる。質問は利用者が書いた生の文字列で、HTML として解釈させない。
    question.textContent = turn.question;

    const answer = doc.createElement('div');
    answer.className = 'conai-prose mt-2';
    // 答えはサーバの MarkdownRenderer（DisableHtml 済み）が作った HTML なので、そのまま入れてよい。
    answer.innerHTML = turn.answerHtml;

    block.append(time, question, answer);

    const sources = Array.isArray(turn.sources) ? turn.sources : [];
    if (sources.length > 0) {
        block.append(renderSources(sources, doc));
    }

    return block;
}

function renderSources(sources, doc) {
    const line = doc.createElement('p');
    line.className = 'mt-2 text-sm text-muted-foreground';

    const label = doc.createElement('span');
    label.textContent = '根拠: ';
    line.append(label);

    let first = true;
    for (const source of sources) {
        if (!first) {
            // 2 件目以降は「、」で区切る。リロード後の Razor 側の描画と同じ見た目に揃える。
            const separator = doc.createElement('span');
            separator.textContent = '、';
            line.append(separator);
        }
        first = false;

        if (source.meetingId) {
            const link = doc.createElement('a');
            link.className = 'btn-link';
            link.href = `/Meetings/Details/${source.meetingId}`;
            link.textContent = source.title;
            line.append(link);
        } else {
            // 会議が消えていると開く先が無い。写し取った名前だけを残す（決定 8）。
            const gone = doc.createElement('span');
            gone.textContent = source.title;
            line.append(gone);
        }
    }

    return line;
}

/**
 * 検索結果 1 件を描く。
 *
 * @param {object} result サーバが返した検索結果
 * @param {{ createElement: (tagName: string) => object }} doc 要素を作る道具
 * @returns {object}
 */
export function renderResult(result, doc) {
    const item = doc.createElement('li');
    item.className = 'border-t border-border pt-3';
    item.dataset.meetingId = result.meetingId;

    const link = doc.createElement('a');
    link.className = 'btn-link font-medium';
    // 会話の根拠と同じく、Details は id を経路で受け取る。照会文字列では開かない。
    link.href = `/Meetings/Details/${encodeURIComponent(result.meetingId)}`;
    link.textContent = result.title;
    item.append(link);

    if (result.heldAtText) {
        const heldAt = doc.createElement('p');
        heldAt.className = 'text-sm text-muted-foreground';
        heldAt.textContent = result.heldAtText;
        item.append(heldAt);
    }

    const excerpt = doc.createElement('p');
    // 抜粋はサーバが組み立てた表示文字である。HTML として入れない。
    excerpt.className = result.matchedIn === 'minutes' ? 'mt-1 text-sm' : 'mt-1 text-sm text-muted-foreground';
    excerpt.textContent = result.excerpt;
    item.append(excerpt);

    return item;
}

/**
 * 画面の状態を持つ操作子。要素も通信も引数で受け取るので、単体で試せる。
 *
 * @param {object} options
 * @param {object} options.elements turns/empty/input/send/clear/busy/notice/error/searchPanel/searchSummary/searchResults/searchMore/searchAnswer
 * @param {{ createElement: (tagName: string) => object }} options.doc
 * @param {(url: string, init: object) => Promise<object>} options.fetch
 * @param {string} options.csrfToken
 * @param {string} options.url 質問の送り先
 * @param {string} options.searchUrl 検索の送り先
 * @param {(message: string) => boolean} options.confirm
 */
export function createChatController({ elements, doc, fetch, csrfToken, url, searchUrl, confirm }) {
    const {
        turns, empty, input, send, clear, busy, notice, error,
        searchPanel, searchSummary, searchResults, searchMore, searchAnswer
    } = elements;
    let working = false;
    // 検索の状態。「さらに表示」と「回答を作る」が直前の検索結果を参照するために持つ。
    let query = '';
    let loaded = [];
    let page = 0;
    let total = 0;

    function setBusy(value, message = BUSY_MESSAGE) {
        working = value;
        send.disabled = value;
        // 送信中に履歴を消すと、直後に返る答えだけが残った画面になる。
        clear.disabled = value;
        // 立てるときだけ文言を差し替える。探すのと答えを作るのは待ち時間の意味が違う。
        busy.textContent = message;
        busy.classList.toggle('hidden', !value);
        turns.setAttribute('aria-busy', value ? 'true' : 'false');
    }

    function show(element, message) {
        element.textContent = message;
        element.classList.remove('hidden');
    }

    function hide(element) {
        element.textContent = '';
        element.classList.add('hidden');
    }

    function toggleEmpty() {
        empty.classList.toggle('hidden', turns.childElementCount > 0);
    }

    function scrollToLatest() {
        // 一覧は高さ上限つきのスクロール領域なので、放っておくと新しい答えが下に隠れる。
        turns.scrollTop = turns.scrollHeight;
    }

    /** 検索の応答を画面へ反映する。append が true なら追記、false なら置き換える。 */
    function paint(body, append) {
        const items = body.results.map(result => renderResult(result, doc));

        if (append) {
            searchResults.append(...items);
            loaded.push(...body.results.map(result => result.meetingId));
        } else {
            searchResults.replaceChildren(...items);
            loaded = body.results.map(result => result.meetingId);
        }

        page = body.page;
        total = body.total;

        // 既存の show(element, message) は textContent を上書きするため、
        // 文言を持たない入れ物とボタンは hidden の付け外しだけで出し入れする
        searchPanel.classList.remove('hidden');
        searchSummary.textContent = total === 0
            ? NO_RESULTS_MESSAGE
            : `${total} 件のうち ${loaded.length} 件を表示しています`;

        // 索引の作成中であることを黙っていると、利用者は「該当が少ない」ではなく
        // 「この語では出ない」と読む。
        if (body.notes.length > 0) {
            show(notice, body.notes.join(' '));
        } else {
            hide(notice);
        }

        // 該当が 0 件でも「回答を作る」は押せるままにする。利用者の行き先を塞がない。
        show(searchAnswer, answerButtonLabel(loaded.length));

        searchMore.classList.toggle('hidden', loaded.length >= total || page >= MAX_PAGE);
    }

    async function search() {
        // Ctrl+Enter は disabled のボタンを経由しないので、ここで送信中を見て弾く。
        if (working) {
            return;
        }

        query = input.value.trim();
        if (query.length === 0) {
            show(error, EMPTY_SEARCH_MESSAGE);
            return;
        }

        hide(error);
        hide(notice);
        setBusy(true, SEARCH_BUSY_MESSAGE);

        let body;
        try {
            body = await postSearch({ url: searchUrl, csrfToken, query, page: 1, fetch });
        } catch (failure) {
            setBusy(false);
            // ボタンを無効にした時点で、フォーカスは body へ落ちている。そのまま次を打てるように戻す。
            input.focus();
            // postSearch は失敗を Error で投げる。通信そのものの失敗は文言を持たないので既定に落とす。
            show(error, failure instanceof Error && failure.message ? failure.message : NETWORK_ERROR_MESSAGE);
            return;
        }

        setBusy(false);
        input.focus();
        paint(body, false);
    }

    async function more() {
        if (working) {
            return;
        }

        hide(error);
        setBusy(true, SEARCH_BUSY_MESSAGE);

        let body;
        try {
            body = await postSearch({ url: searchUrl, csrfToken, query, page: page + 1, fetch });
        } catch (failure) {
            setBusy(false);
            // postSearch は失敗を Error で投げる。通信そのものの失敗は文言を持たないので既定に落とす。
            show(error, failure instanceof Error && failure.message ? failure.message : NETWORK_ERROR_MESSAGE);
            return;
        }

        setBusy(false);
        paint(body, true);
    }

    async function askQuestion(meetingIds) {
        // Ctrl+Enter は disabled のボタンを経由しないので、ここで送信中を見て弾く。
        if (working) {
            return;
        }

        const question = input.value.trim();
        if (question.length === 0) {
            show(error, EMPTY_QUESTION_MESSAGE);
            return;
        }

        hide(error);
        hide(notice);
        setBusy(true);

        const result = await postQuestion({ url, csrfToken, question, meetingIds, fetch });

        setBusy(false);
        // ボタンを無効にした時点で、フォーカスは body へ落ちている。
        // 成功でも失敗でも入力欄へ戻し、そのまま次を打てるようにする。
        input.focus();

        if (!result.ok) {
            // 入力欄は空にしない。書き直した質問を打ち直させないためである。
            show(error, result.message);
            return;
        }

        // 該当なしと議事録なしは失敗ではない。赤いエラー欄ではなく但し書きに出す。
        if (result.status === 'no-meetings') {
            show(notice, [NO_MEETINGS_MESSAGE, ...result.notes].join(' '));
            return;
        }

        if (result.status === 'no-match') {
            show(notice, [noMatchMessage(question), ...result.notes].join(' '));
            return;
        }

        if (result.notes.length > 0) {
            show(notice, result.notes.join(' '));
        }

        turns.append(renderTurn(result.turn, doc));
        input.value = '';
        toggleEmpty();
        scrollToLatest();
    }

    async function ask() {
        // 検索を経由しない。根拠を絞らず、サーバが全体から選ぶ。
        await askQuestion([]);
    }

    async function answer() {
        // 検索結果の上位だけを根拠にする。0 件なら空で渡して、サーバが全体から選ぶ。
        await askQuestion(loaded.slice(0, MAX_ANSWER_MEETINGS));
    }

    async function clearAll() {
        if (!confirm(CLEAR_CONFIRM)) {
            return;
        }

        const result = await clearChat({ url, csrfToken, fetch });
        if (!result.ok) {
            show(error, result.message);
            return;
        }

        turns.replaceChildren();
        hide(error);
        hide(notice);
        toggleEmpty();
    }

    async function handleKeydown(event) {
        // Enter 単独は改行のまま残す。複数行の入力欄なので、書いている途中の誤送信を避ける。
        if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
            event.preventDefault();
            await search();
        }
    }

    return { ask, search, more, answer, clear: clearAll, handleKeydown };
}

function start(root) {
    const elements = {
        turns: root.querySelector('#chat-turns'),
        empty: root.querySelector('#chat-empty'),
        input: root.querySelector('#chat-input'),
        send: root.querySelector('#chat-send'),
        clear: root.querySelector('#chat-clear'),
        busy: root.querySelector('#chat-busy'),
        notice: root.querySelector('#chat-notice'),
        error: root.querySelector('#chat-error'),
        searchPanel: root.querySelector('#search-panel'),
        searchSummary: root.querySelector('#search-summary'),
        searchResults: root.querySelector('#search-results'),
        searchMore: root.querySelector('#search-more'),
        searchAnswer: root.querySelector('#search-answer')
    };

    const controller = createChatController({
        elements,
        doc: document,
        fetch: window.fetch.bind(window),
        csrfToken: document.querySelector('meta[name="csrf"]')?.content ?? '',
        url: root.dataset.chatUrl,
        searchUrl: root.dataset.searchUrl,
        confirm: message => window.confirm(message)
    });

    elements.send.addEventListener('click', () => controller.search());
    elements.searchMore.addEventListener('click', () => controller.more());
    elements.searchAnswer.addEventListener('click', () => controller.answer());
    elements.clear.addEventListener('click', () => controller.clear());
    elements.input.addEventListener('keydown', event => controller.handleKeydown(event));

    // 開いた直後は、いちばん新しい往復が見えている状態にする。
    elements.turns.scrollTop = elements.turns.scrollHeight;
}

// 起動は宣言をすべて済ませた末尾で行う（const を初期化前に触らないため）。
// node:test は document を持たず、素の参照は ReferenceError になるので typeof で確かめてから触る。
const chatRoot = typeof document === 'undefined' ? null : document.getElementById('conai-chat');
if (chatRoot) {
    start(chatRoot);
}
