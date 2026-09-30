(() => {
    const tagInput = document.querySelector('[data-tag-filter-input]');
    const tagSuggestions = document.querySelector('[data-tag-suggestions]');
    const tagSuggestionSource = document.querySelector('[data-tag-suggestion-source]');

    if (tagInput && tagSuggestions && tagSuggestionSource) {
        const availableTags = Array.from(
            tagSuggestionSource.content.querySelectorAll('[data-tag-value]'))
            .map((element) => element.dataset.tagValue || '')
            .filter(Boolean);
        let activeSuggestionIndex = -1;
        let currentMatches = [];

        function isTokenDelimiter(character) {
            return /[\s,()&|!]/.test(character);
        }

        function getCurrentToken() {
            const value = tagInput.value;
            const caret = tagInput.selectionStart ?? value.length;
            let start = caret;
            let end = caret;

            while (start > 0 && !isTokenDelimiter(value[start - 1])) {
                start -= 1;
            }

            while (end < value.length && !isTokenDelimiter(value[end])) {
                end += 1;
            }

            let rawToken = value.slice(start, end);
            let prefix = '';

            while (rawToken.startsWith('-')) {
                prefix += '-';
                rawToken = rawToken.slice(1);
            }

            if (rawToken.startsWith('#')) {
                prefix += '#';
                rawToken = rawToken.slice(1);
            }

            return {
                start,
                end,
                prefix,
                query: rawToken.trim().toLocaleLowerCase()
            };
        }

        function hideTagSuggestions() {
            tagSuggestions.hidden = true;
            tagSuggestions.replaceChildren();
            tagInput.setAttribute('aria-expanded', 'false');
            tagInput.removeAttribute('aria-activedescendant');
            activeSuggestionIndex = -1;
            currentMatches = [];
        }

        function updateActiveSuggestion() {
            const buttons = Array.from(
                tagSuggestions.querySelectorAll('[data-tag-suggestion]'));

            buttons.forEach((button, index) => {
                const isActive = index === activeSuggestionIndex;
                button.classList.toggle('is-active', isActive);
                button.setAttribute('aria-selected', isActive ? 'true' : 'false');

                if (isActive) {
                    tagInput.setAttribute('aria-activedescendant', button.id);
                    button.scrollIntoView({ block: 'nearest' });
                }
            });

            if (activeSuggestionIndex < 0) {
                tagInput.removeAttribute('aria-activedescendant');
            }
        }

        function insertTagSuggestion(tag) {
            const token = getCurrentToken();
            const replacement = token.prefix + tag;

            tagInput.setRangeText(
                replacement,
                token.start,
                token.end,
                'end');

            hideTagSuggestions();
            tagInput.focus();
            tagInput.dispatchEvent(new Event('input', { bubbles: true }));
        }

        function renderTagSuggestions() {
            if (document.activeElement !== tagInput) {
                hideTagSuggestions();
                return;
            }

            const token = getCurrentToken();
            const query = token.query;

            currentMatches = availableTags
                .map((tag, index) => ({
                    tag,
                    index,
                    startsWith: query.length === 0 ||
                        tag.toLocaleLowerCase().startsWith(query)
                }))
                .filter((item) =>
                    query.length === 0 ||
                    item.tag.toLocaleLowerCase().includes(query))
                .sort((left, right) => {
                    if (left.startsWith !== right.startsWith) {
                        return left.startsWith ? -1 : 1;
                    }

                    return left.index - right.index;
                })
                .slice(0, 8)
                .map((item) => item.tag);

            if (currentMatches.length === 0) {
                hideTagSuggestions();
                return;
            }

            tagSuggestions.replaceChildren();

            currentMatches.forEach((tag, index) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.id = `tagSuggestion-${index}`;
                button.className = 'tag-suggestion';
                button.dataset.tagSuggestion = tag;
                button.setAttribute('role', 'option');
                button.setAttribute('aria-selected', 'false');
                button.textContent = `#${tag}`;

                button.addEventListener('mousedown', (event) => {
                    event.preventDefault();
                });

                button.addEventListener('click', () => {
                    insertTagSuggestion(tag);
                });

                tagSuggestions.appendChild(button);
            });

            activeSuggestionIndex = -1;
            tagSuggestions.hidden = false;
            tagInput.setAttribute('aria-expanded', 'true');
        }

        tagInput.addEventListener('input', renderTagSuggestions);
        tagInput.addEventListener('focus', renderTagSuggestions);
        tagInput.addEventListener('click', renderTagSuggestions);
        tagInput.addEventListener('keyup', (event) => {
            if (event.key === 'ArrowLeft' ||
                event.key === 'ArrowRight' ||
                event.key === 'Home' ||
                event.key === 'End') {
                renderTagSuggestions();
            }
        });

        tagInput.addEventListener('keydown', (event) => {
            if (tagSuggestions.hidden) {
                if (event.key === 'ArrowDown') {
                    renderTagSuggestions();
                }
                return;
            }

            if (event.key === 'ArrowDown') {
                event.preventDefault();
                activeSuggestionIndex = Math.min(
                    currentMatches.length - 1,
                    activeSuggestionIndex + 1);
                updateActiveSuggestion();
                return;
            }

            if (event.key === 'ArrowUp') {
                event.preventDefault();
                activeSuggestionIndex = Math.max(
                    0,
                    activeSuggestionIndex <= 0
                        ? currentMatches.length - 1
                        : activeSuggestionIndex - 1);
                updateActiveSuggestion();
                return;
            }

            if ((event.key === 'Enter' || event.key === 'Tab') &&
                activeSuggestionIndex >= 0 &&
                currentMatches[activeSuggestionIndex]) {
                event.preventDefault();
                insertTagSuggestion(currentMatches[activeSuggestionIndex]);
                return;
            }

            if (event.key === 'Escape') {
                event.preventDefault();
                hideTagSuggestions();
            }
        });

        document.addEventListener('mousedown', (event) => {
            if (event.target === tagInput ||
                tagSuggestions.contains(event.target)) {
                return;
            }

            hideTagSuggestions();
        });
    }

    const grid = document.querySelector('[data-notes-grid]');
    if (!grid) return;

    const noteTagGroups = Array.from(
        document.querySelectorAll('[data-note-tags]'));
    const minimumPageSize = 30;
    const maximumPageSize = 60;
    const totalCount = Number.parseInt(grid.dataset.totalCount || '0', 10);
    let lastColumnCount = 0;
    let resizeTimer = null;
    let tagLayoutFrame = null;

    function getRowCount(elements) {
        const rowTops = [];

        elements.forEach((element) => {
            if (element.hidden) return;

            const top = element.offsetTop;
            if (!rowTops.some((rowTop) => Math.abs(rowTop - top) <= 2)) {
                rowTops.push(top);
            }
        });

        return rowTops.length;
    }

    function layoutNoteTags(container) {
        const tags = Array.from(
            container.querySelectorAll('[data-note-tag]'));
        const overflow = container.querySelector('[data-note-tag-overflow]');
        if (!overflow || tags.length === 0) return;

        tags.forEach((tag) => {
            tag.hidden = false;
        });
        overflow.hidden = true;
        overflow.textContent = '';

        if (getRowCount(tags) <= 2) return;

        let visibleCount = tags.length;
        while (visibleCount > 0 &&
               getRowCount(tags.slice(0, visibleCount)) > 2) {
            visibleCount -= 1;
            tags[visibleCount].hidden = true;
        }

        let hiddenCount = tags.length - visibleCount;
        overflow.textContent = `+${hiddenCount}`;
        overflow.hidden = false;

        while (visibleCount > 0 &&
               getRowCount([
                   ...tags.slice(0, visibleCount),
                   overflow
               ]) > 2) {
            visibleCount -= 1;
            tags[visibleCount].hidden = true;
            hiddenCount += 1;
            overflow.textContent = `+${hiddenCount}`;
        }
    }

    function layoutAllNoteTags() {
        noteTagGroups.forEach(layoutNoteTags);
    }

    function scheduleTagLayout() {
        if (tagLayoutFrame !== null) return;

        tagLayoutFrame = window.requestAnimationFrame(() => {
            tagLayoutFrame = null;
            layoutAllNoteTags();
        });
    }

    layoutAllNoteTags();

    if ('ResizeObserver' in window) {
        const resizeObserver = new ResizeObserver((entries) => {
            entries.forEach((entry) => {
                if (entry.target.matches?.('[data-note-tags]')) {
                    layoutNoteTags(entry.target);
                }
            });
        });

        noteTagGroups.forEach((group) => resizeObserver.observe(group));
    }

    document.fonts?.ready?.then(scheduleTagLayout);

    function getColumnCount() {
        const template = window.getComputedStyle(grid).gridTemplateColumns;
        if (!template || template === 'none') return 1;
        return Math.max(
            1,
            template.split(/\s+/).filter(Boolean).length);
    }

    function getDesiredPageSize(columnCount) {
        const rows = Math.ceil(minimumPageSize / columnCount);
        return Math.min(
            maximumPageSize,
            Math.max(minimumPageSize, rows * columnCount));
    }

    function syncPageSize() {
        if (!Number.isFinite(totalCount) ||
            totalCount <= minimumPageSize) {
            return;
        }

        const columnCount = getColumnCount();
        if (columnCount === lastColumnCount) return;
        lastColumnCount = columnCount;

        const desiredPageSize = getDesiredPageSize(columnCount);
        const currentPageSize = Number.parseInt(
            grid.dataset.pageSize || '30',
            10);
        const currentPage = Math.max(
            1,
            Number.parseInt(grid.dataset.page || '1', 10) || 1);

        if (desiredPageSize === currentPageSize) return;

        const firstItemOffset =
            (currentPage - 1) * currentPageSize;
        const nextPage =
            Math.floor(firstItemOffset / desiredPageSize) + 1;
        const url = new URL(window.location.href);

        if (desiredPageSize === minimumPageSize) {
            url.searchParams.delete('pageSize');
        } else {
            url.searchParams.set(
                'pageSize',
                String(desiredPageSize));
        }

        if (nextPage === 1) {
            url.searchParams.delete('page');
        } else {
            url.searchParams.set('page', String(nextPage));
        }

        window.location.replace(url.toString());
    }

    syncPageSize();

    window.addEventListener('resize', () => {
        scheduleTagLayout();

        window.clearTimeout(resizeTimer);
        resizeTimer = window.setTimeout(syncPageSize, 180);
    });
})();
