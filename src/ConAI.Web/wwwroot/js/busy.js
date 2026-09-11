// 「保存」と「議事録を生成」は、録音と生成のどちらからでも止められる。
// 二つの流れが別々にボタンを書き換えると、あとに書いたほうが相手の判断を消してしまう。
// 生成中に録音を止めれば保存が戻り、録音中に生成が終われば生成ボタンが戻る、という具合である。
// そこで状態をここに集め、押せるかどうかの判断を 1 か所だけで出す。

/**
 * ボタンの可否を持つ入れ物を作る。
 *
 * @param {(enabled: boolean) => void} apply 判断が出るたびに呼ぶ。押せるなら true
 * @returns {(kind: 'recording' | 'generating', value: boolean) => void} 走っている流れを伝える
 */
export function createBusy(apply) {
    const state = { recording: false, generating: false };

    return function setBusy(kind, value) {
        state[kind] = value;
        apply(!state.recording && !state.generating);
    };
}
