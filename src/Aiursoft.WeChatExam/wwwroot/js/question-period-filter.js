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
        // An unassigned question has no total category yet.
        const unassigned = period.querySelector('option[value="unassigned"]');
        if (unassigned) {
            unassigned.hidden = total.value !== '';
            unassigned.disabled = total.value !== '';
            if (unassigned.disabled && unassigned.selected) period.value = '';
        }
    }

    total.addEventListener('change', updatePeriods);
    updatePeriods();
})();
