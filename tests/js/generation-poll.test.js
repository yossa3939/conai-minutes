import assert from 'node:assert/strict';
import test from 'node:test';

import { pollGeneration } from '../../src/ConAI.Web/wwwroot/js/generation-poll.js';

// タイマーは呼び出し側から渡すので、テストでは「手で 1 回進める」関数に置き換える。
function fakeTimers() {
    const state = { handler: null, cleared: 0, interval: null };

    return {
        setInterval: (handler, interval) => {
            state.handler = handler;
            state.interval = interval;
            return 'handle';
        },
        clearInterval: handle => {
            assert.equal(handle, 'handle');
            state.cleared += 1;
        },
        tick: () => state.handler(),
        state
    };
}

// 返す応答を順に並べる。尽きたら最後の応答を返し続ける。
function fakeFetch(responses) {
    const calls = [];
    let index = 0;

    const fetch = url => {
        calls.push(url);
        const next = responses[Math.min(index, responses.length - 1)];
        index += 1;
        return next instanceof Error ? Promise.reject(next) : Promise.resolve(next);
    };

    return { fetch, calls };
}

const ok = payload => ({ ok: true, json: () => Promise.resolve(payload) });
const status = code => ({ ok: false, status: code, json: () => Promise.reject(new Error('本文なし')) });

test('完了を読んだら監視をやめ、結果を 1 度だけ渡す', async () => {
    const timers = fakeTimers();
    const { fetch } = fakeFetch([ok({ status: 'Running' }), ok({ status: 'Succeeded', minutes: '# 議事録' })]);
    const settled = [];

    pollGeneration({
        url: '/api/meetings/1/generation',
        fetch,
        setInterval: timers.setInterval,
        clearInterval: timers.clearInterval,
        onSettled: payload => settled.push(payload)
    });

    await timers.tick();
    assert.deepEqual(settled, []);

    await timers.tick();
    assert.equal(settled.length, 1);
    assert.equal(settled[0].minutes, '# 議事録');
    assert.equal(timers.state.cleared, 1);

    // 止めたあとに残っていた回が走っても、二重に伝えない。
    await timers.tick();
    assert.equal(settled.length, 1);
});

test('失敗を読んだ場合も監視をやめる', async () => {
    const timers = fakeTimers();
    const { fetch } = fakeFetch([ok({ status: 'Failed', error: '生成に失敗しました。' })]);
    const settled = [];

    pollGeneration({
        url: '/g',
        fetch,
        setInterval: timers.setInterval,
        clearInterval: timers.clearInterval,
        onSettled: payload => settled.push(payload)
    });

    await timers.tick();

    assert.equal(settled.length, 1);
    assert.equal(settled[0].error, '生成に失敗しました。');
    assert.equal(timers.state.cleared, 1);
});

test('続けて状況を読めない回が上限に達したら諦めて監視をやめる', async () => {
    const timers = fakeTimers();
    const { fetch, calls } = fakeFetch([new Error('通信できない')]);
    let gaveUp = 0;

    pollGeneration({
        url: '/g',
        fetch,
        setInterval: timers.setInterval,
        clearInterval: timers.clearInterval,
        maxFailures: 3,
        onGiveUp: () => { gaveUp += 1; }
    });

    await timers.tick();
    await timers.tick();
    assert.equal(gaveUp, 0);

    await timers.tick();
    assert.equal(gaveUp, 1);
    assert.equal(timers.state.cleared, 1);

    // 止めたあとは、間隔が来ても要求を出さない。
    await timers.tick();
    assert.equal(calls.length, 3);
    assert.equal(gaveUp, 1);
});

test('429 も状況を読めなかった回として数える', async () => {
    const timers = fakeTimers();
    const { fetch } = fakeFetch([status(429)]);
    let gaveUp = 0;

    pollGeneration({
        url: '/g',
        fetch,
        setInterval: timers.setInterval,
        clearInterval: timers.clearInterval,
        maxFailures: 2,
        onGiveUp: () => { gaveUp += 1; }
    });

    await timers.tick();
    await timers.tick();

    assert.equal(gaveUp, 1);
});

test('途中で読めた回があれば、失敗の数は 0 に戻る', async () => {
    const timers = fakeTimers();
    const { fetch } = fakeFetch([
        status(500),
        status(500),
        ok({ status: 'Running' }),
        status(500),
        status(500),
        ok({ status: 'Succeeded' })
    ]);
    let gaveUp = 0;
    const settled = [];

    pollGeneration({
        url: '/g',
        fetch,
        setInterval: timers.setInterval,
        clearInterval: timers.clearInterval,
        maxFailures: 3,
        onGiveUp: () => { gaveUp += 1; },
        onSettled: payload => settled.push(payload)
    });

    for (let i = 0; i < 6; i += 1) {
        await timers.tick();
    }

    assert.equal(gaveUp, 0);
    assert.equal(settled.length, 1);
});

test('本文が JSON でない応答も読めなかった回として扱う', async () => {
    const timers = fakeTimers();
    const { fetch } = fakeFetch([{ ok: true, json: () => Promise.reject(new Error('JSON ではない')) }]);
    let gaveUp = 0;
    const seen = [];

    pollGeneration({
        url: '/g',
        fetch,
        setInterval: timers.setInterval,
        clearInterval: timers.clearInterval,
        maxFailures: 1,
        onStatus: payload => seen.push(payload),
        onGiveUp: () => { gaveUp += 1; }
    });

    await timers.tick();

    assert.deepEqual(seen, []);
    assert.equal(gaveUp, 1);
});

test('返された関数を呼ぶと監視をやめる', async () => {
    const timers = fakeTimers();
    const { fetch, calls } = fakeFetch([ok({ status: 'Running' })]);

    const stop = pollGeneration({
        url: '/g',
        fetch,
        setInterval: timers.setInterval,
        clearInterval: timers.clearInterval
    });

    await timers.tick();
    stop();
    await timers.tick();

    assert.equal(calls.length, 1);
    assert.equal(timers.state.cleared, 1);
});

test('間隔の既定は 3 秒である', () => {
    const timers = fakeTimers();
    const { fetch } = fakeFetch([ok({ status: 'Running' })]);

    pollGeneration({
        url: '/g',
        fetch,
        setInterval: timers.setInterval,
        clearInterval: timers.clearInterval
    });

    assert.equal(timers.state.interval, 3000);
});

test('前の回の応答を待っている間は、次の回で要求を出さない', async () => {
    const timers = fakeTimers();
    const calls = [];
    let release = null;
    const pending = new Promise(resolve => { release = resolve; });
    const fetch = url => {
        calls.push(url);
        return pending;
    };
    let gaveUp = 0;

    pollGeneration({
        url: '/g',
        fetch,
        setInterval: timers.setInterval,
        clearInterval: timers.clearInterval,
        maxFailures: 2,
        onGiveUp: () => { gaveUp += 1; }
    });

    const first = timers.tick();
    timers.tick();
    timers.tick();

    // 応答が返らないうちは、何回間隔が来ても要求は 1 つのままにする。
    assert.equal(calls.length, 1);

    release(ok({ status: 'Running' }));
    await first;

    // 重ならないので、1 回の遅れが失敗の数を二重に進めることもない。
    assert.equal(gaveUp, 0);

    await timers.tick();
    assert.equal(calls.length, 2);
});
