import assert from 'node:assert/strict';
import test from 'node:test';

import { createBusy } from '../../src/ConAI.Web/wwwroot/js/busy.js';

// apply に渡ってきた値を順に記録する。最後の値が、その時点でボタンが押せるかを表す。
function recorder() {
    const applied = [];
    return { applied, apply: enabled => applied.push(enabled) };
}

test('どちらも走っていなければ押せる', () => {
    const { applied, apply } = recorder();
    const setBusy = createBusy(apply);

    setBusy('recording', false);

    assert.deepEqual(applied, [true]);
});

test('録音中に生成が終わっても、録音が続く限り押せないままにする', () => {
    const { applied, apply } = recorder();
    const setBusy = createBusy(apply);

    setBusy('generating', true);
    setBusy('recording', true);
    setBusy('generating', false);

    assert.equal(applied.at(-1), false);
});

test('生成中に録音を止めても、生成が続く限り押せないままにする', () => {
    const { applied, apply } = recorder();
    const setBusy = createBusy(apply);

    setBusy('recording', true);
    setBusy('generating', true);
    setBusy('recording', false);

    assert.equal(applied.at(-1), false);
});

test('両方が終わって初めて押せるようになる', () => {
    const { applied, apply } = recorder();
    const setBusy = createBusy(apply);

    setBusy('recording', true);
    setBusy('generating', true);
    setBusy('recording', false);
    assert.equal(applied.at(-1), false);

    setBusy('generating', false);
    assert.equal(applied.at(-1), true);
});

test('同じ状態を続けて渡されても、判断は変わらない', () => {
    const { applied, apply } = recorder();
    const setBusy = createBusy(apply);

    setBusy('recording', true);
    setBusy('recording', true);

    assert.deepEqual(applied, [false, false]);
});
