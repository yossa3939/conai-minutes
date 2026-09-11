// live の WebSocket を開く。DOM に触らないので、node:test から素のまま試せる。

/// 現在の場所と会議 ID から接続先を作る。https の画面なら wss になる。
export function buildLiveSocketUrl(location, config) {
    const scheme = location.protocol === 'https:' ? 'wss' : 'ws';
    return `${scheme}://${location.host}${config.wsPath}`
        + `?meetingId=${encodeURIComponent(config.meetingId)}`;
}

/// 接続を待つ上限。これを超えても open も error も来なければ、開かないものとして扱う。
const CONNECT_TIMEOUT_MS = 10000;

/// 開けたソケットで解決する。開けなければ、閉じてから断る。
export function openLiveSocket({
    url,
    WebSocketImpl = WebSocket,
    onMessage,
    onClose,
    timeoutMs = CONNECT_TIMEOUT_MS
}) {
    const socket = new WebSocketImpl(url);
    socket.binaryType = 'arraybuffer';

    return new Promise((resolve, reject) => {
        // 結果が決まったかどうか。決まったあとの open / error は、どちらも何もしない。
        let settled = false;

        // 応答が返ってこないと open も error も来ない。放っておくと CONNECTING のまま
        // 待ち続け、録音を始めることも失敗を知らせることもできなくなる。
        const timer = setTimeout(() => {
            if (settled) {
                return;
            }

            settled = true;
            socket.close();
            reject(new Error('接続できませんでした。'));
        }, timeoutMs);

        socket.addEventListener('open', () => {
            if (settled) {
                // 時間切れで閉じたあとの open。ここで message と close を付けると、
                // 断ったはずの接続が知らせを出し始める。
                return;
            }

            settled = true;
            clearTimeout(timer);

            // message と close は開けてから付ける。接続に失敗したときの close で
            // 「接続できませんでした」の案内が「切断しました」に上書きされないようにする。
            socket.addEventListener('message', onMessage);
            socket.addEventListener('close', onClose);
            resolve(socket);
        }, { once: true });

        socket.addEventListener('error', () => {
            if (settled) {
                // 開いたあとの error は接続の失敗ではない。ここで閉じると、
                // 録音中のソケットを自分で切ってしまう。切断は close 側で扱う。
                return;
            }

            settled = true;
            clearTimeout(timer);

            // 開けなかったソケットを残すと、CONNECTING のまま接続を試み続ける。
            socket.close();
            reject(new Error('接続できませんでした。'));
        });
    });
}
