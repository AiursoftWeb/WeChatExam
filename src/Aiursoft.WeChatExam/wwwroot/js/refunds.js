document.querySelectorAll('[data-refund-form]').forEach(form => {
    form.addEventListener('submit', () => {
        form.querySelectorAll('button').forEach(button => {
            button.disabled = true;
            if (button.dataset.busyText) button.textContent = button.dataset.busyText;
        });
    });
});
document.querySelector('[data-copy-refund-guide]')?.addEventListener('click', async event => {
    const button = event.currentTarget;
    const guide = document.getElementById('apple-refund-guide');
    const result = document.getElementById('copy-refund-result');
    try {
        await navigator.clipboard.writeText(guide.value);
        result.textContent = button.dataset.successText;
    } catch {
        guide.focus();
        guide.select();
        result.textContent = button.dataset.failureText;
    }
});
