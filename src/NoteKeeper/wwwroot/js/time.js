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

(() => {
    const options = document.getElementById('timeTaskOptions');
    if (!options) return;

    const updateTaskLink = (form, noteId) => {
        const link = form?.querySelector('[data-time-task-link]');
        if (!link) return;

        if (noteId) {
            link.href = `/notes/${noteId}`;
            link.hidden = false;
        } else {
            link.href = '#';
            link.hidden = true;
        }
    };

    const resolveTaskNote = (input) => {
        const form = input.closest('form');
        const hidden = form?.querySelector('[data-time-task-note-id]');
        if (!hidden) return;

        const value = input.value.trim();
        const match = Array.from(options.options).find((option) =>
            option.value.localeCompare(
                value,
                undefined,
                { sensitivity: 'accent' }) === 0);

        const noteId = match?.dataset.noteId || '';
        hidden.value = noteId;
        updateTaskLink(form, noteId);
    };

    document.addEventListener('input', (event) => {
        const input = event.target.closest('[data-time-task-input]');
        if (!input) return;

        const hidden = input
            .closest('form')
            ?.querySelector('[data-time-task-note-id]');

        if (hidden) {
            hidden.value = '';
            updateTaskLink(input.closest('form'), '');
        }
    });

    document.addEventListener('change', (event) => {
        const input = event.target.closest('[data-time-task-input]');
        if (input) {
            resolveTaskNote(input);
        }
    });

    document.addEventListener('submit', (event) => {
        const input = event.target.querySelector?.('[data-time-task-input]');
        if (input) {
            resolveTaskNote(input);
        }
    });
})();

(() => {
    const form = document.querySelector('[data-time-add-day-form]');
    const days = document.querySelector('[data-time-days]');
    const emptyState = document.querySelector('[data-time-empty-month]');

    if (!form || !days) return;

    const showError = (message) => {
        let error = document.querySelector('[data-time-client-error]');

        if (!error) {
            error = document.createElement('div');
            error.className = 'time-error';
            error.dataset.timeClientError = 'true';
            error.setAttribute('role', 'alert');
            form.closest('.time-add-day-panel')?.before(error);
        }

        error.textContent = message;
    };

    const clearError = () => {
        document.querySelector('[data-time-client-error]')?.remove();
    };

    form.addEventListener('submit', async (event) => {
        event.preventDefault();
        clearError();

        const submitButton = form.querySelector('button[type="submit"]');
        if (submitButton) submitButton.disabled = true;

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    Accept: 'text/html'
                }
            });

            if (!response.ok) {
                const message = (await response.text()).trim();
                throw new Error(message || form.dataset.addDateError || '');
            }

            const html = await response.text();
            const template = document.createElement('template');
            template.innerHTML = html.trim();
            const newDay = template.content.firstElementChild;

            if (!newDay) return;

            const existing = document.getElementById(newDay.id);
            if (existing) {
                existing.replaceWith(newDay);
            } else {
                const newDate = newDay.dataset.date || '';
                const currentDays = Array.from(
                    days.querySelectorAll('.time-day-card'));

                const insertBefore = currentDays.find((item) =>
                    (item.dataset.date || '') < newDate);

                if (insertBefore) {
                    days.insertBefore(newDay, insertBefore);
                } else {
                    days.appendChild(newDay);
                }
            }

            if (emptyState) {
                emptyState.hidden = true;
            }
        } catch (error) {
            showError(error.message || form.dataset.addDateError || '');
        } finally {
            if (submitButton) submitButton.disabled = false;
        }
    });
})();


(() => {
    const durationPattern = /^(?:\s*\d+\s*[wdhm])+\s*$/i;

    const isValidDuration = (value) => {
        const text = String(value || '').trim();
        return text.length === 0 || durationPattern.test(text);
    };

    document.addEventListener('input', (event) => {
        const input = event.target.closest('[data-time-spent-input]');
        if (!input) return;

        input.setCustomValidity('');
    });

    document.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-time-entry-form]');
        if (!form) return;

        const input = form.querySelector('[data-time-spent-input]');
        if (!input) return;

        input.setCustomValidity('');

        if (isValidDuration(input.value)) {
            return;
        }

        event.preventDefault();
        input.setCustomValidity(
            input.dataset.invalidMessage || 'Invalid time.');
        input.reportValidity();
        input.focus();
    });
})();
