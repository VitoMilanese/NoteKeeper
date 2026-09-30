(() => {
    const form = document.querySelector('[data-time-note-search-form]');
    const input = form?.querySelector('[data-time-note-search]');
    const hidden = form?.querySelector('[data-time-note-id]');
    const list = document.getElementById('timeAvailableNotes');

    if (!form || !input || !hidden || !list) return;

    const resolveNoteId = () => {
        const value = input.value.trim();
        const match = Array.from(list.options).find((option) =>
            option.value.localeCompare(
                value,
                undefined,
                { sensitivity: 'accent' }) === 0);

        hidden.value = match?.dataset.noteId || '';
    };

    input.addEventListener('input', () => {
        hidden.value = '';
    });

    input.addEventListener('change', resolveNoteId);

    form.addEventListener('submit', resolveNoteId);
})();
