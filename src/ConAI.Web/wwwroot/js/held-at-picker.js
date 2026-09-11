// 開催日時の入力欄に flatpickr（wwwroot/lib/flatpickr）を付ける。
// ブラウザ標準の datetime-local は、Windows の「短い形式」に曜日が含まれていると
// 日本語の Chrome が曜日だけ落として「yyyy/MM/dd () HH:mm:ss」と描く。書式はアプリ側で決められないためやめた。
// 値は表示と同じ yyyy/MM/dd HH:mm:ss にし、サーバは DateTimeInput がこの書式で読む。

const PICKER_OPTIONS = {
    enableTime: true,
    enableSeconds: true,
    time_24hr: true,
    dateFormat: 'Y/m/d H:i:S',
    // 手で打っても直せるようにする。書式の検証はサーバが行う。
    allowInput: true,
    locale: 'ja',
    // 携帯ではブラウザ標準の入力欄に戻す既定を止め、どの環境でも同じ欄にする。
    disableMobile: true
};

if (typeof window.flatpickr === 'function') {
    for (const input of document.querySelectorAll('input[data-conai-datetime]')) {
        window.flatpickr(input, PICKER_OPTIONS);
    }
}
