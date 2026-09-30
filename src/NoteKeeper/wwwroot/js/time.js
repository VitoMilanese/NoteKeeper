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

    document.addEventListener('change', (event) => {
        const pickerInput = event.target.closest('[data-time-date-picker]');
        if (pickerInput) {
            const control = pickerInput.closest('[data-time-date-control]') ||
                pickerInput.closest('.time-date-control');
            const textInput = control?.querySelector('[data-time-date-text]');
            const formatted = formatPickerValue(pickerInput.value);

            if (textInput && formatted) {
                textInput.value = formatted;
                textInput.dispatchEvent(
                    new Event('change', { bubbles: true }));
            }
            return;
        }

        const textInput = event.target.closest('[data-time-date-text]');
        if (!textInput) return;

        const control = textInput.closest('[data-time-date-control]') ||
            textInput.closest('.time-date-control');
        const picker = control?.querySelector('[data-time-date-picker]');
        const pickerValue = parseDisplayValue(textInput.value);

        if (picker && pickerValue) {
            picker.value = pickerValue;
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


(() => {
    const days = document.querySelector('[data-time-days]');
    if (!days) return;

    const positionDay = (day) => {
        const date = day.dataset.date || '';
        const otherDays = Array.from(
            days.querySelectorAll('.time-day-card'))
            .filter((item) => item !== day);

        const insertBefore = otherDays.find((item) =>
            (item.dataset.date || '') < date);

        if (insertBefore) {
            days.insertBefore(day, insertBefore);
        } else {
            days.appendChild(day);
        }
    };

    const setEditMode = (card, enabled) => {
        const display = card.querySelector('[data-time-day-date-display]');
        const form = card.querySelector('[data-time-day-date-form]');
        const editButton = card.querySelector('[data-time-day-date-edit]');
        const error = card.querySelector('[data-time-day-date-error]');

        if (!display || !form || !editButton) return;

        display.hidden = enabled;
        form.hidden = !enabled;
        editButton.hidden = enabled;

        if (error) {
            error.hidden = true;
            error.textContent = '';
        }

        if (enabled) {
            const input = form.querySelector('[data-time-day-date-input]');
            input?.focus();
            input?.select();
        }
    };

    document.addEventListener('click', (event) => {
        const editButton = event.target.closest('[data-time-day-date-edit]');
        if (editButton) {
            const card = editButton.closest('.time-day-card');
            if (card) {
                setEditMode(card, true);
            }
            return;
        }

        const cancelButton = event.target.closest('[data-time-day-date-cancel]');
        if (cancelButton) {
            const card = cancelButton.closest('.time-day-card');
            if (card) {
                setEditMode(card, false);
            }
        }
    });

    document.addEventListener('submit', async (event) => {
        const form = event.target.closest('[data-time-day-date-form]');
        if (!form) return;

        event.preventDefault();

        const card = form.closest('.time-day-card');
        const error = form.querySelector('[data-time-day-date-error]');
        const submitButton = form.querySelector('button[type="submit"]');

        if (!card) return;

        if (error) {
            error.hidden = true;
            error.textContent = '';
        }

        if (submitButton) {
            submitButton.disabled = true;
        }

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
                throw new Error(
                    message ||
                    form.dataset.updateDateError ||
                    '');
            }

            const targetMonth =
                response.headers.get('X-Time-Day-Month') || '';
            const currentMonth = form.dataset.currentMonth || '';
            const html = await response.text();

            if (targetMonth &&
                currentMonth &&
                targetMonth !== currentMonth) {
                card.remove();

                const emptyState =
                    document.querySelector('[data-time-empty-month]');
                if (emptyState &&
                    !days.querySelector('.time-day-card')) {
                    emptyState.hidden = false;
                }

                return;
            }

            const template = document.createElement('template');
            template.innerHTML = html.trim();
            const updatedDay = template.content.firstElementChild;

            if (!updatedDay) return;

            card.replaceWith(updatedDay);
            positionDay(updatedDay);
        } catch (caught) {
            if (error) {
                error.textContent =
                    caught.message ||
                    form.dataset.updateDateError ||
                    '';
                error.hidden = false;
            }
        } finally {
            if (submitButton) {
                submitButton.disabled = false;
            }
        }
    });
})();


(() => {
    const days = document.querySelector('[data-time-days]');
    const emptyState = document.querySelector('[data-time-empty-month]');

    if (!days) return;

    const showDeleteError = (card, message) => {
        let error = card.previousElementSibling;

        if (!error?.matches?.('[data-time-delete-day-error]')) {
            error = document.createElement('div');
            error.className = 'time-error';
            error.dataset.timeDeleteDayError = 'true';
            card.before(error);
        }

        error.textContent = message;
    };

    const clearDeleteError = (card) => {
        const error = card.previousElementSibling;
        if (error?.matches?.('[data-time-delete-day-error]')) {
            error.remove();
        }
    };

    const updateTrackedNoteSummaries = (summaries) => {
        for (const summary of summaries || []) {
            const row = document.querySelector(
                `[data-time-tracked-note-id="${summary.id}"]`);

            if (!row) continue;

            const spent = row.querySelector('[data-time-tracked-spent]');
            const remaining =
                row.querySelector('[data-time-tracked-remaining]');

            if (spent) {
                spent.textContent = summary.spentTime || '0h';
            }

            if (remaining) {
                remaining.textContent = summary.remainingTime || '—';
                const isOvertime = Boolean(summary.isOvertime);
                remaining.classList.toggle(
                    'time-note-overtime',
                    isOvertime);
                remaining.dataset.isOvertime =
                    isOvertime ? 'true' : 'false';
                remaining.title = isOvertime
                    ? remaining.dataset.overtimeLabel || ''
                    : '';
            }
        }
    };

    document.addEventListener('submit', async (event) => {
        const form = event.target.closest('[data-time-day-delete-form]');
        if (!form) return;

        event.preventDefault();

        const card = form.closest('.time-day-card');
        const submitButton = form.querySelector('button[type="submit"]');

        if (!card) return;

        const confirmed = typeof window.noteKeeperConfirm === 'function'
            ? await window.noteKeeperConfirm({
                title: form.dataset.confirmTitle,
                message: form.dataset.confirmMessage,
                confirmLabel: form.dataset.confirmLabel,
                danger: form.dataset.confirmDanger === 'true'
            })
            : window.confirm(form.dataset.confirmMessage || '');

        if (!confirmed) return;

        clearDeleteError(card);

        if (submitButton) {
            submitButton.disabled = true;
        }

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    Accept: 'application/json'
                }
            });

            if (!response.ok) {
                throw new Error(
                    form.dataset.deleteDayError ||
                    '');
            }

            const result = await response.json();

            updateTrackedNoteSummaries(result.noteSummaries);
            card.remove();

            if (emptyState &&
                !days.querySelector('.time-day-card')) {
                emptyState.hidden = false;
            }
        } catch (caught) {
            showDeleteError(
                card,
                caught.message ||
                form.dataset.deleteDayError ||
                '');
        } finally {
            if (submitButton && card.isConnected) {
                submitButton.disabled = false;
            }
        }
    });
})();


