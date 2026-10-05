(() => {
    const dialog = document.getElementById('projectRenameDialog');
    const form = dialog?.querySelector('[data-project-rename-form]');
    const input = dialog?.querySelector('[data-project-rename-input]');
    const cancel = dialog?.querySelector('[data-project-rename-cancel]');

    if (!dialog || !form || !input) return;

    document.querySelectorAll('[data-project-rename]').forEach((button) => {
        button.addEventListener('click', () => {
            const projectId = button.dataset.projectId;
            if (!projectId) return;

            form.action = `/projects/${projectId}/rename`;
            input.value = button.dataset.projectName || '';
            dialog.showModal();

            window.requestAnimationFrame(() => {
                input.focus();
                input.select();
            });
        });
    });

    cancel?.addEventListener('click', () => dialog.close());

    dialog.addEventListener('click', (event) => {
        if (event.target === dialog) {
            dialog.close();
        }
    });

    dialog.addEventListener('close', () => {
        form.action = '';
        input.value = '';
    });
})();
