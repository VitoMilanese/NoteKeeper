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