(() => {
    const panel = document.querySelector('[data-time-tracked-panel]');
    if (!panel) return;

    const durationPattern = /^(?:\s*\d+\s*[wdhm])+\s*$/i;

    const isValidDuration = (value) => {
        const text = String(value || '').trim();
        return text.length === 0 || durationPattern.test(text);
    };

    const updateRemaining = (row, result) => {
        const remaining =
            row?.querySelector('[data-time-tracked-remaining]');

        if (!remaining) return;

        remaining.textContent = result.remainingTime || '—';

        const isOvertime = Boolean(result.isOvertime);
        remaining.classList.toggle(
            'time-note-overtime',
            isOvertime);
        remaining.dataset.isOvertime =
            isOvertime ? 'true' : 'false';
        remaining.title = isOvertime
            ? remaining.dataset.overtimeLabel || ''
            : '';
    };

    panel.querySelectorAll('[data-time-estimated-input]')
        .forEach((input) => {
            input.dataset.savedValue = input.value;
        });

    panel.addEventListener('input', (event) => {
        const input = event.target.closest(
            '[data-time-estimated-input]');
        if (!input) return;

        input.setCustomValidity('');
        input.classList.remove('is-invalid');
    });

    panel.addEventListener('change', (event) => {
        const input = event.target.closest(
            '[data-time-estimated-input]');
        if (!input) return;

        if (input.value === (input.dataset.savedValue || '')) {
            return;
        }

        input.closest('[data-time-estimated-form]')?.requestSubmit();
    });

    panel.addEventListener('keydown', (event) => {
        const input = event.target.closest(
            '[data-time-estimated-input]');
        if (!input) return;

        if (event.key === 'Escape') {
            event.preventDefault();
            input.value = input.dataset.savedValue || '';
            input.setCustomValidity('');
            input.classList.remove('is-invalid');
            input.blur();
            return;
        }

        if (event.key === 'Enter') {
            event.preventDefault();

            if (input.value === (input.dataset.savedValue || '')) {
                input.blur();
                return;
            }

            input.closest('[data-time-estimated-form]')?.requestSubmit();
        }
    });

    panel.addEventListener('submit', async (event) => {
        const form = event.target.closest(
            '[data-time-estimated-form]');
        if (!form) return;

        event.preventDefault();

        const input = form.querySelector(
            '[data-time-estimated-input]');
        if (!input || input.disabled) return;

        input.setCustomValidity('');
        input.classList.remove('is-invalid');

        if (!isValidDuration(input.value)) {
            input.setCustomValidity(
                input.dataset.invalidMessage || 'Invalid time.');
            input.classList.add('is-invalid');
            input.reportValidity();
            input.focus();
            return;
        }

        const formData = new FormData(form);

        input.disabled = true;
        input.classList.add('is-saving');

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: formData,
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    Accept: 'application/json'
                }
            });

            if (!response.ok) {
                const message = (await response.text()).trim();
                throw new Error(
                    message ||
                    input.dataset.invalidMessage ||
                    'Could not update estimated time.');
            }

            const result = await response.json();
            input.value = result.estimatedTime || '';
            input.dataset.savedValue = input.value;

            updateRemaining(
                form.closest('[data-time-tracked-note-id]'),
                result);
        } catch (caught) {
            input.setCustomValidity(
                caught.message ||
                input.dataset.invalidMessage ||
                'Could not update estimated time.');
            input.classList.add('is-invalid');
        } finally {
            input.disabled = false;
            input.classList.remove('is-saving');

            if (!input.checkValidity()) {
                input.reportValidity();
                input.focus();
            }
        }
    });
})();


