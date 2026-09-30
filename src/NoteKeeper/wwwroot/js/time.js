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


(() => {
    const textInput = document.querySelector('[data-time-date-text]');
    const pickerInput = document.querySelector('[data-time-date-picker]');

    if (!textInput || !pickerInput) return;

    const formatPickerValue = (value) => {
        const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value || '');
        if (!match) return null;

        return `${match[3]}/${match[2]}/${match[1].slice(-2)}`;
    };

    const parseDisplayValue = (value) => {
        const match = /^(\d{2})\/(\d{2})\/(\d{2})$/.exec(
            String(value || '').trim());
        if (!match) return null;

        const day = Number(match[1]);
        const month = Number(match[2]);
        const year = 2000 + Number(match[3]);
        const candidate = new Date(Date.UTC(year, month - 1, day));

        if (candidate.getUTCFullYear() !== year ||
            candidate.getUTCMonth() !== month - 1 ||
            candidate.getUTCDate() !== day) {
            return null;
        }

        return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
    };

    pickerInput.addEventListener('change', () => {
        const formatted = formatPickerValue(pickerInput.value);
        if (formatted) {
            textInput.value = formatted;
        }
    });

    textInput.addEventListener('change', () => {
        const pickerValue = parseDisplayValue(textInput.value);
        if (pickerValue) {
            pickerInput.value = pickerValue;
        }
    });
})();
