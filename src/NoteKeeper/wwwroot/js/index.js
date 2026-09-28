(() => {
    const grid = document.querySelector('[data-notes-grid]');
    if (!grid) return;

    const minimumPageSize = 30;
    const maximumPageSize = 60;
    const totalCount = Number.parseInt(grid.dataset.totalCount || '0', 10);
    let lastColumnCount = 0;
    let resizeTimer = null;

    if (!Number.isFinite(totalCount) || totalCount <= minimumPageSize) return;

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
        window.clearTimeout(resizeTimer);
        resizeTimer = window.setTimeout(syncPageSize, 180);
    });
})();
