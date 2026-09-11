import assert from 'node:assert/strict';
import test from 'node:test';

import { buildLiveSocketUrl, openLiveSocket } from '../../src/ConAI.Web/wwwroot/js/live-socket.js';

// WebSocket を差し替えて、open / error / close を手で起こす。
class FakeSocket {
    constructor(url) {
        this.url = url;
        this.binaryType = null;
        this.closed = 0;
        this.listeners = new Map();
    }

    addEventListener(type, handler, options) {
        const list = this.listeners.get(type) ?? [];
        list.push({ handler, once: options?.once === true });
        this.listeners.set(type, list);
    }

    close() {
        this.closed += 1;
    }

    emit(type, event) {
        const list = this.listeners.get(type) ?? [];
        this.listeners.set(type, list.filter(entry => !entry.once));
        for (const entry of list) {
            entry.handler(event);
        }
    }
}

// 作られたソケットを受け取れるようにして openLiveSocket を呼ぶ。
function open(options = {}) {
    const created = [];
    const promise = openLiveSocket({
        url: 'wss://example/live?meetingId=1',
        WebSocketImpl: class extends FakeSocket {
            constructor(url) {
                super(url);
                created.push(this);
            }
        },
        onMessage: () => {},
        onClose: () => {},
        ...options
    });

    return { promise, socket: created[0] };
}

test('URL は現在の場所と会議 ID から作る', () => {
    const config = { wsPath: '/live', meetingId: 'a b' };

    assert.equal(
        buildLiveSocketUrl({ protocol: 'https:', host: 'conai.example:8443' }, config),
        'wss://conai.example:8443/live?meetingId=a%20b');
    assert.equal(
        buildLiveSocketUrl({ protocol: 'http:', host: 'localhost:5000' }, config),
        'ws://localhost:5000/live?meetingId=a%20b');
});

test('開いたら message と close を伝える', async () => {
    const messages = [];
    const closes = [];
    const { promise, socket } = open({
        onMessage: event => messages.push(event.data),
        onClose: () => closes.push(1)
    });

    socket.emit('open');
    const opened = await promise;

    assert.equal(opened, socket);
    assert.equal(socket.binaryType, 'arraybuffer');

    socket.emit('message', { data: 'こんにちは' });
    socket.emit('close');

    assert.deepEqual(messages, ['こんにちは']);
    assert.equal(closes.length, 1);
});

test('開く前に閉じても切断の知らせは出さない', async () => {
    const closes = [];
    const { promise, socket } = open({ onClose: () => closes.push(1) });

    // 接続に失敗すると error のあとに close が来る。ここで onClose を呼ぶと、
    // 「接続できませんでした」の案内が「切断しました」で上書きされてしまう。
    socket.emit('error');
    socket.emit('close');

    await assert.rejects(promise, /接続できませんでした/);
    assert.equal(closes.length, 0);
});

test('接続に失敗したソケットは閉じてから断る', async () => {
    const { promise, socket } = open();

    socket.emit('error');

    await assert.rejects(promise);
    assert.equal(socket.closed, 1);
});

test('開いたあとの error では切らない', async () => {
    const closes = [];
    const { promise, socket } = open({ onClose: () => closes.push(1) });

    socket.emit('open');
    await promise;

    // 通信の途中の error は接続の失敗ではない。ここで閉じると、録音中のソケットが切れる。
    socket.emit('error');

    assert.equal(socket.closed, 0);
    assert.equal(closes.length, 0);
});

test('返事が来なければ時間切れで閉じて断る', async () => {
    const { promise, socket } = open({ timeoutMs: 10 });

    // open も error も来ない。放っておくと CONNECTING のまま、停止も知らせも出せない。
    await assert.rejects(promise, /接続できませんでした/);
    assert.equal(socket.closed, 1);
});

test('時間切れのあとに開いても知らせは出さない', async () => {
    const messages = [];
    const closes = [];
    const { promise, socket } = open({
        timeoutMs: 10,
        onMessage: event => messages.push(event.data),
        onClose: () => closes.push(1)
    });

    await assert.rejects(promise);

    socket.emit('open');
    socket.emit('message', { data: 'おそい返事' });
    socket.emit('close');

    assert.deepEqual(messages, []);
    assert.equal(closes.length, 0);
});
