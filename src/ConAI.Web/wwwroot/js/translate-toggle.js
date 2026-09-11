// 翻訳モードのチェックに合わせて、翻訳先言語の欄の表示を切り替える（RV 2）。
// 最初の表示はサーバが描く（再表示でも選んだ状態が保たれる）。ここではチェックを変えた直後の切り替えを担う。

const checkbox = document.querySelector('input[type="checkbox"][name$="TranslateMode"]');
const field = document.getElementById('target-language-field');
if (checkbox && field) {
    const sync = () => { field.hidden = !checkbox.checked; };
    checkbox.addEventListener('change', sync);
    sync();
}
