import assert from 'node:assert/strict';
import test from 'node:test';

import { mixLevel } from '../../src/ConAI.Web/wwwroot/js/audio-capture.js';

test('PC 音声だけのときは等倍で足す', () => {
    // マイクがいないのに下げると、PC 音声が Gemini の発話検出に届かなくなる。
    const sources = [{ kind: 'tab' }];

    assert.equal(mixLevel('tab', sources), 1);
});

test('マイクと一緒のときは PC 音声を下げる', () => {
    // コンプレッサが PC 音声に合わせて縮み、マイクの声が埋もれるのを防ぐ。
    const sources = [{ kind: 'tab' }, { kind: 'mic' }];

    assert.equal(mixLevel('tab', sources), 0.5);
});

test('マイクは組み合わせにかかわらず等倍', () => {
    const sources = [{ kind: 'tab' }, { kind: 'mic' }];

    assert.equal(mixLevel('mic', sources), 1);
});

test('マイクだけのときも等倍', () => {
    const sources = [{ kind: 'mic' }];

    assert.equal(mixLevel('mic', sources), 1);
});

test('知らない kind は等倍', () => {
    assert.equal(mixLevel('unknown', [{ kind: 'tab' }]), 1);
});
