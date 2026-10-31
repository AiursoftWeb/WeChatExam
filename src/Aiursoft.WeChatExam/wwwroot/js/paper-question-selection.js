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

// One exact tag, using the same list-group presentation as the existing tag editor.
document.querySelectorAll('[data-tag-suggestions-url]').forEach(input => {
    const list = document.getElementById(input.getAttribute('aria-controls'));
    list.style.zIndex = '1000';
    list.style.maxHeight = '16rem';
    list.style.overflowY = 'auto';
    let timer;
    let request;
    let composing = false;
    let active = -1;
    const close = () => {
        list.hidden = true;
        input.setAttribute('aria-expanded', 'false');
        input.removeAttribute('aria-activedescendant');
        active = -1;
    };
    const choose = value => {
        input.value = value;
        close();
        input.focus();
    };
    const suggest = () => {
        clearTimeout(timer);
        request?.abort();
        close();
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
                if (current.signal.aborted || input.value.trim() !== query || document.activeElement !== input) return;
                list.replaceChildren(...tags.map((tag, index) => {
                    const option = document.createElement('button');
                    option.type = 'button';
                    option.className = 'list-group-item list-group-item-action text-break';
                    option.id = `${list.id}-${index}`;
                    option.setAttribute('role', 'option');
                    option.setAttribute('aria-selected', 'false');
                    option.tabIndex = -1;
                    option.textContent = tag.displayName;
                    option.addEventListener('pointerdown', event => event.preventDefault());
                    option.addEventListener('click', () => choose(tag.displayName));
                    return option;
                }));
                list.hidden = tags.length === 0;
                input.setAttribute('aria-expanded', String(tags.length > 0));
            } catch (error) {
                if (error.name !== 'AbortError' && request === current) close();
                // Exact tag entry still works when suggestions are unavailable.
            }
        }, 250);
    };
    input.addEventListener('input', suggest);
    input.addEventListener('compositionstart', () => { composing = true; suggest(); });
    input.addEventListener('compositionend', () => { composing = false; suggest(); });
    input.addEventListener('blur', close);
    input.addEventListener('keydown', event => {
        if (composing || event.isComposing || list.hidden) return;
        const options = [...list.children];
        if (event.key === 'Escape') {
            event.preventDefault();
            close();
        } else if (event.key === 'Enter' && active >= 0) {
            event.preventDefault();
            choose(options[active].textContent);
        } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            active = active < 0 ? (event.key === 'ArrowDown' ? 0 : options.length - 1)
                : (active + (event.key === 'ArrowDown' ? 1 : options.length - 1)) % options.length;
            options.forEach((option, index) => {
                option.classList.toggle('active', index === active);
                option.setAttribute('aria-selected', String(index === active));
            });
            input.setAttribute('aria-activedescendant', options[active].id);
            options[active].scrollIntoView({ block: 'nearest' });
        }
    });
});

document.querySelectorAll('[data-add-matching]').forEach(form => {
    window.addEventListener('pageshow', () => { form.querySelector('button[type="submit"]').disabled = false; });
    form.addEventListener('submit', () => {
        if (form.checkValidity()) form.querySelector('button[type="submit"]').disabled = true;
    });
});
