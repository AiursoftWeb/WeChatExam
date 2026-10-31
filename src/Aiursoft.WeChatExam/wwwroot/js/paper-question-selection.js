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

// Native suggestions keep single-tag selection usable with mouse, keyboard and manual entry.
document.querySelectorAll('[data-tag-suggestions-url]').forEach(input => {
    const list = document.getElementById(input.getAttribute('list'));
    let timer;
    let request;
    let composing = false;
    const suggest = () => {
        clearTimeout(timer);
        request?.abort();
        list.replaceChildren();
        const query = input.value.trim();
        if (!query || composing) return;
        timer = setTimeout(async () => {
            const current = new AbortController();
            request = current;
            try {
                const url = new URL(input.dataset.tagSuggestionsUrl, window.location.origin);
                url.searchParams.set('query', query);
                const response = await fetch(url, { signal: current.signal });
                if (!response.ok) return;
                const tags = await response.json();
                if (current.signal.aborted || input.value.trim() !== query) return;
                list.replaceChildren(...tags.map(tag => {
                    const option = document.createElement('option');
                    option.value = tag.displayName;
                    return option;
                }));
            } catch (error) {
                if (error.name !== 'AbortError') list.replaceChildren();
                // Exact tag entry still works when suggestions are unavailable.
            }
        }, 250);
    };
    input.addEventListener('input', suggest);
    input.addEventListener('compositionstart', () => { composing = true; suggest(); });
    input.addEventListener('compositionend', () => { composing = false; suggest(); });
});

document.querySelectorAll('[data-add-matching]').forEach(form => {
    window.addEventListener('pageshow', () => { form.querySelector('button[type="submit"]').disabled = false; });
    form.addEventListener('submit', () => {
        if (form.checkValidity()) form.querySelector('button[type="submit"]').disabled = true;
    });
});
