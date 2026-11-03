document.querySelectorAll('form[data-edit-form]').forEach(form => {
    const original = new URLSearchParams(new FormData(form)).toString();
    let submitting = false;

    window.addEventListener('submit', event => {
        if (event.target === form && !event.defaultPrevented) submitting = true;
    });
    window.addEventListener('pageshow', () => { submitting = false; });
    window.addEventListener('beforeunload', event => {
        if (!submitting && (form.dataset.editDirty === 'true' || new URLSearchParams(new FormData(form)).toString() !== original)) {
            event.preventDefault();
            event.returnValue = '';
        }
    });
});
