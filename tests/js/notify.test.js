import assert from 'node:assert/strict';
import test from 'node:test';

import { createDocument } from './dom-stub.js';
import {
    DEFAULT_ERROR_MESSAGE,
    NETWORK_ERROR_MESSAGE,
    RATE_LIMIT_MESSAGE,
    SENDING_MESSAGE,
    createNotifyController,
    postNotify
} from '../../src/ConAI.Web/wwwroot/js/notify.js';

function jsonResponse(status, body) {
    return {
        ok: status >= 200 && status < 300,
        status,
        json: async () => body
    };
}

function brokenResponse(status) {
    return {
        ok: status >= 200 && status < 300,
        status,
        json: async () => {
            throw new SyntaxError('本文が JSON ではない');
        }
    };
}

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

function createElements() {
    const doc = createDocument();

    return {
        button: doc.createElement('button'),
        result: doc.createElement('p')
    };
}

test('CSRF トークンを付けて POST する', async () => {
    const fetch = fakeFetch([jsonResponse(202, { ok: true, message: '受け付けました。' })]);

    await postNotify({ url: '/api/meetings/abc/notify', csrfToken: 'token-1', fetch });

    assert.equal(fetch.calls.length, 1);
    assert.equal(fetch.calls[0].url, '/api/meetings/abc/notify');
    assert.equal(fetch.calls[0].init.method, 'POST');
    assert.equal(fetch.calls[0].init.headers['X-CSRF-TOKEN'], 'token-1');
});

test('本文が ok:false なら 200 でも失敗として返る', async () => {
    const fetch = fakeFetch([jsonResponse(200, { ok: false, message: '宛先に断られました。' })]);

    const result = await postNotify({ url: '/api/notifications/abc/test', csrfToken: 't', fetch });

    assert.deepEqual(result, { ok: false, message: '宛先に断られました。' });
});

test('繋がらなければ通信の文言になる', async () => {
    const fetch = fakeFetch([new TypeError('failed to fetch')]);

    const result = await postNotify({ url: '/api/notifications/abc/test', csrfToken: 't', fetch });

    assert.deepEqual(result, { ok: false, message: NETWORK_ERROR_MESSAGE });
});

test('429 は待つよう促す文言になる', async () => {
    const fetch = fakeFetch([{ ok: false, status: 429, json: async () => ({}) }]);

    const result = await postNotify({ url: '/api/notifications/abc/test', csrfToken: 't', fetch });

    assert.deepEqual(result, { ok: false, message: RATE_LIMIT_MESSAGE });
});

test('本文が JSON でない失敗は既定の文言になる', async () => {
    const fetch = fakeFetch([brokenResponse(500)]);

    const result = await postNotify({ url: '/api/notifications/abc/test', csrfToken: 't', fetch });

    assert.deepEqual(result, { ok: false, message: DEFAULT_ERROR_MESSAGE });
});

test('成功すると結果の欄に文言と成功の色が出る', async () => {
    const elements = createElements();
    const fetch = fakeFetch([jsonResponse(200, { ok: true, message: 'テスト通知を送りました。' })]);
    const controller = createNotifyController({
        ...elements,
        fetch,
        csrfToken: 't',
        url: '/api/notifications/abc/test'
    });

    await controller.run();

    assert.equal(elements.result.textContent, 'テスト通知を送りました。');
    assert.ok(elements.result.classList.contains('alert-success'));
    assert.ok(!elements.result.classList.contains('alert-destructive'));
    assert.ok(!elements.result.classList.contains('hidden'));
    assert.equal(elements.button.disabled, false);
});

test('失敗すると結果の欄に失敗の色が出る', async () => {
    const elements = createElements();
    const fetch = fakeFetch([jsonResponse(200, { ok: false, message: '宛先に断られました。' })]);
    const controller = createNotifyController({
        ...elements,
        fetch,
        csrfToken: 't',
        url: '/api/notifications/abc/test'
    });

    await controller.run();

    assert.equal(elements.result.textContent, '宛先に断られました。');
    assert.ok(elements.result.classList.contains('alert-destructive'));
    assert.ok(!elements.result.classList.contains('alert-success'));
});

test('送っているあいだはボタンを押せず、送信中の文言が出る', async () => {
    const elements = createElements();
    const seen = [];
    const fetch = async () => {
        seen.push({ disabled: elements.button.disabled, text: elements.result.textContent });
        return jsonResponse(202, { ok: true, message: '受け付けました。' });
    };

    const controller = createNotifyController({
        ...elements,
        fetch,
        csrfToken: 't',
        url: '/api/meetings/abc/notify'
    });

    await controller.run();

    assert.deepEqual(seen, [{ disabled: true, text: SENDING_MESSAGE }]);
    assert.equal(elements.button.disabled, false);
});
