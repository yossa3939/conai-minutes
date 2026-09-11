import assert from 'node:assert/strict';
import test from 'node:test';

import { createDocument, createElements } from './dom-stub.js';
import {
    CLEAR_CONFIRM,
    NETWORK_ERROR_MESSAGE,
    NO_MEETINGS_MESSAGE,
    NO_RESULTS_MESSAGE,
    RATE_LIMIT_MESSAGE,
    answerButtonLabel,
    createChatController,
    noMatchMessage,
    postQuestion,
    renderTurn
} from '../../src/ConAI.Web/wwwroot/js/chat.js';

function fakeFetch(responses) {
    const calls = [];
    const queue = [...responses];

    const impl = async (url, init) => {
        calls.push({ url, init });
        const next = queue.shift();
        if (next instanceof Error) {
            throw next;
        }

        return next;
    };

    impl.calls = calls;

    return impl;
}

function jsonResponse(body, { ok = true, status = 200 } = {}) {
    return { ok, status, json: async () => body };
}

function answeredBody(overrides = {}) {
    return {
        status: 'answered',
        notes: [],
        turn: {
            id: 'a1',
            question: '予算の話はどうなりましたか',
            answerHtml: '<p>増額が承認されました。</p>',
            createdAtText: '2026/09/04 12:00:00',
            sources: [{ meetingId: 'm1', title: '予算会議' }]
        },
        ...overrides
    };
}

function foundBody(overrides = {}) {
    return {
        status: 'found',
        total: 1,
        page: 1,
        pageSize: 20,
        indexingRemaining: 0,
        results: [
            {
                meetingId: '11111111-1111-1111-1111-111111111111',
                title: '予算検討会',
                heldAtText: '2026/08/12 10:00:00',
                excerpt: '…島田さんから予算の見直しについて…',
                matchedIn: 'minutes'
            }
        ],
        notes: [],
        ...overrides
    };
}

function controllerWith(fetchImpl, { confirmResult = true } = {}) {
    const elements = createElements();
    const controller = createChatController({
        elements,
        doc: createDocument(),
        fetch: fetchImpl,
        csrfToken: 'token-1',
        url: '/api/chat',
        searchUrl: '/api/search',
        confirm: () => confirmResult
    });

    return { elements, controller };
}

test('質問は CSRF トークン付きで POST される', async () => {
    const fetchImpl = fakeFetch([jsonResponse(answeredBody())]);

    const result = await postQuestion({
        url: '/api/chat',
        csrfToken: 'token-1',
        question: '予算の話はどうなりましたか',
        fetch: fetchImpl
    });

    assert.equal(result.ok, true);
    assert.equal(result.status, 'answered');
    assert.equal(fetchImpl.calls[0].url, '/api/chat');
    assert.equal(fetchImpl.calls[0].init.method, 'POST');
    assert.equal(fetchImpl.calls[0].init.headers['X-CSRF-TOKEN'], 'token-1');
    assert.equal(
        JSON.parse(fetchImpl.calls[0].init.body).question,
        '予算の話はどうなりましたか');
});

test('通信に失敗すると通信のメッセージを返す', async () => {
    const fetchImpl = fakeFetch([new Error('切断')]);

    const result = await postQuestion({
        url: '/api/chat', csrfToken: 'token-1', question: '質問', fetch: fetchImpl
    });

    assert.deepEqual(result, { ok: false, message: NETWORK_ERROR_MESSAGE });
});

test('429 はレート制限のメッセージを返す', async () => {
    const fetchImpl = fakeFetch([{ ok: false, status: 429, json: async () => ({}) }]);

    const result = await postQuestion({
        url: '/api/chat', csrfToken: 'token-1', question: '質問', fetch: fetchImpl
    });

    assert.deepEqual(result, { ok: false, message: RATE_LIMIT_MESSAGE });
});

test('本文の message がそのままエラーになる', async () => {
    const fetchImpl = fakeFetch([
        jsonResponse({ message: '質問を入力してください。' }, { ok: false, status: 400 })
    ]);

    const result = await postQuestion({
        url: '/api/chat', csrfToken: 'token-1', question: '質問', fetch: fetchImpl
    });

    assert.deepEqual(result, { ok: false, message: '質問を入力してください。' });
});

test('答えが返ると往復が増えて空状態が消える', async () => {
    const { elements, controller } = controllerWith(fakeFetch([jsonResponse(answeredBody())]));
    elements.input.value = '予算の話はどうなりましたか';

    await controller.ask();

    assert.equal(elements.turns.childElementCount, 1);
    assert.equal(elements.empty.classList.contains('hidden'), true);
    assert.equal(elements.input.value, '');
    assert.equal(elements.notice.classList.contains('hidden'), true);
});

