(() => {
    const grid = document.querySelector('[data-notes-grid]');
    if (!grid) return;

    const noteTagGroups = Array.from(document.querySelectorAll('[data-note-tags]'));
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
        const tags = Array.from(container.querySelectorAll('[data-note-tag]'));
        const overflow = container.querySelector('[data-note-tag-overflow]');
        if (!overflow || tags.length === 0) return;

        tags.forEach((tag) => {
            tag.hidden = false;
        });
        overflow.hidden = true;
        overflow.textContent = '';

        if (getRowCount(tags) <= 2) return;

        let visibleCount = tags.length;
        while (visibleCount > 0 && getRowCount(tags.slice(0, visibleCount)) > 2) {
            visibleCount -= 1;
            tags[visibleCount].hidden = true;
        }

        let hiddenCount = tags.length - visibleCount;
        overflow.textContent = `+${hiddenCount}`;
        overflow.hidden = false;

        while (visibleCount > 0 &&
               getRowCount([...tags.slice(0, visibleCount), overflow]) > 2) {
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
        return Math.max(1, template.split(/\s+/).filter(Boolean).length);
    }

    function getDesiredPageSize(columnCount) {
        const rows = Math.ceil(minimumPageSize / columnCount);
        return Math.min(maximumPageSize, Math.max(minimumPageSize, rows * columnCount));
    }

    function syncPageSize() {
        if (!Number.isFinite(totalCount) || totalCount <= minimumPageSize) return;

        const columnCount = getColumnCount();
        if (columnCount === lastColumnCount) return;
        lastColumnCount = columnCount;

        const desiredPageSize = getDesiredPageSize(columnCount);
        const currentPageSize = Number.parseInt(grid.dataset.pageSize || '30', 10);
        const currentPage = Math.max(1, Number.parseInt(grid.dataset.page || '1', 10) || 1);
        if (desiredPageSize === currentPageSize) return;

        const firstItemOffset = (currentPage - 1) * currentPageSize;
        const nextPage = Math.floor(firstItemOffset / desiredPageSize) + 1;
        const url = new URL(window.location.href);

        if (desiredPageSize === minimumPageSize) {
            url.searchParams.delete('pageSize');
        } else {
            url.searchParams.set('pageSize', String(desiredPageSize));
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
