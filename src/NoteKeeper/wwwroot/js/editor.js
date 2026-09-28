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
    const antiForgeryToken = editor.querySelector('input[name="__RequestVerificationToken"]').value;
    const initialBlocks = JSON.parse(document.getElementById('initialBlocks').textContent || '[]');
    const strings = JSON.parse(document.getElementById('editorStrings')?.textContent || '{}');
    const locale = strings.locale || document.documentElement.lang || 'en';
    const format = (template, value) => String(template || '').replace('{0}', value);

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

        if (type === 'link') {
            actions.appendChild(makeControl('↗', strings.openLink, 'open-link'));
        }

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

    function createTextBlock(data) {
        const body = document.createElement('div');
        body.className = 'block-body';

        const textarea = document.createElement('textarea');
        textarea.className = 'block-textarea';
        textarea.placeholder = strings.textPlaceholder;
        textarea.value = data.textContent || '';
        body.appendChild(textarea);
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

        const url = document.createElement('input');
        url.className = 'block-input link-url';
        url.type = 'url';
        url.placeholder = 'https://…';
        url.value = data.url || '';

        const comment = document.createElement('textarea');
        comment.className = 'block-textarea link-comment';
        comment.placeholder = strings.linkCommentPlaceholder;
        comment.value = data.textContent || '';

        row.append(title, url);
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
                case 'text':
                    return {
                        type: 1,
                        textContent: block.querySelector('.block-textarea').value
                    };
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
        if (event.target.matches('input, textarea')) {
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
            const focusTarget = block.querySelector('textarea, input');
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
        if (imageItems.length === 0) return;

        const files = imageItems.map(item => item.getAsFile()).filter(Boolean);
        if (files.length === 0) return;

        event.preventDefault();
        await addImageFiles(files, activeBlock, null);
    });

    window.addEventListener('keydown', (event) => {
        const isSaveShortcut =
            (event.ctrlKey || event.metaKey) &&
            (event.code === 'KeyS' || event.key.toLowerCase() === 's');

        if (isSaveShortcut) {
            event.preventDefault();
            saveNote();
            return;
        }

        if (event.key === 'Escape') {
            event.preventDefault();
            window.location.assign('/');
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