test('該当なしは注記に出て往復は増えない', async () => {
    const body = { status: 'no-match', turn: null, notes: [] };
    const { elements, controller } = controllerWith(fakeFetch([jsonResponse(body)]));
    elements.input.value = '昼食のメニューは';

    await controller.ask();

    assert.equal(elements.turns.childElementCount, 0);
    assert.equal(elements.notice.classList.contains('hidden'), false);
    assert.equal(elements.notice.textContent, noMatchMessage('昼食のメニューは'));
    // 該当なしは失敗ではない。赤いエラー欄には出さない。
    assert.equal(elements.error.classList.contains('hidden'), true);
});

test('議事録なしは注記に出る', async () => {
    const body = { status: 'no-meetings', turn: null, notes: [] };
    const { elements, controller } = controllerWith(fakeFetch([jsonResponse(body)]));
    elements.input.value = '何かありましたか';

    await controller.ask();

    assert.equal(elements.notice.textContent, NO_MEETINGS_MESSAGE);
    assert.equal(elements.turns.childElementCount, 0);
});

test('打ち切りの注記は答えと一緒に出る', async () => {
    const body = answeredBody({ notes: ['会議が多いため、新しいものから一部だけを対象に探しました。'] });
    const { elements, controller } = controllerWith(fakeFetch([jsonResponse(body)]));
    elements.input.value = '予算の話は';

    await controller.ask();

    assert.equal(elements.turns.childElementCount, 1);
    assert.equal(
        elements.notice.textContent,
        '会議が多いため、新しいものから一部だけを対象に探しました。');
});

test('質問は textContent に入り、答えだけが innerHTML になる', () => {
    const doc = createDocument();
    const block = renderTurn({
        id: 'a1',
        question: '<script>alert(1)</script>',
        answerHtml: '<p>答え</p>',
        createdAtText: '2026/09/04 12:00:00',
        sources: []
    }, doc);

    const question = block.children.find(child => child.textContent === '<script>alert(1)</script>');
    assert.ok(question, '質問は textContent に入っていること');
    assert.equal(question.innerHTML, '');

    const answer = block.children.find(child => child.innerHTML === '<p>答え</p>');
    assert.ok(answer, '答えは innerHTML に入っていること');
});

test('根拠は会議が生きていればリンクになり、消えていれば文字だけになる', () => {
    const doc = createDocument();
    const block = renderTurn({
        id: 'a1',
        question: '質問',
        answerHtml: '<p>答え</p>',
        createdAtText: '2026/09/04 12:00:00',
        sources: [
            { meetingId: 'm1', title: '予算会議' },
            { meetingId: null, title: '消えた会議' }
        ]
    }, doc);

    const links = block.collect(node => node.tagName === 'A');

    assert.equal(links.length, 1);
    assert.equal(links[0].href, '/Meetings/Details/m1');
    assert.equal(links[0].textContent, '予算会議');
    assert.ok(block.text.includes('消えた会議'));
    // 2 件目以降は「、」で区切る。リロード後の Razor 側の描画と同じ見た目に揃える。
    assert.ok(block.text.includes('予算会議、消えた会議'));
});

test('送信中はボタンが無効になり aria-busy が立つ', async () => {
    let seen = null;
    const fetchImpl = async () => {
        seen = { send: elements.send.disabled, busy: elements.turns.getAttribute('aria-busy') };

        return jsonResponse(answeredBody());
    };

    const elements = createElements();
    const controller = createChatController({
        elements,
        doc: createDocument(),
        fetch: fetchImpl,
        csrfToken: 'token-1',
        url: '/api/chat',
        confirm: () => true
    });
    elements.input.value = '予算の話は';

    await controller.ask();

    assert.deepEqual(seen, { send: true, busy: 'true' });
    assert.equal(elements.send.disabled, false);
    assert.equal(elements.turns.getAttribute('aria-busy'), 'false');
    // 送信の後はそのまま次を打てるように、入力欄へ戻す。
    assert.equal(elements.input.focusCount, 1);
});

test('Ctrl+Enter は送り、Enter 単独は送らない', async () => {
    // Ctrl+Enter の送り先は探すである。応答は検索の形にする。
    const fetchImpl = fakeFetch([jsonResponse(foundBody())]);
    const { elements, controller } = controllerWith(fetchImpl);
    elements.input.value = '予算の話は';

    let prevented = 0;
    await controller.handleKeydown({ key: 'Enter', ctrlKey: false, preventDefault: () => { prevented += 1; } });
    assert.equal(fetchImpl.calls.length, 0);
    assert.equal(prevented, 0);

    await controller.handleKeydown({ key: 'Enter', ctrlKey: true, preventDefault: () => { prevented += 1; } });
    assert.equal(fetchImpl.calls.length, 1);
    assert.equal(fetchImpl.calls[0].url, '/api/search');
    assert.equal(prevented, 1);
});

