(() => {
    const editor = document.getElementById('noteEditor');
    if (!editor) return;

    const titleInput = document.getElementById('titleInput');
    const blocksContainer = document.getElementById('blocksContainer');
    const saveButton = document.getElementById('saveButton');
    const saveButtonBottom = document.getElementById('saveButtonBottom');
    const saveStatus = document.getElementById('saveStatus');
    const addImageButton = document.getElementById('addImageButton');
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
        'B', 'STRONG', 'I', 'EM', 'U', 'S', 'STRIKE', 'CODE',
        'BLOCKQUOTE', 'DETAILS', 'SUMMARY', 'HR', 'OL', 'UL', 'LI',
        'DIV', 'P', 'BR', 'SPAN'
    ]);
    const quickSymbols = ['—', '•', '◉', '◎', '★', '☑', '☒', '☐', '✓', '➢', 'ℹ️', '🔥', '❤️', '❓', '❗', '💡', '🟥', '🟩', '🟨'];

    let dirty = false;
    let saving = false;
    let activeBlock = null;
    let draggedBlock = null;
    let imageReplaceTarget = null;

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

                if (!allowedRichTags.has(child.tagName)) {
                    child.replaceWith(...Array.from(child.childNodes));
                    continue;
                }

                for (const attribute of Array.from(child.attributes)) {
                    const keepMarker = child.tagName === 'UL' &&
                        attribute.name === 'data-marker' &&
                        ['bullet', 'dash'].includes(attribute.value);
                    const keepOpen = child.tagName === 'DETAILS' && attribute.name === 'open';
                    const keepTextSize = child.tagName === 'SPAN' &&
                        attribute.name === 'data-size' &&
                        ['small', 'large', 'xlarge'].includes(attribute.value);

                    if (!keepMarker && !keepOpen && !keepTextSize) {
                        child.removeAttribute(attribute.name);
                    }
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
        richEditor.focus();
        const selection = window.getSelection();
        if (!selection) return;

        selection.removeAllRanges();

        if (richEditor._savedRange) {
            selection.addRange(richEditor._savedRange);
            return;
        }

        const range = document.createRange();
        range.selectNodeContents(richEditor);
        range.collapse(false);
        selection.addRange(range);
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

    function normalizeFontSizeMarkup(richEditor) {
        const sizeMap = {
            '1': 'small',
            '3': 'normal',
            '5': 'large',
            '7': 'xlarge'
        };

        richEditor.querySelectorAll('font[size]').forEach((font) => {
            const size = sizeMap[font.getAttribute('size')] || 'normal';

            if (size === 'normal') {
                font.replaceWith(...Array.from(font.childNodes));
                return;
            }

            const span = document.createElement('span');
            span.dataset.size = size;
            span.append(...Array.from(font.childNodes));
            font.replaceWith(span);
        });
    }

    function applyTextSize(richEditor, size) {
        const commandSize = {
            small: '1',
            normal: '3',
            large: '5',
            xlarge: '7'
        }[size];

        if (!commandSize) return;

        restoreRichSelection(richEditor);
        document.execCommand('fontSize', false, commandSize);
        normalizeFontSizeMarkup(richEditor);
        notifyRichInput(richEditor);
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
            makeFormatButton('❝', strings.formatQuote, () => {
                const selected = selectedRichText(richEditor);
                insertRichHtml(
                    richEditor,
                    `<blockquote>${escapeHtml(selected || strings.quoteContent)}</blockquote><div><br></div>`
                );
            }),
            makeFormatButton('▸', strings.formatExpander, () => {
                const selected = selectedRichText(richEditor);
                insertRichHtml(
                    richEditor,
                    `<details><summary>${escapeHtml(strings.expanderSummary)}</summary><div>${escapeHtml(selected || strings.expanderContent)}</div></details><div><br></div>`
                );
            }),
            makeFormatButton('―', strings.formatDivider, () => insertRichHtml(richEditor, '<hr><div><br></div>'))
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
            restoreRichSelection(richEditor);
            showToast(strings.systemEmojiHint);
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
        richEditor.addEventListener('mouseup', () => saveRichSelection(richEditor));
        richEditor.addEventListener('keyup', () => saveRichSelection(richEditor));
        richEditor.addEventListener('focus', () => saveRichSelection(richEditor));
        richEditor.addEventListener('click', (event) => {
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
            if (!['Backspace', 'Delete'].includes(event.key)) return;
            if (!richEditor._selectedDivider?.isConnected) return;

            event.preventDefault();
            richEditor._selectedDivider.remove();
            richEditor._selectedDivider = null;
            notifyRichInput(richEditor);
        });

        body.append(createFormattingToolbar(richEditor), richEditor);
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

        const frame = document.createElement('div');
        frame.className = 'image-preview-frame';
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

    function createBlock(type, data = {}, insertAfter = null, markDirty = true) {
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

        if (insertAfter?.parentElement === blocksContainer) {
            blocksContainer.insertBefore(block, insertAfter.nextSibling);
        } else {
            blocksContainer.appendChild(block);
        }

        setActiveBlock(block);
        if (markDirty) setDirty(true);
        return block;
    }

    function setActiveBlock(block) {
        if (activeBlock === block) return;
        activeBlock?.classList.remove('is-active');
        activeBlock = block;
        activeBlock?.classList.add('is-active');
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

    async function addImageFiles(files, insertAfter = activeBlock, replaceTarget = null) {
        const images = Array.from(files).filter(file => file.type.startsWith('image/'));
        if (images.length === 0) return;

        addImageButton.disabled = true;
        setStatus(strings.uploading, 'is-saving');

        try {
            let anchor = insertAfter;
            for (let index = 0; index < images.length; index++) {
                const result = await uploadImage(images[index]);

                if (index === 0 && replaceTarget) {
                    replaceTarget.dataset.imagePath = result.path;
                    const image = replaceTarget.querySelector('.image-preview-frame img');
                    image.src = result.path;
                    setActiveBlock(replaceTarget);
                    setDirty(true);
                    anchor = replaceTarget;
                } else {
                    anchor = createBlock('image', { imagePath: result.path, caption: '' }, anchor, true);
                }
            }
            showToast(images.length === 1 ? strings.imageAdded : format(strings.imagesAdded, images.length));
        } catch (error) {
            showToast(error.message || strings.uploadError, true);
            setStatus(strings.uploadErrorStatus, 'is-error');
        } finally {
            addImageButton.disabled = false;
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
        createBlock(type, block, null, false);
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
            const block = createBlock(type, {}, activeBlock, true);
            const focusTarget = block.querySelector('[contenteditable="true"], textarea, input');
            focusTarget?.focus();
        });
    });

    addImageButton.addEventListener('click', () => {
        imageReplaceTarget = null;
        imageFileInput.multiple = true;
        imageFileInput.click();
    });

    imageFileInput.addEventListener('change', async () => {
        const files = imageFileInput.files;
        if (files?.length) {
            await addImageFiles(files, activeBlock, imageReplaceTarget);
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
            await addImageFiles(files, activeBlock, null);
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
