(() => {
    const total = document.getElementById('totalCategoryId');
    const period = document.getElementById('period');
    if (!total || !period) return;

    function updatePeriods() {
        for (const group of period.querySelectorAll('optgroup')) {
            const excluded = total.value !== '' && group.dataset.totalCategoryId !== total.value;
            group.hidden = excluded;
            group.disabled = excluded;
            if (excluded && [...group.children].some(option => option.selected)) period.value = '';
        }
    }

    total.addEventListener('change', updatePeriods);
    updatePeriods();
})();