test('会話を消すと一覧が空になり、断ると消さない', async () => {
    const deleted = fakeFetch([{ ok: true, status: 204, json: async () => ({}) }]);
    const accepted = controllerWith(deleted);
    accepted.elements.turns.append(createDocument().createElement('div'));

    await accepted.controller.clear();

    assert.equal(accepted.elements.turns.childElementCount, 0);
    assert.equal(deleted.calls[0].init.method, 'DELETE');
    assert.equal(deleted.calls[0].init.headers['X-CSRF-TOKEN'], 'token-1');

    const untouched = fakeFetch([]);
    const refused = controllerWith(untouched, { confirmResult: false });
    refused.elements.turns.append(createDocument().createElement('div'));

    await refused.controller.clear();

    assert.equal(untouched.calls.length, 0);
    assert.equal(refused.elements.turns.childElementCount, 1);
    assert.ok(CLEAR_CONFIRM.length > 0);
});

test('探すと検索の宛先へ検索語が送られる', async () => {
    const fetch = fakeFetch([jsonResponse(foundBody())]);
    const elements = createElements();
    const controller = createChatController({
        elements,
        doc: createDocument(),
        fetch,
        csrfToken: 'token',
        url: '/api/chat',
        searchUrl: '/api/search',
        confirm: () => true
    });

    elements.input.value = '予算';
    await controller.search();

    assert.equal(fetch.calls[0].url, '/api/search');
    assert.deepEqual(JSON.parse(fetch.calls[0].init.body), { query: '予算', page: 1 });
    assert.equal(elements.searchResults.childElementCount, 1);
    assert.match(elements.searchResults.text, /予算検討会/);
});

test('該当が無いと回答を作る誘導が出る', async () => {
    const fetch = fakeFetch([jsonResponse(foundBody({ status: 'empty', total: 0, results: [] }))]);
    const elements = createElements();
    const controller = createChatController({
        elements, doc: createDocument(), fetch, csrfToken: 'token',
        url: '/api/chat', searchUrl: '/api/search', confirm: () => true
    });

    elements.input.value = '存在しない語句';
    await controller.search();

    assert.equal(elements.searchSummary.text, NO_RESULTS_MESSAGE);
    assert.equal(elements.searchAnswer.classList.contains('hidden'), false);
});

test('注記が件数の下に並ぶ', async () => {
    const notes = ['索引を作成中です（残り 3 件）。結果が揃っていない可能性があります。'];
    const fetch = fakeFetch([jsonResponse(foundBody({ notes }))]);
    const elements = createElements();
    const controller = createChatController({
        elements, doc: createDocument(), fetch, csrfToken: 'token',
        url: '/api/chat', searchUrl: '/api/search', confirm: () => true
    });

    elements.input.value = '予算';
    await controller.search();

    assert.match(elements.notice.text, /索引を作成中です/);
});

test('さらに表示で次のページが追記される', async () => {
    const second = foundBody({
        page: 2,
        total: 2,
        results: [
            {
                meetingId: '22222222-2222-2222-2222-222222222222',
                title: '部門定例',
                heldAtText: '',
                excerpt: '文字起こしに一致',
                matchedIn: 'transcript'
            }
        ]
    });
    const fetch = fakeFetch([jsonResponse(foundBody({ total: 2 })), jsonResponse(second)]);
    const elements = createElements();
    const controller = createChatController({
        elements, doc: createDocument(), fetch, csrfToken: 'token',
        url: '/api/chat', searchUrl: '/api/search', confirm: () => true
    });

    elements.input.value = '予算';
    await controller.search();
    await controller.more();

    assert.equal(JSON.parse(fetch.calls[1].init.body).page, 2);
    assert.equal(elements.searchResults.childElementCount, 2);
});

test('回答を作るボタンが送る件数を名乗る', () => {
    assert.equal(answerButtonLabel(3), '上位 3 件から回答を作る');
    assert.equal(answerButtonLabel(500), '上位 10 件から回答を作る');
    assert.equal(answerButtonLabel(0), '回答を作る');
});

test('回答の要求に検索結果の会議 ID が載る', async () => {
    const fetch = fakeFetch([jsonResponse(foundBody()), jsonResponse(answeredBody())]);
    const elements = createElements();
    const controller = createChatController({
        elements, doc: createDocument(), fetch, csrfToken: 'token',
        url: '/api/chat', searchUrl: '/api/search', confirm: () => true
    });

    elements.input.value = '予算';
    await controller.search();
    await controller.answer();

    const body = JSON.parse(fetch.calls[1].init.body);
    assert.equal(fetch.calls[1].url, '/api/chat');
    assert.deepEqual(body.meetingIds, ['11111111-1111-1111-1111-111111111111']);
});

test('検索していなくても回答は作れる', async () => {
    const fetch = fakeFetch([jsonResponse(answeredBody())]);
    const elements = createElements();
    const controller = createChatController({
        elements, doc: createDocument(), fetch, csrfToken: 'token',
        url: '/api/chat', searchUrl: '/api/search', confirm: () => true
    });

    elements.input.value = '予算はどうなった？';
    await controller.answer();

    assert.deepEqual(JSON.parse(fetch.calls[0].init.body).meetingIds, []);
});
