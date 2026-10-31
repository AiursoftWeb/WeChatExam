document.querySelectorAll('[data-question-selection]').forEach(form => {
    const questions = [...form.querySelectorAll('input[name="QuestionIds"]')];
    const selectPage = form.querySelector('[data-select-page]');
    const count = form.querySelector('[data-selection-count]');
    const add = form.querySelector('[data-add-selected]');
    const format = count.dataset.countFormat || '{0} selected on this page';

    const update = () => {
        const selected = questions.filter(question => question.checked).length;
        selectPage.checked = selected === questions.length;
        selectPage.indeterminate = selected > 0 && selected < questions.length;
        count.textContent = format.replace('{0}', selected);
        add.disabled = selected === 0;
    };

    // Only the rendered page is selected; there is no hidden selection across pages or filters.
    selectPage.addEventListener('change', () => {
        questions.forEach(question => { question.checked = selectPage.checked; });
        update();
    });
    questions.forEach(question => question.addEventListener('change', update));
    window.addEventListener('pageshow', update);
    form.querySelector('[data-selection-tools]').classList.remove('d-none');
    update();
});
