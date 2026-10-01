const locale = document.documentElement.lang || 'en';

document.querySelectorAll('.js-local-time').forEach((element) => {
    const raw = element.getAttribute('datetime');
    if (!raw) return;

    const date = new Date(raw);
    if (Number.isNaN(date.getTime())) return;

    element.textContent = new Intl.DateTimeFormat(locale, {
        dateStyle: 'medium',
        timeStyle: 'short'
    }).format(date);
});

(() => {
    const root = document.documentElement;
    const toggle = document.querySelector('[data-theme-toggle]');
    if (!toggle) return;

    const icon = toggle.querySelector('[data-theme-icon]');

    function updateThemeButton() {
        const current = root.dataset.theme === 'light' ? 'light' : 'dark';
        const label = current === 'dark' ? toggle.dataset.lightLabel : toggle.dataset.darkLabel;
        toggle.title = label || '';
        toggle.setAttribute('aria-label', label || '');
        if (icon) icon.textContent = current === 'dark' ? '☀' : '☾';
    }

    toggle.addEventListener('click', () => {
        const next = root.dataset.theme === 'light' ? 'dark' : 'light';
        root.dataset.theme = next;
        const secure = window.location.protocol === 'https:' ? '; Secure' : '';
        document.cookie = `notekeeper_theme=${next}; Max-Age=31536000; Path=/; SameSite=Lax${secure}`;
        updateThemeButton();
    });

    updateThemeButton();
})();

(() => {
    const dialog = document.getElementById('confirmDialog');
    if (!dialog) return;

    const titleElement = dialog.querySelector('[data-confirm-title]');
    const messageElement = dialog.querySelector('[data-confirm-message]');
    const acceptButton = dialog.querySelector('[data-confirm-accept]');
    const iconElement = dialog.querySelector('[data-confirm-icon]');

    window.noteKeeperConfirm = (options = {}) => new Promise((resolve) => {
        titleElement.textContent = options.title || titleElement.textContent || '';
        messageElement.textContent = options.message || '';
        acceptButton.textContent = options.confirmLabel || acceptButton.textContent || '';

        const danger = Boolean(options.danger);
        acceptButton.classList.toggle('button-danger', danger);
        acceptButton.classList.toggle('button-primary', !danger);
        iconElement.classList.toggle('is-danger', danger);
        iconElement.textContent = danger ? '!' : '?';

        const onClose = () => {
            dialog.removeEventListener('close', onClose);
            resolve(dialog.returnValue === 'confirm');
        };

        dialog.addEventListener('close', onClose);
        dialog.showModal();
    });

    document.addEventListener('submit', async (event) => {
        const form = event.target.closest('form[data-confirm-form]');
        if (!form || form.dataset.confirmBypass === 'true') return;

        event.preventDefault();

        const confirmed = await window.noteKeeperConfirm({
            title: form.dataset.confirmTitle,
            message: form.dataset.confirmMessage,
            confirmLabel: form.dataset.confirmLabel,
            danger: form.dataset.confirmDanger === 'true'
        });

        if (!confirmed) return;

        form.dataset.confirmBypass = 'true';
        form.submit();
    });
})();
