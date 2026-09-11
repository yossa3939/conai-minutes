// node:test は DOM を持たない。chat.js が実際に呼ぶ API だけを実装した代役である。
// 本物のブラウザの代わりではないので、ここに無い API を使いたくなったら、まずこのファイルを足す。

class ClassList {
    #tokens;

    constructor(initial) {
        this.#tokens = new Set(String(initial).split(' ').filter(name => name.length > 0));
    }

    add(...names) {
        for (const name of names) {
            this.#tokens.add(name);
        }
    }

    remove(...names) {
        for (const name of names) {
            this.#tokens.delete(name);
        }
    }

    contains(name) {
        return this.#tokens.has(name);
    }

    toggle(name, force) {
        if (force === undefined) {
            return this.contains(name) ? (this.remove(name), false) : (this.add(name), true);
        }

        if (force) {
            this.add(name);
        } else {
            this.remove(name);
        }

        return force;
    }

    toString() {
        return [...this.#tokens].join(' ');
    }
}

export class FakeElement {
    constructor(tagName) {
        this.tagName = String(tagName).toUpperCase();
        this.children = [];
        this.dataset = {};
        this.attributes = {};
        this.textContent = '';
        this.innerHTML = '';
        this.classList = new ClassList('');
        this.disabled = false;
        this.value = '';
        this.href = '';
        this.scrollTop = 0;
        this.scrollHeight = 0;
        this.focusCount = 0;
    }

    get className() {
        return this.classList.toString();
    }

    set className(value) {
        this.classList = new ClassList(value);
    }

    get childElementCount() {
        return this.children.length;
    }

    append(...nodes) {
        this.children.push(...nodes);
    }

    replaceChildren(...nodes) {
        this.children = [...nodes];
    }

    setAttribute(name, value) {
        this.attributes[name] = value;
    }

    getAttribute(name) {
        return Object.hasOwn(this.attributes, name) ? this.attributes[name] : null;
    }

    focus() {
        this.focusCount += 1;
    }

    /** 木の下にある文字を文書順に連結する。本物の textContent と同じ並び。
     *  innerHTML の中身は HTML 文字列であって表示文字ではないので、数に入れない。 */
    get text() {
        return this.textContent + this.children.map(child => child.text).join('');
    }

    /** 木の下から、条件に合う要素をすべて集める。 */
    collect(predicate) {
        const found = predicate(this) ? [this] : [];

        for (const child of this.children) {
            found.push(...child.collect(predicate));
        }

        return found;
    }
}

export function createDocument() {
    return { createElement: tagName => new FakeElement(tagName) };
}

/** 画面の 13 要素をまとめて作る。テストはここから必要なものを取り出す。 */
export function createElements() {
    const doc = createDocument();

    return {
        turns: doc.createElement('div'),
        empty: doc.createElement('p'),
        input: doc.createElement('textarea'),
        send: doc.createElement('button'),
        clear: doc.createElement('button'),
        busy: doc.createElement('p'),
        notice: doc.createElement('div'),
        error: doc.createElement('div'),
        searchPanel: doc.createElement('section'),
        searchSummary: doc.createElement('p'),
        searchResults: doc.createElement('ul'),
        searchMore: doc.createElement('button'),
        searchAnswer: doc.createElement('button')
    };
}
