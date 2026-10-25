document.addEventListener('DOMContentLoaded', () => {
    const tagInput = document.getElementById('tagInputField');
    const tags = document.getElementById('Tags');
    if (tagInput && tags && typeof TagInput !== 'undefined')
        TagInput.init(tagInput, tags, (tags.value || '').split(/\s+/).filter(Boolean));

    const panel = document.querySelector('[data-paper-question-search]');
    if (!panel) return;
    const labels = document.querySelector('[data-paper-search-localization]');
    const translate = (key, fallback) => labels?.querySelector(`[data-key="${key}"]`)?.textContent || fallback;
    const element = (tag, className, text) => {
        const node = document.createElement(tag);
        node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    };
    const mode = panel.querySelector('#question-search-mode');
    const finder = panel.querySelector('#question-search-input');
    const type = panel.querySelector('#typeFilter');
    const help = panel.querySelector('#question-search-help');
    const button = panel.querySelector('#find-questions');
    const status = panel.querySelector('#question-search-status');
    const results = panel.querySelector('#search-results-container');
    const selectAll = panel.querySelector('#selectAllQuestions');
    const options = panel.querySelector('#batchAddOptions');
    const selectedList = panel.querySelector('#selected-questions-list');
    const inputs = panel.querySelector('#selectedQuestionsContainer');
    const add = panel.querySelector('#batchAddBtn');
    const selected = new Map();
    let pendingSearch;

    function updateSelection() {
        selectedList.replaceChildren();
        inputs.replaceChildren();
        for (const question of selected.values()) {
            const row = element('div', 'list-group-item d-flex align-items-center gap-2');
            row.append(element('span', 'text-break flex-grow-1', question.content));
            const remove = element('button', 'btn btn-sm btn-outline-danger flex-shrink-0 text-nowrap', translate('remove', 'Remove'));
            remove.type = 'button';
            remove.addEventListener('click', () => { selected.delete(question.id); updateSelection(); });
            row.append(remove); selectedList.append(row);
            const input = element('input', '');
            input.type = 'hidden'; input.name = 'questionIds'; input.value = question.id;
            inputs.append(input);
        }
        const checkboxes = [...results.querySelectorAll('input[type="checkbox"]')];
        checkboxes.forEach(checkbox => { checkbox.checked = selected.has(checkbox.value); });
        const checkedCount = checkboxes.filter(checkbox => checkbox.checked).length;
        selectAll.disabled = checkboxes.length === 0;
        selectAll.checked = checkboxes.length > 0 && checkedCount === checkboxes.length;
        selectAll.indeterminate = checkedCount > 0 && checkedCount < checkboxes.length;
        options.hidden = selected.size === 0;
        add.disabled = selected.size === 0;
        add.textContent = translate('add-selected', 'Add Selected ({0})').replace('{0}', String(selected.size));
    }

    function questionRow(question) {
        const row = element('div', 'list-group-item d-flex align-items-start gap-2');
        const checkbox = element('input', 'form-check-input mt-1 flex-shrink-0');
        checkbox.type = 'checkbox'; checkbox.value = question.id;
        checkbox.setAttribute('aria-label', translate('select-question', 'Select Question'));
        checkbox.addEventListener('change', () => {
            if (checkbox.checked) selected.set(question.id, question);
            else selected.delete(question.id);
            updateSelection();
        });
        const content = element('div', 'flex-grow-1 text-break');
        const typeLabel = [...type.options].find(option => option.value === question.questionType)?.textContent || question.questionType;
        content.append(element('div', 'small text-muted mb-1', typeLabel),
            element('p', 'mb-1', question.content.length > 160 ? question.content.slice(0, 160) + '…' : question.content));
        const preview = element('details', '');
        preview.append(element('summary', '', translate('preview', 'Preview')),
            element('p', 'mt-2 mb-0 text-break', question.preview ?? question.content));
        if (question.answer) preview.append(element('strong', '', translate('answer', 'Answer')),
            element('p', 'mb-0 text-break', question.answer));
        if (question.explanation) preview.append(element('p', 'mb-0 text-break text-muted', question.explanation));
        content.append(preview); row.append(checkbox, content);
        return row;
    }

    async function findQuestions() {
        pendingSearch?.abort();
        const controller = new AbortController(); pendingSearch = controller;
        const searchMode = mode.value;
        const query = finder.value.trim();
        status.replaceChildren();
        results.replaceChildren();
        updateSelection();
        if (!query && searchMode !== 'mtql') {
            results.append(element('p', 'list-group-item text-muted', translate('enter-search', 'Enter a few words to find questions.')));
            return;
        }
        button.disabled = true;
        results.append(element('p', 'list-group-item text-muted', translate('searching', 'Searching...')));
        try {
            const parameters = new URLSearchParams({ mode: searchMode, questionType: type.value,
                [searchMode === 'mtql' ? 'mtql' : 'query']: query });
            const response = await fetch(`${panel.dataset.endpoint}?${parameters}`, { signal: controller.signal });
            if (!response.ok || response.redirected) throw new Error('Question search failed');
            const data = await response.json();
            if (pendingSearch !== controller) return;
            const items = searchMode === 'mtql' ? data : data.results;
            const usedAi = searchMode !== 'mtql' && data.usedAi;
            const label = searchMode === 'mtql' ? translate('mtql-search', 'Tag Expression Search (MTQL)')
                : usedAi ? translate('ai-search', 'AI Semantic Search') : translate('keyword-search', 'Keyword Search');
            status.append(element('span', `badge ${usedAi ? 'bg-primary' : 'bg-secondary'}`, label));
            if (searchMode === 'smart' && !usedAi)
                status.append(element('span', 'text-muted ms-2', translate('ai-fallback', 'AI semantic search was not used. Showing keyword results.')));
            results.replaceChildren(...items.map(questionRow));
            if (!items.length) results.append(element('p', 'list-group-item text-muted', translate('no-questions', 'No questions found.')));
            updateSelection();
        } catch (error) {
            if (pendingSearch === controller && error.name !== 'AbortError')
                results.replaceChildren(element('p', 'list-group-item text-danger', translate('search-failed', 'Search is unavailable. Please try again.')));
        } finally {
            if (pendingSearch === controller) button.disabled = false;
        }
    }
    async function findSimilar(source) {
        pendingSearch?.abort();
        const controller = new AbortController(); pendingSearch = controller;
        status.replaceChildren(); results.replaceChildren(); updateSelection();
        button.disabled = true;
        status.append(element('p', 'text-break mb-1', translate('similar-to', 'Similar to: {0}').replace('{0}', () => source.dataset.questionTitle)),
            element('p', 'text-muted mb-1', translate('similar-help', 'Find up to five similar questions. Existing paper questions are excluded; the original question stays unchanged.')));
        results.append(element('p', 'list-group-item text-muted', translate('searching', 'Searching...')));
        panel.scrollIntoView({ block: 'nearest' });
        try {
            const response = await fetch(panel.dataset.similarEndpoint, {
                method: 'POST', signal: controller.signal,
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: panel.querySelector('[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ id: source.dataset.similarQuestion,
                    categoryIds: [document.getElementById('SelectedCategoryId').value].filter(Boolean), excludeIds: [...selected.keys()] })
            });
            if (!response.ok || response.redirected) throw new Error('Similar question search failed');
            const data = await response.json();
            if (pendingSearch !== controller) return;
            if (!data.available) throw new Error('Similar question search unavailable');
            status.prepend(element('span', 'badge bg-primary mb-1', translate('ai-search', 'AI Semantic Search')));
            results.replaceChildren(...data.results.map(questionRow));
            if (!data.results.length) results.append(element('p', 'list-group-item text-muted', translate('no-questions', 'No questions found.')));
            updateSelection();
        } catch (error) {
            if (pendingSearch === controller && error.name !== 'AbortError')
                results.replaceChildren(element('p', 'list-group-item text-muted', translate('similar-unavailable', 'Similar questions are unavailable. Please try again later.')));
        } finally {
            if (pendingSearch === controller) button.disabled = false;
        }
    }
    document.querySelectorAll('[data-similar-question]').forEach(source => source.addEventListener('click', () => findSimilar(source)));
    document.getElementById('SelectedCategoryId').addEventListener('change', () => {
        pendingSearch?.abort(); pendingSearch = null;
        button.disabled = false;
        status.replaceChildren(); results.replaceChildren(); updateSelection();
    });
    mode.addEventListener('change', () => {
        pendingSearch?.abort(); pendingSearch = null;
        button.disabled = false;
        status.replaceChildren(); results.replaceChildren(); updateSelection();
        help.textContent = mode.value === 'mtql' ? translate('mtql-help', 'Combine tags, for example: rock && not metal')
            : mode.value === 'keywords' ? translate('keywords-help', 'Match words in the question text, answers and explanations.')
                : translate('smart-help', 'Find exact words and related meanings.');
        finder.placeholder = mode.value === 'mtql' ? translate('mtql-placeholder', 'e.g. rock && not metal')
            : translate('text-placeholder', 'Enter a topic or a few words...');
    });
    button.addEventListener('click', findQuestions);
    finder.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); findQuestions(); } });
    selectAll.addEventListener('change', () => {
        const checked = selectAll.checked;
        for (const checkbox of results.querySelectorAll('input[type="checkbox"]')) {
            if (checkbox.checked !== checked) { checkbox.checked = checked; checkbox.dispatchEvent(new Event('change')); }
        }
    });
    panel.querySelector('#batchAddForm').addEventListener('submit', event => { if (selected.size === 0) event.preventDefault(); });
    updateSelection();
});
