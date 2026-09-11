// 議事録の生成状況を一定の間隔で読みに行く。
// DOM も window も触らない（fetch とタイマーは呼び出し側から渡す）ので、そのまま単体で試せる。

export const POLL_INTERVAL_MS = 3000;

// 続けてこの回数だけ状況を読めなかったら、読みに行くのをやめる。
// やめないと、サーバが落ちている間ずっと 3 秒ごとに要求を出し続けてしまう。
export const MAX_CONSECUTIVE_FAILURES = 5;

/**
 * 生成状況の監視を始める。
 *
 * @param {object} options
 * @param {string} options.url 状況を読む URL
 * @param {(url: string, init: object) => Promise<{ ok: boolean, json: () => Promise<object> }>} options.fetch
 * @param {(handler: () => void, ms: number) => unknown} options.setInterval
 * @param {(handle: unknown) => void} options.clearInterval
 * @param {number} [options.interval] 読みに行く間隔（ミリ秒）
 * @param {number} [options.maxFailures] 続けて読めなかったときに諦める回数
 * @param {(payload: object) => void} [options.onStatus] 状況を読めるたびに呼ぶ
 * @param {(payload: object) => void} [options.onSettled] 完了・失敗を読んだときに 1 度だけ呼ぶ
 * @param {() => void} [options.onGiveUp] 続けて読めずに諦めたときに 1 度だけ呼ぶ
 * @returns {() => void} 呼ぶと監視をやめる
 */
export function pollGeneration({
    url,
    fetch,
    setInterval,
    clearInterval,
    interval = POLL_INTERVAL_MS,
    maxFailures = MAX_CONSECUTIVE_FAILURES,
    onStatus = () => {},
    onSettled = () => {},
    onGiveUp = () => {}
}) {
    let failures = 0;
    let stopped = false;
    let reading = false;

    const handle = setInterval(async () => {
        // 応答が間隔より遅いと、次の回が重なって出てしまう。そうなると 1 回分の遅れが
        // 失敗の数を二重に進め、想定の半分の時間で諦めてしまう。前の回が終わるまでは飛ばす。
        if (stopped || reading) {
            return;
        }

        reading = true;
        let payload;
        try {
            payload = await readStatus(url, fetch);
        } finally {
            reading = false;
        }

        // 読んでいる間に片が付いていることがある。その回の結果は捨てる。
        if (stopped) {
            return;
        }

        if (payload === null) {
            failures += 1;
            if (failures >= maxFailures) {
                stop();
                onGiveUp();
            }
            return;
        }

        failures = 0;
        onStatus(payload);

        if (payload.status === 'Succeeded' || payload.status === 'Failed') {
            stop();
            onSettled(payload);
        }
    }, interval);

    function stop() {
        if (stopped) {
            return;
        }

        stopped = true;
        clearInterval(handle);
    }

    return stop;
}

// 状況を読めなかったときは null を返す。429（絞られた）も、状況が分からない点では同じに扱う。
async function readStatus(url, fetch) {
    const response = await fetch(url, { headers: { Accept: 'application/json' } }).catch(() => null);
    if (!response?.ok) {
        return null;
    }

    try {
        return await response.json();
    } catch {
        return null;
    }
}
