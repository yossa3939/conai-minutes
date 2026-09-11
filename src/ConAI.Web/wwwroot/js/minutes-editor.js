// 議事録の「プレビュー / 編集」の切り替え。
// プレビューは画面を開いたときにサーバが描く（閲覧タブと同じ MarkdownRenderer の出力）。
// 保存はヘッダの「保存」1 本にまとめたので、ここでは切り替えだけを担う（RV 3）。

// タブの見た目。有効なタブに付ける／外すクラス（Tailwind のユーティリティ。ビルド時に走査される）。
const TAB_ACTIVE = ['bg-card', 'text-foreground', 'shadow-sm'];
const TAB_INACTIVE = ['text-muted-foreground'];

const block = document.getElementById('minutes-block');
if (block) {
    setUpMinutesEditor(block);
}

function setUpMinutesEditor(block) {
    const previewTab = block.querySelector('#minutes-preview-tab');
    const editTab = block.querySelector('#minutes-edit-tab');
    const preview = block.querySelector('#minutes-preview');
    const edit = block.querySelector('#minutes-edit');
    const textarea = block.querySelector('#minutes');
    if (!previewTab || !editTab || !preview || !edit || !textarea) {
        return;
    }

    const activate = tab => {
        const showPreview = tab === 'preview';
        setTabState(previewTab, showPreview);
        setTabState(editTab, !showPreview);
        preview.hidden = !showPreview;
        edit.hidden = showPreview;
    };

    previewTab.addEventListener('click', () => activate('preview'));
    editTab.addEventListener('click', () => {
        activate('edit');
        textarea.focus();
    });
}

function setTabState(button, active) {
    button.setAttribute('aria-pressed', active ? 'true' : 'false');
    button.classList.remove(...(active ? TAB_INACTIVE : TAB_ACTIVE));
    button.classList.add(...(active ? TAB_ACTIVE : TAB_INACTIVE));
}
