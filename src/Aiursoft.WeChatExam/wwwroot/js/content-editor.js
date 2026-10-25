document.addEventListener('DOMContentLoaded', () => {
    const form = document.querySelector('form[data-content-editor]');
    if (!form) return;
    const panel = document.querySelector('[data-question-recommendations]');
    const labels = document.querySelector('[data-editor-localization]');
    const translate = (key, fallback) => labels?.querySelector(`[data-key="${key}"]`)?.textContent || fallback;
    const selected = document.getElementById('selected-questions-container');
    const selectedIds = () => [...form.querySelectorAll('input[name="SelectedQuestionIds"]')].map(input => input.value);
    const element = (tag, className, text) => {
        const node = document.createElement(tag);
        node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    };

    function selectQuestion(question) {
        if (!selected || selectedIds().includes(question.id)) return;
        const row = element('div', 'list-group-item d-flex gap-2 justify-content-between align-items-center');
        row.dataset.questionId = question.id;
        row.append(element('span', 'text-break', question.title));
        const remove = element('button', 'btn btn-sm btn-outline-danger flex-shrink-0 text-nowrap', translate('remove', 'Remove'));
        remove.type = 'button';
        remove.dataset.removeQuestion = '';
        const hidden = element('input', '');
        hidden.type = 'hidden'; hidden.name = 'SelectedQuestionIds'; hidden.value = question.id;
        row.append(remove, hidden);
        selected.append(row);
        form.dispatchEvent(new Event('question-selection-changed'));
    }
    selected?.addEventListener('click', event => {
        const button = event.target.closest('[data-remove-question]');
        if (!button) return;
        button.closest('[data-question-id]').remove();
        form.dispatchEvent(new Event('question-selection-changed'));
    });

    if (panel) {
        const target = panel.querySelector('[data-recommendation-results]');
        const refresh = panel.querySelector('[data-refresh-recommendations]');
        const localize = (key, fallback) => panel.querySelector(`[data-key="${key}"]`)?.textContent || fallback;
        const isKnowledgePoint = panel.dataset.knowledgePoint === 'true';
        const isDraft = panel.dataset.draft === 'true';
        let activeRequest;
        let currentText = null;
        let selectedDraft = null;

        function questionCard(question) {
            const card = element('div', 'border-bottom pb-3 mb-3');
            card.dataset.recommendedQuestionId = question.id;
            const meta = element('div', 'small text-muted mb-1',
                [localize(`type-${question.questionType}`, ''), question.category].filter(Boolean).join(' · '));
            card.append(meta, element('p', 'text-break mb-2', question.title.length > 160 ? question.title.slice(0, 160) + '…' : question.title));
            const preview = element('details', 'mb-2');
            preview.append(element('summary', '', localize('preview', 'Preview')),
                element('p', 'text-break mt-2 mb-2', question.title));
            if (question.answer) preview.append(element('strong', '', localize('answer', 'Answer')),
                element('p', 'text-break mb-2', question.answer));
            if (question.preview) preview.append(element('p', 'text-break text-muted', question.preview));
            card.append(preview);
            const actions = element('div', 'd-flex flex-wrap gap-2');
            for (const [url, key, fallback] of [[panel.dataset.detailsUrl, 'open', 'Open Details'], [panel.dataset.editUrl, 'edit', 'Edit in New Tab']]) {
                if (key === 'edit' && panel.dataset.canEdit !== 'true') continue;
                const link = element('a', 'btn btn-sm btn-outline-secondary', localize(key, fallback));
                link.href = `${url}?id=${encodeURIComponent(question.id)}`;
                link.target = '_blank'; link.rel = 'noopener noreferrer';
                actions.append(link);
            }
            if (isKnowledgePoint) {
                const add = element('button', 'btn btn-sm btn-primary', localize('associate', 'Associate'));
                add.type = 'button'; add.addEventListener('click', () => selectQuestion(question)); actions.prepend(add);
            }
            card.append(actions);
            return card;
        }

        async function loadRecommendations(forceRefresh = false) {
            activeRequest?.abort();
            const controller = new AbortController();
            activeRequest = controller;
            refresh.disabled = true;
            target.replaceChildren(element('p', 'text-muted mb-0', localize('loading', 'Finding related questions...')));
            // Knowledge-point categories can be more specific than question categories.
            const categories = selectedDraft?.categoryIds ?? (isKnowledgePoint
                ? []
                : [form.querySelector('#CategoryId')?.value].filter(Boolean));
            try {
                const response = await fetch(panel.dataset.endpoint, {
                    method: 'POST', signal: controller.signal,
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': form.querySelector('input[name="__RequestVerificationToken"]').value },
                    body: JSON.stringify({ id: panel.dataset.contentId, text: currentText, categoryIds: categories,
                        excludeIds: selectedIds(), refresh: forceRefresh })
                });
                if (!response.ok || response.redirected) throw new Error('Recommendation request failed');
                const data = await response.json();
                if (activeRequest !== controller) return;
                if (!data.available) target.replaceChildren(element('p', 'text-muted mb-0', localize('unavailable', 'Recommendations are unavailable. Please try again later.')));
                else if (!data.results.length) target.replaceChildren(element('p', 'text-muted mb-0', localize('empty', 'No related questions found yet.')));
                else target.replaceChildren(...data.results.map(questionCard));
            } catch (error) {
                if (error.name !== 'AbortError' && activeRequest === controller) target.replaceChildren(element('p', 'text-muted mb-0', localize('unavailable', 'Recommendations are unavailable. Please try again later.')));
            } finally {
                if (activeRequest === controller) refresh.disabled = false;
            }
        }
        if (isDraft) form.addEventListener('question-draft-selected', event => {
            activeRequest?.abort();
            activeRequest = null;
            refresh.disabled = false;
            selectedDraft = event.detail;
            currentText = selectedDraft?.text ?? null;
            if (currentText?.trim()) loadRecommendations(true);
            else target.replaceChildren(element('p', 'text-muted mb-0', localize('draft-prompt', 'Enter question content, then find similar questions.')));
        });
        refresh.addEventListener('click', () => {
            if (selectedDraft) { loadRecommendations(true); return; }
            const content = form.querySelector('#Content')?.value || '';
            if (isDraft && !content.trim()) {
                target.replaceChildren(element('p', 'text-muted mb-0', localize('draft-prompt', 'Enter question content, then find similar questions.')));
                return;
            }
            currentText = isKnowledgePoint ? `${form.querySelector('#Title')?.value || ''}\n${content}`
                : [content, ...[...form.querySelectorAll('.option-input:not(:disabled)')].map(input => input.value),
                    form.querySelector('input[name="StandardAnswer"]:checked:not(:disabled)')?.value || form.querySelector('#standard-answer-input:not(:disabled)')?.value || '',
                    form.querySelector('#Explanation')?.value || ''].join('\n');
            loadRecommendations(true);
        });
        form.addEventListener('question-selection-changed', () => loadRecommendations());
        if (isDraft) target.append(element('p', 'text-muted mb-0', localize('draft-prompt', 'Enter question content, then find similar questions.')));
        else loadRecommendations();
    }

    const searchButton = document.getElementById('find-questions');
    const finder = document.getElementById('question-search-input');
    if (searchButton && finder) {
        const results = document.getElementById('search-results-container');
        const mode = document.getElementById('question-search-mode');
        const status = document.getElementById('question-search-status');
        const help = document.getElementById('question-search-help');
        let pendingSearch;
        form.addEventListener('question-selection-changed', () => {
            for (const button of results.querySelectorAll('[data-associate-question]'))
                button.disabled = selectedIds().includes(button.dataset.associateQuestion);
        });
        mode.addEventListener('change', () => {
            pendingSearch?.abort();
            pendingSearch = null;
            status.replaceChildren();
            results.replaceChildren();
            const helpText = mode.value === 'mtql' ? translate('mtql-help', 'Combine tags, for example: rock && not metal')
                : mode.value === 'keywords' ? translate('keywords-help', 'Match words in the question text, answers and explanations.')
                    : translate('smart-help', 'Find exact words and related meanings.');
            help.textContent = helpText;
            finder.placeholder = mode.value === 'mtql' ? translate('mtql-placeholder', 'e.g. rock && not metal')
                : translate('text-placeholder', 'Enter a topic or a few words...');
        });
        async function findQuestions() {
            pendingSearch?.abort();
            const controller = new AbortController(); pendingSearch = controller;
            const query = finder.value.trim();
            const type = document.getElementById('type-filter').value;
            const searchMode = mode.value;
            status.replaceChildren();
            if (!query && searchMode !== 'mtql') {
                results.replaceChildren(element('p', 'list-group-item text-muted', translate('enter-search', 'Enter a few words to find questions.')));
                return;
            }
            results.replaceChildren(element('p', 'list-group-item text-muted', translate('searching', 'Searching...')));
            try {
                const url = searchMode === 'mtql' ? form.dataset.mtqlUrl : form.dataset.searchUrl;
                const parameters = new URLSearchParams({ questionType: type, [searchMode === 'mtql' ? 'mtql' : 'query']: query });
                if (searchMode !== 'mtql') {
                    parameters.set('useAi', String(searchMode === 'smart'));
                    parameters.set('includeSearchStatus', 'true');
                }
                const response = await fetch(`${url}?${parameters}`, { signal: controller.signal });
                if (!response.ok || response.redirected) throw new Error('Search request failed');
                const data = await response.json();
                if (pendingSearch !== controller) return;
                const items = searchMode === 'mtql' ? data : data.results;
                const usedAi = searchMode !== 'mtql' && data.usedAi;
                const label = searchMode === 'mtql' ? translate('mtql-search', 'Tag Expression Search (MTQL)')
                    : usedAi ? translate('ai-search', 'AI Semantic Search') : translate('keyword-search', 'Keyword Search');
                status.append(element('span', `badge ${usedAi ? 'bg-primary' : 'bg-secondary'}`, label));
                if (searchMode === 'smart' && !usedAi)
                    status.append(element('span', 'text-muted ms-2', translate('ai-fallback', 'AI semantic search was not used. Showing keyword results.')));
                results.replaceChildren();
                if (!items.length) results.append(element('p', 'list-group-item text-muted', translate('no-questions', 'No questions found.')));
                for (const item of items) {
                    const question = { ...item, title: item.title ?? item.content };
                    const row = element('div', 'list-group-item d-flex align-items-center gap-2');
                    row.append(element('span', 'text-break flex-grow-1', question.title));
                    const add = element('button', 'btn btn-sm btn-outline-primary flex-shrink-0 text-nowrap', translate('associate', 'Associate'));
                    add.type = 'button'; add.dataset.associateQuestion = question.id;
                    add.disabled = selectedIds().includes(question.id);
                    add.addEventListener('click', () => { selectQuestion(question); add.disabled = true; });
                    row.append(add); results.append(row);
                }
            } catch (error) {
                if (error.name !== 'AbortError') results.replaceChildren(element('p', 'list-group-item text-danger', translate('search-failed', 'Search is unavailable. Please try again.')));
            }
        }
        searchButton.addEventListener('click', findQuestions);
        finder.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); findQuestions(); } });
    }

    const questionType = document.getElementById('QuestionType');
    if (questionType) {
        const loc = (key, fallback) => document.querySelector(`#loc-data [data-key="${key}"]`)?.textContent || fallback;
        const tags = document.getElementById('Tags');
        if (typeof TagInput !== 'undefined') TagInput.init(document.getElementById('tagInputField'), tags, (tags.value || '').split(' ').filter(Boolean));
        const options = document.getElementById('options-list');
        const addOptionButton = document.getElementById('add-option-btn');
        const answerInput = document.getElementById('standard-answer-input');
        const choice = loc('type-choice', '0'); const bool = loc('type-bool', '2');
        function bindOption(row) {
            const input = row.querySelector('.option-input'); const radio = row.querySelector('.answer-radio');
            input.addEventListener('input', () => { radio.value = input.value; });
            row.querySelector('.remove-option-btn').addEventListener('click', () => row.remove());
        }
        function addOption(value = '', checked = false) {
            const row = element('div', 'input-group mb-2 option-item');
            const radioBox = element('div', 'input-group-text'); const radio = element('input', 'form-check-input mt-0 answer-radio');
            radio.type = 'radio'; radio.name = 'StandardAnswer'; radio.value = value; radio.checked = checked; radioBox.append(radio);
            const input = element('input', 'form-control option-input');
            input.type = 'text'; input.name = 'Options'; input.value = value; input.required = true; input.placeholder = loc('option-content', 'Option content');
            const remove = element('button', 'btn btn-outline-danger remove-option-btn', '×');
            remove.type = 'button'; remove.setAttribute('aria-label', loc('remove-option', 'Remove Option'));
            row.append(radioBox, input, remove); options.append(row); bindOption(row);
        }
        function updateOptions() {
            const useOptions = questionType.value === choice || questionType.value === bool;
            document.getElementById('options-section').style.display = useOptions ? 'block' : 'none';
            document.getElementById('standard-answer-text-section').style.display = useOptions ? 'none' : 'block';
            answerInput.disabled = useOptions;
            options.querySelectorAll('.answer-radio').forEach(radio => { radio.disabled = !useOptions; });
            options.querySelectorAll('.option-input').forEach(input => { input.disabled = !useOptions; });
            addOptionButton.style.display = questionType.value === bool ? 'none' : 'inline-block';
            options.querySelectorAll('.remove-option-btn').forEach(button => { button.style.display = questionType.value === bool ? 'none' : 'inline-block'; });
        }
        function populateOptions() {
            const answer = (answerInput.value || '').trim();
            if (questionType.value === bool) {
                const correct = loc('correct', 'Correct'); const incorrect = loc('incorrect', 'Incorrect');
                addOption(correct, answer === correct || answer.toLowerCase() === 'true');
                addOption(incorrect, answer === incorrect || answer.toLowerCase() === 'false');
            } else if (questionType.value === choice) ['A', 'B', 'C', 'D'].forEach(value => addOption(value, answer === value));
        }
        options.querySelectorAll('.option-item').forEach(bindOption);
        addOptionButton.addEventListener('click', () => addOption());
        questionType.addEventListener('change', () => {
            if (questionType.value === bool) options.replaceChildren();
            if (!options.children.length) populateOptions();
            updateOptions();
        });
        if (!options.children.length) populateOptions();
        updateOptions();
    }
});