(() => {
    const panel = document.querySelector('[data-time-tracked-panel]');
    const toggle = panel?.querySelector('[data-time-tracked-toggle]');
    const content = panel?.querySelector('[data-time-tracked-content]');

    if (!panel || !toggle || !content) return;

    const storageKey = 'notekeeper.time.trackedNotesCollapsed';

    const applyState = (collapsed) => {
        content.hidden = collapsed;
        toggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
        toggle.textContent = collapsed ? '▸' : '▾';

        const label = collapsed
            ? toggle.dataset.expandLabel
            : toggle.dataset.collapseLabel;

        if (label) {
            toggle.title = label;
            toggle.setAttribute('aria-label', label);
        }

        panel.classList.toggle('is-collapsed', collapsed);
    };

    let collapsed = false;

    try {
        collapsed = localStorage.getItem(storageKey) === '1';
    } catch {
        collapsed = false;
    }

    applyState(collapsed);

    toggle.addEventListener('click', () => {
        collapsed = toggle.getAttribute('aria-expanded') === 'true';
        applyState(collapsed);

        try {
            localStorage.setItem(storageKey, collapsed ? '1' : '0');
        } catch {
            // Local storage may be unavailable; keep the state for this page.
        }
    });
})();

(() => {
    const panel = document.querySelector('[data-time-tracked-panel]');
    if (!panel) return;

    const table = panel.querySelector('[data-time-tracked-table]');
    const empty = panel.querySelector('[data-time-tracked-empty]');
    const availableNotes = document.getElementById('timeAvailableNotes');
    const taskOptions = document.getElementById('timeTaskOptions');

    const refreshVisibility = () => {
        const hasRows = Boolean(
            table?.querySelector('[data-time-tracked-note-id]'));

        if (table) {
            table.hidden = !hasRows;
        }

        if (empty) {
            empty.hidden = hasRows;
        }
    };

    const addAvailableNote = (noteId, title) => {
        if (!availableNotes || !noteId || !title) return;

        const exists = Array.from(availableNotes.options).some(
            (option) => option.dataset.noteId === String(noteId));

        if (!exists) {
            const option = document.createElement('option');
            option.value = title;
            option.dataset.noteId = String(noteId);
            availableNotes.appendChild(option);
        }
    };

    const markTaskOptionUnpinned = (noteId) => {
        if (!taskOptions) return;

        const option = Array.from(taskOptions.options).find(
            (item) => item.dataset.noteId === String(noteId));

        if (!option) return;

        option.dataset.pinned = 'false';
        option.label = option.value;

        const sorted = Array.from(taskOptions.options).sort((left, right) => {
            const leftPinned = left.dataset.pinned === 'true' ? 1 : 0;
            const rightPinned = right.dataset.pinned === 'true' ? 1 : 0;

            if (leftPinned !== rightPinned) {
                return rightPinned - leftPinned;
            }

            return left.value.localeCompare(
                right.value,
                undefined,
                { sensitivity: 'base' });
        });

        sorted.forEach((item) => taskOptions.appendChild(item));
    };

    const removeTrackedRow = (noteId) => {
        const row = panel.querySelector(
            `[data-time-tracked-note-id="${noteId}"]`);

        if (!row) return;

        const title =
            row.querySelector('.time-note-title')?.textContent?.trim() || '';

        addAvailableNote(noteId, title);
        markTaskOptionUnpinned(noteId);
        row.remove();
    };

    document.addEventListener('submit', async (event) => {
        const singleForm = event.target.closest('[data-time-unpin-form]');
        const allForm = event.target.closest('[data-time-unpin-all-form]');
        const form = singleForm || allForm;

        if (!form) return;

        event.preventDefault();

        if (allForm) {
            const confirmed = typeof window.noteKeeperConfirm === 'function'
                ? await window.noteKeeperConfirm({
                    title: form.dataset.confirmTitle,
                    message: form.dataset.confirmMessage,
                    confirmLabel: form.dataset.confirmLabel,
                    danger: true
                })
                : window.confirm(form.dataset.confirmMessage || '');

            if (!confirmed) return;
        }

        const button = form.querySelector('button[type="submit"]');
        if (button) {
            button.disabled = true;
        }

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    Accept: 'application/json'
                }
            });

            if (!response.ok) {
                throw new Error('Could not update tracked notes.');
            }

            const result = await response.json();
            const ids = allForm
                ? result.removedNoteIds || []
                : [result.removedNoteId];

            ids
                .filter((id) => id !== null && id !== undefined)
                .forEach(removeTrackedRow);

            refreshVisibility();
        } catch {
            if (button) {
                button.disabled = false;
            }
        }
    });

    refreshVisibility();
})();
