(() => {
    const editor = document.getElementById('noteEditor');
    if (!editor) return;

    const titleInput = document.getElementById('titleInput');
    const blocksContainer = document.getElementById('blocksContainer');
    const saveButton = document.getElementById('saveButton');
    const saveButtonBottom = document.getElementById('saveButtonBottom');
    const saveStatus = document.getElementById('saveStatus');
    const addImageButtons = Array.from(document.querySelectorAll('[data-add-image-button]'));
    const imageFileInput = document.getElementById('imageFileInput');
    const deleteNoteForm = document.getElementById('deleteNoteForm');
    const exportNoteLink = document.getElementById('exportNoteLink');
    const antiForgeryToken = editor.querySelector('input[name="__RequestVerificationToken"]').value;
    const initialBlocks = JSON.parse(document.getElementById('initialBlocks').textContent || '[]');
    const strings = JSON.parse(document.getElementById('editorStrings')?.textContent || '{}');
    const locale = strings.locale || document.documentElement.lang || 'en';
    const format = (template, value) => String(template || '').replace('{0}', value);
    const richTextPrefix = '<!--NKHTML1-->';
    const allowedRichTags = new Set([
        'B', 'STRONG', 'I', 'EM', 'U', 'S', 'STRIKE', 'CODE', 'A',
        'BLOCKQUOTE', 'DETAILS', 'SUMMARY', 'HR', 'OL', 'UL', 'LI',
        'DIV', 'P', 'BR', 'SPAN'
    ]);
    const quickSymbols = ['—', '•', '◉', '◎', '★', '☑', '☒', '☐', '✓', '➢', 'ℹ️', '🔥', '❤️', '❓', '❗', '💡', '🟥', '🟩', '🟨'];
    let emojiEntries = null;
    let activeEmojiPicker = null;

    let dirty = false;
    let saving = false;
    let activeBlock = null;
    let draggedBlock = null;
    let imageReplaceTarget = null;
    let floatingToolbarFrame = null;

    const typeNames = {
        text: strings.typeText,
        link: strings.typeLink,
        image: strings.typeImage
    };

    const typeIcons = {
        text: '¶',
        link: '↗',
        image: '▧'
    };

    function setDirty(value = true) {
        dirty = value;
        if (dirty && !saving) {
            saveStatus.textContent = strings.unsaved;
            saveStatus.className = 'save-status is-dirty';
        }
    }

    function setStatus(message, className = '') {
        saveStatus.textContent = message;
        saveStatus.className = `save-status ${className}`.trim();
    }

    function showToast(message, isError = false) {
        document.querySelector('.upload-toast')?.remove();
        const toast = document.createElement('div');
        toast.className = `upload-toast${isError ? ' error' : ''}`;
        toast.textContent = message;
        document.body.appendChild(toast);
        window.setTimeout(() => toast.remove(), 3200);
    }

    function createToolbar(type) {
        const toolbar = document.createElement('div');
        toolbar.className = 'block-toolbar';

        const label = document.createElement('div');
        label.className = 'block-type-label';
        label.textContent = `${typeIcons[type]}  ${typeNames[type]}`;

        const actions = document.createElement('div');
        actions.className = 'block-toolbar-actions';

        const drag = makeControl('⋮⋮', strings.dragBlock, 'drag-handle');
        drag.draggable = true;
        const up = makeControl('↑', strings.moveUp, 'move-up');
        const down = makeControl('↓', strings.moveDown, 'move-down');
        const remove = makeControl('×', strings.removeBlock, 'remove');

        actions.append(drag, up, down, remove);
        toolbar.append(label, actions);
        return toolbar;
    }

    function makeControl(text, title, className) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = `block-control ${className}`;
        button.title = title;
        button.setAttribute('aria-label', title);
        button.textContent = text;
        return button;
    }

    function normalizeHyperlinkUrl(value) {
        let url = String(value || '').trim();
        if (!url) return null;

        if (!/^[a-z][a-z0-9+.-]*:/i.test(url)) {
            url = `https://${url}`;
        }

        try {
            const parsed = new URL(url);
            if (!['http:', 'https:'].includes(parsed.protocol)) return null;
            return parsed.toString();
        } catch {
            return null;
        }
    }

    function normalizeRichTextColor(value) {
        const raw = String(value || '').trim().toLowerCase();
        const hex = /^#([0-9a-f]{6})$/i.exec(raw);
        if (hex) return `#${hex[1].toLowerCase()}`;

        const rgb = /^rgb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)$/i.exec(raw);
        if (!rgb) return null;

        const channels = rgb.slice(1).map(Number);
        if (channels.some((channel) => channel < 0 || channel > 255)) return null;

        return `#${channels.map((channel) => channel.toString(16).padStart(2, '0')).join('')}`;
    }

    function sanitizeRichHtml(html) {
        const template = document.createElement('template');
        template.innerHTML = html || '';

        const sanitizeNode = (node) => {
            for (const child of Array.from(node.childNodes)) {
                if (child.nodeType === Node.COMMENT_NODE) {
                    child.remove();
                    continue;
                }

                if (child.nodeType !== Node.ELEMENT_NODE) continue;

                if (child.hasAttribute('data-expander-boundary')) {
                    child.remove();
                    continue;
                }

                if (!allowedRichTags.has(child.tagName)) {
                    child.replaceWith(...Array.from(child.childNodes));
                    continue;
                }

                if (child.tagName === 'A') {
                    const href = normalizeHyperlinkUrl(child.getAttribute('href'));
                    sanitizeNode(child);

                    if (!href) {
                        child.replaceWith(...Array.from(child.childNodes));
                        continue;
                    }

                    for (const attribute of Array.from(child.attributes)) {
                        child.removeAttribute(attribute.name);
                    }

                    child.setAttribute('href', href);
                    child.setAttribute('target', '_blank');
                    child.setAttribute('rel', 'noopener noreferrer');
                    continue;
                }

                const textColor = child.tagName === 'SPAN'
                    ? normalizeRichTextColor(child.dataset.color || child.style.color)
                    : null;

                for (const attribute of Array.from(child.attributes)) {
                    const keepMarker = child.tagName === 'UL' &&
                        attribute.name === 'data-marker' &&
                        ['bullet', 'dash'].includes(attribute.value);
                    const keepOpen = child.tagName === 'DETAILS' && attribute.name === 'open';
                    const keepTextSize = child.tagName === 'SPAN' &&
                        attribute.name === 'data-size' &&
                        ['small', 'normal', 'large', 'xlarge'].includes(attribute.value);

                    if (!keepMarker && !keepOpen && !keepTextSize) {
                        child.removeAttribute(attribute.name);
                    }
                }

                if (textColor) {
                    child.dataset.color = textColor;
                    child.style.color = textColor;
                }

                sanitizeNode(child);
            }
        };

        sanitizeNode(template.content);
        return template.innerHTML;
    }

    function setRichEditorContent(richEditor, value) {
        const raw = value || '';
        if (raw.startsWith(richTextPrefix)) {
            richEditor.innerHTML = sanitizeRichHtml(raw.slice(richTextPrefix.length));
        } else {
            richEditor.textContent = raw;
        }
    }

    function saveRichSelection(richEditor) {
        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return;

        const range = selection.getRangeAt(0);
        const container = range.commonAncestorContainer;
        if (richEditor.contains(container) || container === richEditor) {
            richEditor._savedRange = range.cloneRange();
        }
    }

    function restoreRichSelection(richEditor) {
        const savedRange = richEditor._savedRange?.cloneRange() || null;
        richEditor.focus({ preventScroll: true });

        const selection = window.getSelection();
        if (!selection) return;

        selection.removeAllRanges();

        if (savedRange) {
            selection.addRange(savedRange);
            richEditor._savedRange = savedRange.cloneRange();
            return;
        }

        const range = document.createRange();
        range.selectNodeContents(richEditor);
        range.collapse(false);
        selection.addRange(range);
        richEditor._savedRange = range.cloneRange();
    }

    function notifyRichInput(richEditor) {
        saveRichSelection(richEditor);
        richEditor.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function runRichCommand(richEditor, command, value = null) {
        restoreRichSelection(richEditor);
        document.execCommand(command, false, value);
        notifyRichInput(richEditor);
    }

    function escapeHtml(value) {
        const span = document.createElement('span');
        span.textContent = value;
        return span.innerHTML;
    }

    function selectedRichText(richEditor) {
        restoreRichSelection(richEditor);
        return window.getSelection()?.toString() || '';
    }

    function insertRichHtml(richEditor, html) {
        restoreRichSelection(richEditor);
        document.execCommand('insertHTML', false, html);
        notifyRichInput(richEditor);
    }

    function insertRichText(richEditor, text) {
        restoreRichSelection(richEditor);
        document.execCommand('insertText', false, text);
        notifyRichInput(richEditor);
    }

    function insertBlockWithContinuation(richEditor, blockElement) {
        restoreRichSelection(richEditor);

        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return;

        const range = selection.getRangeAt(0);
        range.deleteContents();

        const continuation = document.createElement('div');
        continuation.appendChild(document.createElement('br'));

        const fragment = document.createDocumentFragment();
        fragment.append(blockElement, continuation);
        range.insertNode(fragment);

        const caret = document.createRange();
        caret.setStart(continuation, 0);
        caret.collapse(true);

        selection.removeAllRanges();
        selection.addRange(caret);
        richEditor._savedRange = caret.cloneRange();
        notifyRichInput(richEditor);
    }

    function getEmojiCategory(codePoint) {
        if (
            (codePoint >= 0x1F600 && codePoint <= 0x1F64F) ||
            (codePoint >= 0x1F910 && codePoint <= 0x1F92F) ||
            (codePoint >= 0x1F970 && codePoint <= 0x1F97F) ||
            (codePoint >= 0x1FAE0 && codePoint <= 0x1FAE9)
        ) {
            return 'smileys';
        }

        if (
            (codePoint >= 0x1F440 && codePoint <= 0x1F487) ||
            (codePoint >= 0x1F590 && codePoint <= 0x1F596) ||
            (codePoint >= 0x1F918 && codePoint <= 0x1F91F) ||
            (codePoint >= 0x1F9D0 && codePoint <= 0x1F9FF)
        ) {
            return 'people';
        }

        if (
            (codePoint >= 0x1F400 && codePoint <= 0x1F43F) ||
            (codePoint >= 0x1F980 && codePoint <= 0x1F9AE) ||
            (codePoint >= 0x1F330 && codePoint <= 0x1F343) ||
            (codePoint >= 0x1FAB0 && codePoint <= 0x1FABF)
        ) {
            return 'animals';
        }

        if (
            (codePoint >= 0x1F32D && codePoint <= 0x1F37F) ||
            (codePoint >= 0x1F950 && codePoint <= 0x1F96F) ||
            codePoint === 0x1F9C0 ||
            codePoint === 0x1F9C1 ||
            (codePoint >= 0x1FAD0 && codePoint <= 0x1FADF)
        ) {
            return 'food';
        }

        if (
            (codePoint >= 0x1F680 && codePoint <= 0x1F6FF) ||
            (codePoint >= 0x1F3E0 && codePoint <= 0x1F3F0)
        ) {
            return 'travel';
        }

        if (
            (codePoint >= 0x1F3A0 && codePoint <= 0x1F3DF) ||
            (codePoint >= 0x1F3C0 && codePoint <= 0x1F3C4) ||
            (codePoint >= 0x1F93A && codePoint <= 0x1F94F)
        ) {
            return 'activities';
        }

        if (
            (codePoint >= 0x1F4A0 && codePoint <= 0x1F5FF) ||
            (codePoint >= 0x1F9E0 && codePoint <= 0x1F9FF) ||
            (codePoint >= 0x1FA70 && codePoint <= 0x1FAFF)
        ) {
            return 'objects';
        }

        return 'symbols';
    }

    function getEmojiEntries() {
        if (emojiEntries) return emojiEntries;

        const extendedPictographic = /\p{Extended_Pictographic}/u;
        const emojiPresentation = /\p{Emoji_Presentation}/u;
        const result = [];
        const seen = new Set();
        const keywords = {
            '😀': 'grin smile happy face',
            '😁': 'grin smile happy teeth',
            '😂': 'joy laugh tears funny',
            '🤣': 'rofl laugh funny',
            '😊': 'smile happy blush',
            '😍': 'love heart eyes',
            '🥰': 'love hearts affection',
            '😎': 'cool sunglasses',
            '😭': 'cry sad tears',
            '😡': 'angry mad',
            '🤔': 'think thinking',
            '👍': 'thumb up yes like good',
            '👎': 'thumb down no dislike bad',
            '👏': 'clap applause',
            '🙏': 'pray thanks please',
            '❤️': 'heart love red',
            '🔥': 'fire hot',
            '💡': 'idea light bulb',
            '✅': 'check done yes complete',
            '❌': 'cross no wrong',
            '⚠️': 'warning alert',
            '❓': 'question help',
            '❗': 'exclamation important',
            '🎉': 'party celebration',
            '🚀': 'rocket space',
            '💻': 'computer laptop',
            '📌': 'pin',
            '📎': 'paperclip attachment',
            '📝': 'note memo write',
            '🔍': 'search magnify',
            '🔗': 'link chain',
            '📅': 'calendar date',
            '⏰': 'alarm clock time',
            '⭐': 'star favorite',
            '☕': 'coffee drink',
            '🍕': 'pizza food',
            '🍔': 'burger food',
            '🐶': 'dog puppy animal',
            '🐱': 'cat animal',
            '🐺': 'wolf animal',
            '🦊': 'fox animal'
        };

        const categoryLabels = {
            smileys: strings.emojiCategorySmileys,
            people: strings.emojiCategoryPeople,
            animals: strings.emojiCategoryAnimals,
            food: strings.emojiCategoryFood,
            travel: strings.emojiCategoryTravel,
            activities: strings.emojiCategoryActivities,
            objects: strings.emojiCategoryObjects,
            symbols: strings.emojiCategorySymbols,
            flags: strings.emojiCategoryFlags
        };

        const add = (value, category, codePoint = value.codePointAt(0)) => {
            if (!value || seen.has(value)) return;
            seen.add(value);

            const resolvedCategory = category || getEmojiCategory(codePoint);
            result.push({
                emoji: value,
                category: resolvedCategory,
                searchText: [
                    value,
                    resolvedCategory,
                    categoryLabels[resolvedCategory],
                    keywords[value] || ''
                ].filter(Boolean).join(' ').toLowerCase()
            });
        };

        const ranges = [
            [0x2600, 0x27BF],
            [0x1F300, 0x1FAFF]
        ];

        for (const [start, end] of ranges) {
            for (let codePoint = start; codePoint <= end; codePoint++) {
                const character = String.fromCodePoint(codePoint);
                if (!extendedPictographic.test(character)) continue;
                add(
                    emojiPresentation.test(character) ? character : `${character}\uFE0F`,
                    null,
                    codePoint
                );
            }
        }

        [
            '❤️', '❣️', '♥️', '☑️', '☒️', '☀️', '☁️', '☂️', '☃️', '☄️',
            '✈️', '⌚', '⌛', '⚡', '⚽', '⚾', '⛳', '⛵', '⛺'
        ].forEach((emoji) => add(emoji));

        [
            '🇺🇦', '🇮🇹', '🇺🇸', '🇬🇧', '🇩🇪', '🇫🇷', '🇪🇸', '🇵🇱', '🇪🇺',
            '🇨🇦', '🇯🇵', '🇨🇳', '🇰🇷', '🇧🇷', '🇦🇺', '🇮🇳'
        ].forEach((emoji) => add(emoji, 'flags'));

        emojiEntries = result;
        return emojiEntries;
    }

    function closeEmojiPicker(restoreEditorFocus = false) {
        if (!activeEmojiPicker) return false;

        const { element, richEditor } = activeEmojiPicker;
        activeEmojiPicker = null;
        element.remove();

        if (restoreEditorFocus) {
            restoreRichSelection(richEditor);
        }

        return true;
    }

    function positionEmojiPicker() {
        if (!activeEmojiPicker) return;

        const { element, anchor } = activeEmojiPicker;
        const anchorRect = anchor.getBoundingClientRect();
        const margin = 8;
        const width = Math.min(420, window.innerWidth - margin * 2);
        const height = Math.min(element.offsetHeight || 460, window.innerHeight - margin * 2);

        let left = anchorRect.right - width;
        left = Math.max(margin, Math.min(left, window.innerWidth - width - margin));

        let top = anchorRect.bottom + 8;
        if (top + height > window.innerHeight - margin) {
            top = Math.max(margin, anchorRect.top - height - 8);
        }

        element.style.left = `${left}px`;
        element.style.top = `${top}px`;
        element.style.width = `${width}px`;
    }

    function openEmojiPicker(anchor, richEditor) {
        closeEmojiPicker(false);
        saveRichSelection(richEditor);

        const categoryDefinitions = [
            ['all', '◉', strings.emojiCategoryAll],
            ['smileys', '😀', strings.emojiCategorySmileys],
            ['people', '🧑', strings.emojiCategoryPeople],
            ['animals', '🐻', strings.emojiCategoryAnimals],
            ['food', '🍔', strings.emojiCategoryFood],
            ['travel', '🚗', strings.emojiCategoryTravel],
            ['activities', '⚽', strings.emojiCategoryActivities],
            ['objects', '💡', strings.emojiCategoryObjects],
            ['symbols', '❤️', strings.emojiCategorySymbols],
            ['flags', '🏳️', strings.emojiCategoryFlags]
        ];

        const picker = document.createElement('div');
        picker.className = 'emoji-picker-popover';
        picker.setAttribute('role', 'dialog');
        picker.setAttribute('aria-label', strings.systemEmoji);

        const search = document.createElement('input');
        search.type = 'search';
        search.className = 'emoji-search';
        search.placeholder = strings.emojiSearchPlaceholder;
        search.setAttribute('aria-label', strings.emojiSearchPlaceholder);

        const categories = document.createElement('div');
        categories.className = 'emoji-categories';

        const grid = document.createElement('div');
        grid.className = 'emoji-grid';

        const empty = document.createElement('div');
        empty.className = 'emoji-empty is-hidden';
        empty.textContent = strings.emojiNoResults;

        let activeCategory = 'all';

        const render = () => {
            const query = search.value.trim().toLowerCase();
            const matches = getEmojiEntries().filter((entry) =>
                (activeCategory === 'all' || entry.category === activeCategory) &&
                (!query || entry.searchText.includes(query))
            );

            grid.replaceChildren();

            const fragment = document.createDocumentFragment();
            matches.forEach((entry) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'emoji-item';
                button.textContent = entry.emoji;
                button.title = entry.emoji;
                button.addEventListener('mousedown', (event) => event.preventDefault());
                button.addEventListener('click', () => {
                    insertRichText(richEditor, entry.emoji);
                    closeEmojiPicker(false);
                });
                fragment.appendChild(button);
            });

            grid.appendChild(fragment);
            empty.classList.toggle('is-hidden', matches.length > 0);
        };

        categoryDefinitions.forEach(([key, icon, label]) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = `emoji-category${key === 'all' ? ' is-active' : ''}`;
            button.textContent = icon;
            button.title = label;
            button.setAttribute('aria-label', label);
            button.addEventListener('click', () => {
                activeCategory = key;
                categories.querySelectorAll('.emoji-category').forEach((item) => {
                    item.classList.toggle('is-active', item === button);
                });
                render();
            });
            categories.appendChild(button);
        });

        search.addEventListener('input', render);
        picker.append(search, categories, grid, empty);
        document.body.appendChild(picker);

        activeEmojiPicker = {
            element: picker,
            anchor,
            richEditor
        };

        render();
        requestAnimationFrame(() => {
            positionEmojiPicker();
            search.focus({ preventScroll: true });
        });
    }

    function applyTextSize(richEditor, size) {
        if (!['small', 'normal', 'large', 'xlarge'].includes(size)) return;

        restoreRichSelection(richEditor);

        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return;

        const range = selection.getRangeAt(0);
        if (range.collapsed || !richEditor.contains(range.commonAncestorContainer)) return;

        const content = range.extractContents();
        content.querySelectorAll?.('span[data-size]').forEach((span) => {
            span.replaceWith(...Array.from(span.childNodes));
        });

        const wrapper = document.createElement('span');
        wrapper.dataset.size = size;
        wrapper.appendChild(content);
        range.insertNode(wrapper);

        const selectedRange = document.createRange();
        selectedRange.selectNodeContents(wrapper);
        selection.removeAllRanges();
        selection.addRange(selectedRange);
        richEditor._savedRange = selectedRange.cloneRange();

        notifyRichInput(richEditor);
    }

    function applyTextColor(richEditor, color) {
        const normalizedColor = normalizeRichTextColor(color);
        if (!normalizedColor) return;

        restoreRichSelection(richEditor);

        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return;

        const range = selection.getRangeAt(0);
        if (range.collapsed || !richEditor.contains(range.commonAncestorContainer)) return;

        document.execCommand('styleWithCSS', false, true);
        document.execCommand('foreColor', false, normalizedColor);
        notifyRichInput(richEditor);
    }

    function convertBacktickCodeAtCaret(richEditor) {
        const selection = window.getSelection();
        if (!selection || !selection.isCollapsed || selection.rangeCount === 0) return false;

        const textNode = selection.anchorNode;
        const caretOffset = selection.anchorOffset;

        if (textNode?.nodeType !== Node.TEXT_NODE || caretOffset < 3) return false;
        if (textNode.parentElement?.closest('code')) return false;

        const value = textNode.nodeValue || '';
        const closingIndex = caretOffset - 1;
        if (value[closingIndex] !== '`') return false;

        const openingIndex = value.lastIndexOf('`', closingIndex - 1);
        if (openingIndex < 0 || openingIndex === closingIndex - 1) return false;

        const codeText = value.slice(openingIndex + 1, closingIndex);
        if (codeText.includes('\n')) return false;

        const before = document.createTextNode(value.slice(0, openingIndex));
        const code = document.createElement('code');
        code.textContent = codeText;
        const after = document.createTextNode(value.slice(caretOffset));

        textNode.replaceWith(before, code, after);

        const caret = document.createRange();
        caret.setStart(after, 0);
        caret.collapse(true);
        selection.removeAllRanges();
        selection.addRange(caret);
        richEditor._savedRange = caret.cloneRange();
        return true;
    }

    function applyListMarker(richEditor, marker) {
        const command = marker === 'ordered' ? 'insertOrderedList' : 'insertUnorderedList';
        runRichCommand(richEditor, command);

        if (marker === 'ordered') return;

        const selection = window.getSelection();
        const anchor = selection?.anchorNode;
        const element = anchor?.nodeType === Node.ELEMENT_NODE ? anchor : anchor?.parentElement;
        const list = element?.closest?.('ul');
        if (!list) return;

        list.dataset.marker = marker === 'dash' ? 'dash' : 'bullet';
        notifyRichInput(richEditor);
    }

    function closestQuote(node, richEditor) {
        const element = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
        const quote = element?.closest?.('blockquote');
        return quote && richEditor.contains(quote) ? quote : null;
    }

    function quoteForSelection(richEditor, range) {
        const startQuote = closestQuote(range.startContainer, richEditor);
        const endQuote = closestQuote(range.endContainer, richEditor);

        if (startQuote && startQuote === endQuote) {
            return startQuote;
        }

        const intersectingQuotes = Array.from(richEditor.querySelectorAll('blockquote')).filter((quote) => {
            try {
                return range.intersectsNode(quote);
            } catch {
                return false;
            }
        });

        if (intersectingQuotes.length !== 1) return null;

        const quote = intersectingQuotes[0];
        const selectedText = range.toString().replace(/\s+/g, ' ').trim();
        const quoteText = (quote.textContent || '').replace(/\s+/g, ' ').trim();
        return selectedText && selectedText === quoteText ? quote : null;
    }

    function rangeHtml(range) {
        const container = document.createElement('div');
        container.appendChild(range.cloneContents());
        return container.innerHTML;
    }

    function toggleQuote(richEditor) {
        restoreRichSelection(richEditor);

        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return;

        const range = selection.getRangeAt(0);
        if (!richEditor.contains(range.commonAncestorContainer) && range.commonAncestorContainer !== richEditor) {
            return;
        }

        const existingQuote = quoteForSelection(richEditor, range);
        if (existingQuote) {
            const quoteRange = document.createRange();
            quoteRange.selectNode(existingQuote);
            selection.removeAllRanges();
            selection.addRange(quoteRange);
            richEditor._savedRange = quoteRange.cloneRange();

            const replacement = `<div>${existingQuote.innerHTML || '<br>'}</div>`;
            document.execCommand('insertHTML', false, replacement);
            notifyRichInput(richEditor);
            return;
        }

        const selectedHtml = range.collapsed ? '' : rangeHtml(range);
        const quoteContent = selectedHtml || escapeHtml(strings.quoteContent);
        document.execCommand(
            'insertHTML',
            false,
            `<blockquote>${quoteContent}</blockquote><div><br></div>`
        );
        notifyRichInput(richEditor);
    }

    function closestHyperlink(node, richEditor) {
        const element = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
        const link = element?.closest?.('a[href]');
        return link && richEditor.contains(link) ? link : null;
    }

    function hyperlinkForSelection(richEditor, range) {
        const startLink = closestHyperlink(range.startContainer, richEditor);
        const endLink = closestHyperlink(range.endContainer, richEditor);
        return startLink && startLink === endLink ? startLink : null;
    }

    function restoreHyperlinkRange(context) {
        const range = context.range.cloneRange();
        context.richEditor.focus({ preventScroll: true });

        const selection = window.getSelection();
        if (!selection) return null;

        selection.removeAllRanges();
        selection.addRange(range);
        context.richEditor._savedRange = range.cloneRange();
        return selection;
    }

    function ensureHyperlinkDialog() {
        let dialog = document.getElementById('hyperlinkDialog');
        if (dialog) return dialog;

        dialog = document.createElement('dialog');
        dialog.id = 'hyperlinkDialog';
        dialog.className = 'app-dialog hyperlink-dialog';
        dialog.innerHTML = `
            <div class="app-dialog-card hyperlink-dialog-card">
                <div class="app-dialog-icon">🔗</div>
                <div class="app-dialog-copy">
                    <h2>${escapeHtml(strings.hyperlinkDialogTitle)}</h2>
                    <label class="hyperlink-dialog-field">
                        <span>${escapeHtml(strings.hyperlinkUrlLabel)}</span>
                        <input class="hyperlink-url-input" type="url" inputmode="url" autocomplete="url" placeholder="https://example.com">
                    </label>
                    <p class="hyperlink-dialog-error" hidden></p>
                </div>
                <div class="app-dialog-actions hyperlink-dialog-actions">
                    <button type="button" class="button button-ghost hyperlink-remove">${escapeHtml(strings.hyperlinkRemove)}</button>
                    <button type="button" class="button button-secondary hyperlink-cancel">${escapeHtml(strings.hyperlinkCancel)}</button>
                    <button type="button" class="button button-primary hyperlink-apply">${escapeHtml(strings.hyperlinkApply)}</button>
                </div>
            </div>`;

        const input = dialog.querySelector('.hyperlink-url-input');
        const error = dialog.querySelector('.hyperlink-dialog-error');
        const removeButton = dialog.querySelector('.hyperlink-remove');
        const applyButton = dialog.querySelector('.hyperlink-apply');

        const closeAndRestore = () => {
            const context = dialog._hyperlinkContext;
            dialog.close();
            if (context) restoreHyperlinkRange(context);
        };

        dialog.querySelector('.hyperlink-cancel').addEventListener('click', closeAndRestore);
        dialog.addEventListener('cancel', (event) => {
            event.preventDefault();
            closeAndRestore();
        });

        removeButton.addEventListener('click', () => {
            const context = dialog._hyperlinkContext;
            const link = context?.existingLink;
            if (!context || !link?.isConnected) return;

            const linkRange = document.createRange();
            linkRange.selectNodeContents(link);
            context.range = linkRange;

            // Close the modal first: while a modal <dialog> is open, the editor
            // behind it is inert and Chromium may refuse to focus/edit it.
            dialog.close();

            const selection = restoreHyperlinkRange(context);
            if (!selection) return;

            document.execCommand('unlink', false, null);
            notifyRichInput(context.richEditor);
        });

        const applyHyperlink = () => {
            const context = dialog._hyperlinkContext;
            if (!context) return;

            const href = normalizeHyperlinkUrl(input.value);
            if (!href) {
                error.textContent = strings.hyperlinkInvalidUrl;
                error.hidden = false;
                input.focus();
                return;
            }

            if (context.range.collapsed ||
                !context.richEditor.contains(context.range.commonAncestorContainer)) {
                return;
            }

            error.hidden = true;

            // Close the modal before restoring the contenteditable selection.
            // The page behind a modal <dialog> is inert, so trying to edit it while
            // the dialog is still open can make execCommand silently do nothing.
            dialog.close();

            const selection = restoreHyperlinkRange(context);
            if (!selection) return;

            document.execCommand('createLink', false, href);

            // Normalize the anchor produced by the browser.
            let link = hyperlinkForSelection(context.richEditor, selection.getRangeAt(0));
            if (!link && context.existingLink?.isConnected) {
                link = context.existingLink;
            }

            if (link) {
                link.setAttribute('href', href);
                link.setAttribute('target', '_blank');
                link.setAttribute('rel', 'noopener noreferrer');

                const linkedRange = document.createRange();
                linkedRange.selectNodeContents(link);
                context.range = linkedRange;
                context.existingLink = link;
                context.richEditor._savedRange = linkedRange.cloneRange();
            }

            notifyRichInput(context.richEditor);
        };

        applyButton.addEventListener('click', applyHyperlink);

        dialog.addEventListener('keydown', (event) => {
            if (event.target !== input || event.key !== 'Enter' || event.isComposing) return;

            event.preventDefault();
            event.stopImmediatePropagation();
            applyHyperlink();
        }, true);

        document.body.appendChild(dialog);
        return dialog;
    }

    function openHyperlinkDialog(richEditor) {
        restoreRichSelection(richEditor);

        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return;

        let range = selection.getRangeAt(0);
        if (!richEditor.contains(range.commonAncestorContainer) && range.commonAncestorContainer !== richEditor) {
            return;
        }

        const existingLink = hyperlinkForSelection(richEditor, range);
        if (range.collapsed && !existingLink) {
            showToast(strings.hyperlinkSelectText, true);
            return;
        }

        if (existingLink) {
            const linkRange = document.createRange();
            linkRange.selectNodeContents(existingLink);
            range = linkRange;
        } else {
            range = range.cloneRange();
        }

        const dialog = ensureHyperlinkDialog();
        const input = dialog.querySelector('.hyperlink-url-input');
        const error = dialog.querySelector('.hyperlink-dialog-error');
        const removeButton = dialog.querySelector('.hyperlink-remove');

        dialog._hyperlinkContext = {
            richEditor,
            range,
            existingLink
        };

        input.value = existingLink?.getAttribute('href') || '';
        error.hidden = true;
        error.textContent = '';
        removeButton.hidden = !existingLink;

        dialog.showModal();
        window.setTimeout(() => {
            input.focus();
            input.select();
        }, 0);
    }

    function summaryForSelection(richEditor) {
        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return null;

        const node = selection.anchorNode;
        const element = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
        const summary = element?.closest?.('summary');
        return summary && richEditor.contains(summary) ? summary : null;
    }

    function pointIsOverSummaryText(summary, clientX, clientY) {
        const walker = document.createTreeWalker(summary, NodeFilter.SHOW_TEXT);
        let node;

        while ((node = walker.nextNode())) {
            if (!node.nodeValue) continue;

            const range = document.createRange();
            range.selectNodeContents(node);

            for (const rect of range.getClientRects()) {
                if (
                    clientX >= rect.left &&
                    clientX <= rect.right &&
                    clientY >= rect.top &&
                    clientY <= rect.bottom
                ) {
                    return true;
                }
            }
        }

        return false;
    }

    function deleteExpandableContainer(richEditor, details) {
        if (!details?.isConnected || !richEditor.contains(details)) return;

        const next = details.nextSibling;
        const previous = details.previousSibling;
        details.remove();

        if (!richEditor.hasChildNodes()) {
            richEditor.appendChild(document.createElement('br'));
        }

        const selection = window.getSelection();
        if (selection) {
            const range = document.createRange();
            const target = next?.isConnected ? next : previous?.isConnected ? previous : richEditor;

            if (target === richEditor) {
                range.selectNodeContents(richEditor);
                range.collapse(false);
            } else if (target.nodeType === Node.TEXT_NODE) {
                range.setStart(target, target.nodeValue?.length || 0);
                range.collapse(true);
            } else {
                range.selectNodeContents(target);
                range.collapse(false);
            }

            selection.removeAllRanges();
            selection.addRange(range);
            richEditor._savedRange = range.cloneRange();
        }

        notifyRichInput(richEditor);
    }

    function createExpanderBoundary() {
        const boundary = document.createElement('div');
        boundary.className = 'expander-boundary';
        boundary.dataset.expanderBoundary = 'true';
        boundary.appendChild(document.createElement('br'));
        return boundary;
    }

    function expanderBoundaryHasContent(boundary) {
        if ((boundary.textContent || '').length > 0) return true;

        return Array.from(boundary.children).some((child) => child.tagName !== 'BR');
    }

    function ensureExpandableBoundaries(richEditor) {
        // Once the user types into a caret anchor it becomes ordinary note
        // content and must be saved normally.
        richEditor.querySelectorAll('[data-expander-boundary]').forEach((boundary) => {
            if (!expanderBoundaryHasContent(boundary)) return;

            boundary.removeAttribute('data-expander-boundary');
            boundary.classList.remove('expander-boundary');
        });

        // Remove anchors that are no longer adjacent to an expandable
        // container (for example after Ctrl+Z removes the expander).
        richEditor.querySelectorAll('[data-expander-boundary]').forEach((boundary) => {
            const beforeExpander = boundary.nextElementSibling?.tagName === 'DETAILS';
            const afterExpander = boundary.previousElementSibling?.tagName === 'DETAILS';

            if (!beforeExpander && !afterExpander) {
                boundary.remove();
            }
        });

        richEditor.querySelectorAll('details').forEach((details) => {
            const previous = details.previousElementSibling;
            const next = details.nextElementSibling;

            if (!previous || previous.tagName === 'DETAILS') {
                const boundary = createExpanderBoundary();
                details.before(boundary);
            }

            if (!next || next.tagName === 'DETAILS') {
                const boundary = createExpanderBoundary();
                details.after(boundary);
            }
        });
    }

    function decorateExpanders(richEditor) {
        richEditor.querySelectorAll('details > summary').forEach((summary) => {
            if (summary.querySelector(':scope > .expander-delete')) return;

            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'expander-delete';
            remove.contentEditable = 'false';
            remove.title = strings.expanderDelete;
            remove.setAttribute('aria-label', strings.expanderDelete);

            remove.addEventListener('mousedown', (event) => {
                event.preventDefault();
                event.stopPropagation();
            });

            remove.addEventListener('click', (event) => {
                event.preventDefault();
                event.stopPropagation();
                deleteExpandableContainer(richEditor, summary.parentElement);
            });

            summary.appendChild(remove);
        });

        ensureExpandableBoundaries(richEditor);
    }

    function insertExpandableContainer(richEditor) {
        restoreRichSelection(richEditor);

        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return;

        const range = selection.getRangeAt(0);
        if (!richEditor.contains(range.commonAncestorContainer) &&
            range.commonAncestorContainer !== richEditor) {
            return;
        }

        const selectedHtml = range.collapsed ? '' : rangeHtml(range);
        const contentHtml = selectedHtml || '<br>';
        const html =
            `<details><summary>${escapeHtml(strings.expanderSummary)}</summary><div>${contentHtml}</div></details>`;

        // Do not append an extra continuation block: <details> is already a
        // block element, so the browser can place the caret immediately after it
        // without creating visible empty rows.
        document.execCommand('insertHTML', false, html);
        decorateExpanders(richEditor);
        notifyRichInput(richEditor);
    }

    function makeFormatButton(label, title, onClick, className = '') {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = `format-button ${className}`.trim();
        button.textContent = label;
        button.title = title;
        button.setAttribute('aria-label', title);
        button.addEventListener('mousedown', (event) => event.preventDefault());
        button.addEventListener('click', onClick);
        return button;
    }

    function updateFloatingFormattingToolbars() {
        const viewportHeight = window.innerHeight || document.documentElement.clientHeight;
        const editorTopbar = document.querySelector('.editor-topbar');
        const topBoundary = Math.max(0, editorTopbar?.getBoundingClientRect().bottom || 0) + 6;

        document.querySelectorAll('.rich-text-wrap').forEach((wrap) => {
            const topToolbar = wrap.querySelector('.format-toolbar-primary');
            const floatingToolbar = wrap.querySelector('.format-toolbar-floating');
            const floatingSpacer = wrap.querySelector('.format-toolbar-spacer');
            const richEditor = wrap.querySelector('.rich-text-editor');
            const block = wrap.closest('.note-block');

            if (!topToolbar || !floatingToolbar || !floatingSpacer || !richEditor || !block) return;

            const topRect = topToolbar.getBoundingClientRect();
            const editorRect = richEditor.getBoundingClientRect();
            const wrapRect = wrap.getBoundingClientRect();

            floatingToolbar.style.left = `${wrapRect.left}px`;
            floatingToolbar.style.width = `${wrapRect.width}px`;

            const toolbarHeight = floatingToolbar.offsetHeight;
            const topToolbarGone = topRect.bottom <= topBoundary;
            const blockStillVisible =
                editorRect.bottom > topBoundary + toolbarHeight &&
                editorRect.top < viewportHeight;

            const shouldShow =
                block === activeBlock &&
                topToolbarGone &&
                blockStillVisible;

            floatingToolbar.classList.toggle('is-visible', shouldShow);
            floatingToolbar.setAttribute('aria-hidden', shouldShow ? 'false' : 'true');
            floatingSpacer.style.height = shouldShow
                ? `${toolbarHeight + 12}px`
                : '0px';

            if (!shouldShow) {
                floatingToolbar.querySelectorAll('.symbol-picker[open]').forEach((picker) => {
                    picker.open = false;
                });

                if (activeEmojiPicker?.anchor && floatingToolbar.contains(activeEmojiPicker.anchor)) {
                    closeEmojiPicker(false);
                }
                return;
            }

            const bottomGap = 12;
            const viewportTop = viewportHeight - toolbarHeight - bottomGap;
            const reservedWrapRect = wrap.getBoundingClientRect();
            const blockBottomTop = reservedWrapRect.bottom - toolbarHeight;
            floatingToolbar.style.top = `${Math.max(topBoundary, Math.min(viewportTop, blockBottomTop))}px`;
        });
    }

    function scheduleFloatingFormattingToolbarUpdate() {
        if (floatingToolbarFrame !== null) return;

        floatingToolbarFrame = window.requestAnimationFrame(() => {
            floatingToolbarFrame = null;
            updateFloatingFormattingToolbars();
        });
    }

    function createFormattingToolbar(richEditor) {
        const toolbar = document.createElement('div');
        toolbar.className = 'format-toolbar';
        toolbar.setAttribute('role', 'toolbar');
        toolbar.addEventListener('mousedown', () => saveRichSelection(richEditor), true);

        toolbar.append(
            makeFormatButton('B', strings.formatBold, () => runRichCommand(richEditor, 'bold'), 'is-bold'),
            makeFormatButton('I', strings.formatItalic, () => runRichCommand(richEditor, 'italic'), 'is-italic'),
            makeFormatButton('U', strings.formatUnderline, () => runRichCommand(richEditor, 'underline'), 'is-underline'),
            makeFormatButton('S', strings.formatStrike, () => runRichCommand(richEditor, 'strikeThrough'), 'is-strike'),
            makeFormatButton('</>', strings.formatCode, () => {
                const selected = selectedRichText(richEditor);
                insertRichHtml(richEditor, `<code>${escapeHtml(selected || 'code')}</code><span>&nbsp;</span>`);
            }),
            makeFormatButton('🔗', strings.formatHyperlink, () => openHyperlinkDialog(richEditor), 'is-hyperlink'),
            makeFormatButton('❝', strings.formatQuote, () => toggleQuote(richEditor)),
            makeFormatButton('▸', strings.formatExpander, () => {
                insertExpandableContainer(richEditor);
            }),
            makeFormatButton('―', strings.formatDivider, () => {
                insertBlockWithContinuation(richEditor, document.createElement('hr'));
            })
        );

        const sizeSelect = document.createElement('select');
        sizeSelect.className = 'format-select text-size-select';
        sizeSelect.title = strings.formatTextSize;
        sizeSelect.setAttribute('aria-label', strings.formatTextSize);
        [
            ['', strings.formatTextSize],
            ['small', strings.formatSizeSmall],
            ['normal', strings.formatSizeNormal],
            ['large', strings.formatSizeLarge],
            ['xlarge', strings.formatSizeExtraLarge]
        ].forEach(([value, label]) => {
            const option = document.createElement('option');
            option.value = value;
            option.textContent = label;
            sizeSelect.appendChild(option);
        });
        sizeSelect.addEventListener('mousedown', () => saveRichSelection(richEditor));
        sizeSelect.addEventListener('change', () => {
            if (sizeSelect.value) applyTextSize(richEditor, sizeSelect.value);
            sizeSelect.value = '';
        });
        toolbar.appendChild(sizeSelect);

        const colorPicker = document.createElement('label');
        colorPicker.className = 'format-color-picker';
        colorPicker.title = strings.formatTextColor;

        const colorLetter = document.createElement('span');
        colorLetter.className = 'format-color-letter';
        colorLetter.textContent = 'A';

        const colorSwatch = document.createElement('span');
        colorSwatch.className = 'format-color-swatch';

        const colorInput = document.createElement('input');
        colorInput.className = 'format-color-input';
        colorInput.type = 'color';
        colorInput.value = '#7c8cff';
        colorInput.title = strings.formatTextColor;
        colorInput.setAttribute('aria-label', strings.formatTextColor);
        colorSwatch.style.backgroundColor = colorInput.value;
        colorInput.addEventListener('mousedown', () => saveRichSelection(richEditor));
        colorInput.addEventListener('change', () => {
            colorSwatch.style.backgroundColor = colorInput.value;
            applyTextColor(richEditor, colorInput.value);
        });

        colorPicker.append(colorLetter, colorSwatch, colorInput);
        toolbar.appendChild(colorPicker);

        const listSelect = document.createElement('select');
        listSelect.className = 'format-select';
        listSelect.title = strings.formatList;
        listSelect.setAttribute('aria-label', strings.formatList);
        [
            ['', strings.formatList],
            ['ordered', strings.formatOrderedList],
            ['bullet', strings.formatBulletList],
            ['dash', strings.formatDashList]
        ].forEach(([value, label]) => {
            const option = document.createElement('option');
            option.value = value;
            option.textContent = label;
            listSelect.appendChild(option);
        });
        listSelect.addEventListener('mousedown', () => saveRichSelection(richEditor));
        listSelect.addEventListener('change', () => {
            if (listSelect.value) applyListMarker(richEditor, listSelect.value);
            listSelect.value = '';
        });
        toolbar.appendChild(listSelect);

        const symbols = document.createElement('details');
        symbols.className = 'symbol-picker';

        const summary = document.createElement('summary');
        summary.className = 'format-button';
        summary.title = strings.formatSymbols;
        summary.setAttribute('aria-label', strings.formatSymbols);
        summary.textContent = 'Ω';

        const symbolPanel = document.createElement('div');
        symbolPanel.className = 'symbol-panel';

        quickSymbols.forEach((symbol) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = symbol;
            button.title = symbol;
            button.addEventListener('mousedown', (event) => {
                event.preventDefault();
                saveRichSelection(richEditor);
            });
            button.addEventListener('click', () => {
                insertRichText(richEditor, symbol);
                symbols.open = false;
            });
            symbolPanel.appendChild(button);
        });

        const systemEmojiButton = document.createElement('button');
        systemEmojiButton.type = 'button';
        systemEmojiButton.className = 'system-emoji-button';
        systemEmojiButton.textContent = '😀';
        systemEmojiButton.title = strings.systemEmoji;
        systemEmojiButton.setAttribute('aria-label', strings.systemEmoji);
        systemEmojiButton.addEventListener('mousedown', (event) => {
            event.preventDefault();
            saveRichSelection(richEditor);
        });
        systemEmojiButton.addEventListener('click', () => {
            symbols.open = false;
            openEmojiPicker(systemEmojiButton, richEditor);
        });
        symbolPanel.appendChild(systemEmojiButton);

        symbols.append(summary, symbolPanel);
        toolbar.appendChild(symbols);
        return toolbar;
    }

    function createTextBlock(data) {
        const body = document.createElement('div');
        body.className = 'block-body rich-text-wrap';

        const richEditor = document.createElement('div');
        richEditor.className = 'block-textarea rich-text-editor';
        richEditor.contentEditable = 'true';
        richEditor.spellcheck = true;
        richEditor.dataset.placeholder = strings.textPlaceholder;
        richEditor.setAttribute('role', 'textbox');
        richEditor.setAttribute('aria-multiline', 'true');

        setRichEditorContent(richEditor, data.textContent || '');
        decorateExpanders(richEditor);
        richEditor.addEventListener('mouseup', () => saveRichSelection(richEditor));
        richEditor.addEventListener('keyup', () => saveRichSelection(richEditor));
        richEditor.addEventListener('focus', () => saveRichSelection(richEditor));
        richEditor.addEventListener('input', (event) => {
            if (event.inputType === 'insertText' && event.data === '`') {
                convertBacktickCodeAtCaret(richEditor);
            }

            // Undo/redo can restore a saved semantic <details> without the
            // editor-only controls. Rebuild the delete control and compact
            // caret anchors when needed.
            decorateExpanders(richEditor);
            ensureExpandableBoundaries(richEditor);
        });
        richEditor.addEventListener('click', (event) => {
            const summary = event.target.closest?.('summary');
            if (summary && richEditor.contains(summary)) {
                if (event.target.closest?.('.expander-delete')) {
                    return;
                }

                // Native <summary> toggles when any part is clicked. Keep that
                // behavior only for the arrow or unused area to the right; a
                // click on title text is reserved for placing/editing the caret.
                if (pointIsOverSummaryText(summary, event.clientX, event.clientY)) {
                    event.preventDefault();
                }
            }

            const hyperlink = event.target.closest?.('a[href]');
            if (hyperlink && (event.ctrlKey || event.metaKey)) {
                event.preventDefault();
                event.stopPropagation();

                const href = normalizeHyperlinkUrl(hyperlink.getAttribute('href'));
                if (href) {
                    window.open(href, '_blank', 'noopener,noreferrer');
                }
                return;
            }

            const divider = event.target.closest?.('hr');
            richEditor.querySelectorAll('hr.is-selected-divider').forEach((item) => {
                if (item !== divider) item.classList.remove('is-selected-divider');
            });

            richEditor._selectedDivider = divider || null;

            if (divider) {
                divider.classList.add('is-selected-divider');
                showToast(strings.dividerDeleteHint);
            }
        });
        richEditor.addEventListener('keydown', (event) => {
            if (event.key === ' ' && summaryForSelection(richEditor)) {
                event.preventDefault();
                event.stopPropagation();

                // A focused native <summary> treats Space as activation. Insert
                // a real space instead so the title behaves like editable text.
                document.execCommand('insertText', false, ' ');
                notifyRichInput(richEditor);
                return;
            }

            if (!['Backspace', 'Delete'].includes(event.key)) return;
            if (!richEditor._selectedDivider?.isConnected) return;

            event.preventDefault();
            richEditor._selectedDivider.remove();
            richEditor._selectedDivider = null;
            notifyRichInput(richEditor);
        });

        const topToolbar = createFormattingToolbar(richEditor);
        topToolbar.classList.add('format-toolbar-primary');

        const floatingSpacer = document.createElement('div');
        floatingSpacer.className = 'format-toolbar-spacer';
        floatingSpacer.setAttribute('aria-hidden', 'true');

        const floatingToolbar = createFormattingToolbar(richEditor);
        floatingToolbar.classList.add('format-toolbar-floating');
        floatingToolbar.setAttribute('aria-hidden', 'true');

        body.append(topToolbar, richEditor, floatingSpacer, floatingToolbar);
        scheduleFloatingFormattingToolbarUpdate();
        return body;
    }

    function createLinkBlock(data) {
        const body = document.createElement('div');
        body.className = 'block-body link-fields';

        const row = document.createElement('div');
        row.className = 'link-row';

        const title = document.createElement('input');
        title.className = 'block-input link-title';
        title.placeholder = strings.linkTitlePlaceholder;
        title.maxLength = 300;
        title.value = data.linkTitle || '';

        const urlWrap = document.createElement('div');
        urlWrap.className = 'link-url-row';

        const url = document.createElement('input');
        url.className = 'block-input link-url';
        url.type = 'url';
        url.placeholder = 'https://…';
        url.value = data.url || '';

        const openLink = makeControl('↗', strings.openLink, 'open-link inline-link-open');
        urlWrap.append(url, openLink);

        const comment = document.createElement('textarea');
        comment.className = 'block-textarea link-comment';
        comment.placeholder = strings.linkCommentPlaceholder;
        comment.value = data.textContent || '';

        row.append(title, urlWrap);
        body.append(row, comment);
        return body;
    }

    function createImageBlock(data) {
        const body = document.createElement('div');
        body.className = 'block-body image-wrap';

        const frame = document.createElement('button');
        frame.type = 'button';
        frame.className = 'image-preview-frame image-preview-button';
        frame.title = strings.openImagePreview;
        frame.setAttribute('aria-label', strings.openImagePreview);

        const img = document.createElement('img');
        img.alt = data.caption || strings.imageAlt;
        img.loading = 'lazy';
        img.src = data.imagePath;
        frame.appendChild(img);

        const meta = document.createElement('div');
        meta.className = 'image-meta-row';

        const caption = document.createElement('input');
        caption.className = 'image-caption';
        caption.maxLength = 500;
        caption.placeholder = strings.imageCaptionPlaceholder;
        caption.value = data.caption || '';

        const replace = document.createElement('button');
        replace.type = 'button';
        replace.className = 'button button-secondary button-small replace-image';
        replace.textContent = strings.replaceImage;

        meta.append(caption, replace);
        body.append(frame, meta);
        return body;
    }

    function createBlock(type, data = {}, markDirty = true) {
        const block = document.createElement('article');
        block.className = 'note-block';
        block.dataset.type = type;

        if (type === 'image') {
            block.dataset.imagePath = data.imagePath || '';
        }

        block.appendChild(createToolbar(type));

        if (type === 'text') block.appendChild(createTextBlock(data));
        if (type === 'link') block.appendChild(createLinkBlock(data));
        if (type === 'image') block.appendChild(createImageBlock(data));

        blocksContainer.appendChild(block);

        setActiveBlock(block);
        if (markDirty) setDirty(true);
        return block;
    }

    function openImagePreview(block) {
        const source = block.querySelector('.image-preview-frame img');
        if (!source?.src) return;

        let dialog = document.getElementById('imageViewerDialog');
        if (!dialog) {
            dialog = document.createElement('dialog');
            dialog.id = 'imageViewerDialog';
            dialog.className = 'image-viewer-dialog';
            dialog.innerHTML = `
                <div class="image-viewer-card">
                    <button type="button" class="image-viewer-close" aria-label="${escapeHtml(strings.closeImagePreview)}" title="${escapeHtml(strings.closeImagePreview)}">×</button>
                    <img class="image-viewer-image" alt="">
                    <div class="image-viewer-caption"></div>
                </div>`;

            dialog.querySelector('.image-viewer-close').addEventListener('click', () => dialog.close());
            dialog.addEventListener('click', (event) => {
                if (event.target === dialog) dialog.close();
            });
            document.body.appendChild(dialog);
        }

        const image = dialog.querySelector('.image-viewer-image');
        const caption = dialog.querySelector('.image-viewer-caption');
        const captionText = block.querySelector('.image-caption')?.value?.trim() || '';

        image.src = source.src;
        image.alt = captionText || strings.imageAlt;
        caption.textContent = captionText;
        caption.hidden = !captionText;

        if (!dialog.open) {
            dialog.showModal();
        }
    }

    function setActiveBlock(block) {
        if (activeBlock === block) {
            scheduleFloatingFormattingToolbarUpdate();
            return;
        }

        activeBlock?.classList.remove('is-active');
        activeBlock = block;
        activeBlock?.classList.add('is-active');
        scheduleFloatingFormattingToolbarUpdate();
    }

    function moveBlock(block, direction) {
        if (direction < 0) {
            const previous = block.previousElementSibling;
            if (previous) blocksContainer.insertBefore(block, previous);
        } else {
            const next = block.nextElementSibling;
            if (next) blocksContainer.insertBefore(next, block);
        }
        setActiveBlock(block);
        setDirty(true);
    }

    async function uploadImage(file) {
        const form = new FormData();
        form.append('image', file, file.name || 'clipboard-image.png');

        const response = await fetch('/notes/upload-image', {
            method: 'POST',
            headers: {
                RequestVerificationToken: antiForgeryToken
            },
            body: form
        });

        if (!response.ok) {
            let message = strings.uploadFailed;
            try {
                const payload = await response.json();
                message = payload.message || message;
            } catch { }
            throw new Error(message);
        }

        return await response.json();
    }

    async function addImageFiles(files, replaceTarget = null) {
        const images = Array.from(files).filter(file => file.type.startsWith('image/'));
        if (images.length === 0) return;

        addImageButtons.forEach(button => { button.disabled = true; });
        setStatus(strings.uploading, 'is-saving');

        try {
            let lastAddedBlock = null;
            for (let index = 0; index < images.length; index++) {
                const result = await uploadImage(images[index]);

                if (index === 0 && replaceTarget) {
                    replaceTarget.dataset.imagePath = result.path;
                    const image = replaceTarget.querySelector('.image-preview-frame img');
                    image.src = result.path;
                    setActiveBlock(replaceTarget);
                    setDirty(true);
                } else {
                    lastAddedBlock = createBlock('image', { imagePath: result.path, caption: '' }, true);
                }
            }

            lastAddedBlock?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            showToast(images.length === 1 ? strings.imageAdded : format(strings.imagesAdded, images.length));
        } catch (error) {
            showToast(error.message || strings.uploadError, true);
            setStatus(strings.uploadErrorStatus, 'is-error');
        } finally {
            addImageButtons.forEach(button => { button.disabled = false; });
            if (dirty) setDirty(true);
        }
    }

    function collectBlocks() {
        return Array.from(blocksContainer.querySelectorAll('.note-block')).map((block) => {
            switch (block.dataset.type) {
                case 'text': {
                    const richEditor = block.querySelector('.rich-text-editor');
                    return {
                        type: 1,
                        textContent: richTextPrefix + sanitizeRichHtml(richEditor.innerHTML)
                    };
                }
                case 'link':
                    return {
                        type: 2,
                        linkTitle: block.querySelector('.link-title').value,
                        url: block.querySelector('.link-url').value,
                        textContent: block.querySelector('.link-comment').value
                    };
                case 'image':
                    return {
                        type: 3,
                        imagePath: block.dataset.imagePath,
                        caption: block.querySelector('.image-caption').value
                    };
                default:
                    return null;
            }
        }).filter(Boolean);
    }

    function validateBeforeSave() {
        for (const block of blocksContainer.querySelectorAll('.note-block[data-type="link"]')) {
            const input = block.querySelector('.link-url');
            const raw = input.value.trim();
            if (!raw) continue;

            try {
                const parsed = new URL(raw);
                if (!['http:', 'https:'].includes(parsed.protocol)) throw new Error();
            } catch {
                setActiveBlock(block);
                input.focus();
                showToast(strings.linkMustHttp, true);
                return false;
            }
        }
        return true;
    }

    async function saveNote() {
        if (saving) return;
        if (!validateBeforeSave()) return;

        saving = true;
        saveButton.disabled = true;
        saveButtonBottom.disabled = true;
        setStatus(strings.saving, 'is-saving');

        const idText = editor.dataset.noteId;
        const payload = {
            id: idText ? Number(idText) : null,
            title: titleInput.value,
            blocks: collectBlocks()
        };

        try {
            const response = await fetch('/notes/save', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    RequestVerificationToken: antiForgeryToken
                },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                let message = format(strings.saveErrorStatus, response.status);
                try {
                    const data = await response.json();
                    message = data.message || message;
                } catch { }
                throw new Error(message);
            }

            const result = await response.json();
            editor.dataset.noteId = String(result.id);
            titleInput.value = result.title;
            dirty = false;

            if (deleteNoteForm) {
                deleteNoteForm.classList.remove('is-hidden');
                deleteNoteForm.action = `/notes/${result.id}/delete`;
                deleteNoteForm.dataset.confirmTitle = strings.deleteDialogTitle;
                deleteNoteForm.dataset.confirmMessage = format(strings.deleteDialogMessage, result.title);
                deleteNoteForm.dataset.confirmLabel = strings.deleteLabel;
            }

            if (exportNoteLink) {
                exportNoteLink.classList.remove('is-hidden');
                exportNoteLink.href = `/notes/${result.id}/export`;
            }

            const url = `/notes/${result.id}`;
            if (window.location.pathname !== url) {
                window.history.replaceState({}, '', url);
            }

            document.title = `${result.title} — NoteKeeper`;
            const savedAt = new Date(result.updatedAtUtc);
            const formatted = new Intl.DateTimeFormat(locale, {
                dateStyle: 'medium',
                timeStyle: 'short'
            }).format(savedAt);
            setStatus(format(strings.savedAt, formatted), 'is-success');
        } catch (error) {
            setStatus(error.message || strings.saveFailed, 'is-error');
            showToast(error.message || strings.saveFailedToast, true);
        } finally {
            saving = false;
            saveButton.disabled = false;
            saveButtonBottom.disabled = false;
        }
    }

    initialBlocks.forEach((block) => {
        const type = block.type === 1 ? 'text' : block.type === 2 ? 'link' : 'image';
        createBlock(type, block, false);
    });
    dirty = false;

    editor.addEventListener('input', (event) => {
        if (event.target.matches('input, textarea, [contenteditable="true"]')) {
            setDirty(true);
        }
    });

    blocksContainer.addEventListener('focusin', (event) => {
        setActiveBlock(event.target.closest('.note-block'));
    });

    blocksContainer.addEventListener('click', (event) => {
        const block = event.target.closest('.note-block');
        if (block) setActiveBlock(block);

        if (event.target.closest('.remove')) {
            block.remove();
            activeBlock = null;
            setDirty(true);
            return;
        }

        if (event.target.closest('.move-up')) {
            moveBlock(block, -1);
            return;
        }

        if (event.target.closest('.move-down')) {
            moveBlock(block, 1);
            return;
        }

        if (event.target.closest('.open-link')) {
            const rawUrl = block.querySelector('.link-url')?.value?.trim();
            try {
                const parsed = new URL(rawUrl);
                if (!['http:', 'https:'].includes(parsed.protocol)) throw new Error();
                window.open(parsed.toString(), '_blank', 'noopener,noreferrer');
            } catch {
                showToast(strings.openLinkInvalid, true);
            }
            return;
        }

        if (event.target.closest('.image-preview-button')) {
            openImagePreview(block);
            return;
        }

        if (event.target.closest('.replace-image')) {
            imageReplaceTarget = block;
            imageFileInput.multiple = false;
            imageFileInput.click();
        }
    });

    blocksContainer.addEventListener('dragstart', (event) => {
        const handle = event.target.closest('.drag-handle');
        const block = event.target.closest('.note-block');
        if (!handle || !block) {
            event.preventDefault();
            return;
        }

        draggedBlock = block;
        block.classList.add('is-dragging');
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', 'move');
    });

    blocksContainer.addEventListener('dragover', (event) => {
        if (!draggedBlock) return;
        event.preventDefault();

        const candidates = [...blocksContainer.querySelectorAll('.note-block:not(.is-dragging)')];
        const afterElement = candidates.reduce((closest, child) => {
            const box = child.getBoundingClientRect();
            const offset = event.clientY - box.top - box.height / 2;
            if (offset < 0 && offset > closest.offset) {
                return { offset, element: child };
            }
            return closest;
        }, { offset: Number.NEGATIVE_INFINITY, element: null }).element;

        if (afterElement) {
            blocksContainer.insertBefore(draggedBlock, afterElement);
        } else {
            blocksContainer.appendChild(draggedBlock);
        }
    });

    blocksContainer.addEventListener('dragend', () => {
        if (!draggedBlock) return;
        draggedBlock.classList.remove('is-dragging');
        setActiveBlock(draggedBlock);
        draggedBlock = null;
        setDirty(true);
    });

    document.querySelectorAll('[data-add-block]').forEach((button) => {
        button.addEventListener('click', () => {
            const type = button.dataset.addBlock;
            const block = createBlock(type, {}, true);
            const focusTarget = block.querySelector('[contenteditable="true"], textarea, input');
            focusTarget?.focus();
        });
    });

    addImageButtons.forEach((button) => {
        button.addEventListener('click', () => {
            imageReplaceTarget = null;
            imageFileInput.multiple = true;
            imageFileInput.click();
        });
    });

    imageFileInput.addEventListener('change', async () => {
        const files = imageFileInput.files;
        if (files?.length) {
            await addImageFiles(files, imageReplaceTarget);
        }
        imageReplaceTarget = null;
        imageFileInput.value = '';
        imageFileInput.multiple = true;
    });

    document.addEventListener('paste', async (event) => {
        const items = Array.from(event.clipboardData?.items || []);
        const imageItems = items.filter(item => item.kind === 'file' && item.type.startsWith('image/'));

        if (imageItems.length > 0) {
            const files = imageItems.map(item => item.getAsFile()).filter(Boolean);
            if (files.length === 0) return;

            event.preventDefault();
            await addImageFiles(files, null);
            return;
        }

        const richEditor = event.target.closest?.('.rich-text-editor');
        if (!richEditor) return;

        const plainText = event.clipboardData?.getData('text/plain');
        if (plainText == null) return;

        event.preventDefault();
        insertRichText(richEditor, plainText);
    });

    async function navigateAway(url) {
        if (!dirty) {
            window.location.assign(url);
            return;
        }

        const confirmed = await window.noteKeeperConfirm?.({
            title: strings.unsavedDialogTitle,
            message: strings.unsavedDialogMessage,
            confirmLabel: strings.leaveWithoutSaving,
            danger: true
        });

        if (!confirmed) return;

        dirty = false;
        window.location.assign(url);
    }

    document.addEventListener('pointerdown', (event) => {
        if (activeEmojiPicker &&
            !activeEmojiPicker.element.contains(event.target) &&
            !activeEmojiPicker.anchor.contains(event.target)) {
            closeEmojiPicker(false);
        }

        document.querySelectorAll('.symbol-picker[open]').forEach((picker) => {
            if (!picker.contains(event.target)) {
                picker.open = false;
            }
        });
    }, true);

    window.addEventListener('resize', () => {
        positionEmojiPicker();
        scheduleFloatingFormattingToolbarUpdate();
    });
    document.addEventListener('scroll', () => {
        positionEmojiPicker();
        scheduleFloatingFormattingToolbarUpdate();
    }, true);

    document.addEventListener('click', (event) => {
        if (!dirty || event.defaultPrevented || event.button !== 0) return;
        if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;

        const anchor = event.target.closest('a[href]');
        if (!anchor || anchor.target === '_blank' || anchor.hasAttribute('download')) return;

        const targetUrl = new URL(anchor.href, window.location.href);
        if (targetUrl.origin !== window.location.origin) return;

        event.preventDefault();
        navigateAway(targetUrl.href);
    }, true);

    window.addEventListener('keydown', (event) => {
        const richEditor = event.target.closest?.('.rich-text-editor');
        const hasFormatModifier = event.ctrlKey || event.metaKey;

        if (richEditor && hasFormatModifier) {
            if (event.code === 'KeyK') {
                event.preventDefault();
                saveRichSelection(richEditor);
                openHyperlinkDialog(richEditor);
                return;
            }

            const command = {
                KeyB: 'bold',
                KeyI: 'italic',
                KeyU: 'underline'
            }[event.code];

            if (command) {
                event.preventDefault();
                runRichCommand(richEditor, command);
                return;
            }
        }

        const isSaveShortcut =
            (event.ctrlKey || event.metaKey) &&
            (event.code === 'KeyS' || event.key.toLowerCase() === 's');

        if (isSaveShortcut) {
            event.preventDefault();
            saveNote();
            return;
        }

        if (event.key === 'Escape') {
            if (closeEmojiPicker(true)) {
                event.preventDefault();
                event.stopPropagation();
                return;
            }

            const openSymbolPicker = document.querySelector('.symbol-picker[open]');
            if (openSymbolPicker) {
                openSymbolPicker.open = false;
                event.preventDefault();
                event.stopPropagation();
                return;
            }

            if (document.querySelector('dialog[open]')) return;
            event.preventDefault();
            navigateAway('/');
        }
    }, true);

    saveButton.addEventListener('click', saveNote);
    saveButtonBottom.addEventListener('click', saveNote);

    window.addEventListener('beforeunload', (event) => {
        if (!dirty) return;
        event.preventDefault();
        event.returnValue = '';
    });
})();
