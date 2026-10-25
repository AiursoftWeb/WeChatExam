document.addEventListener('DOMContentLoaded', () => {
    const panel = document.querySelector('[data-knowledge-point-finder]');
    if (!panel) return;
    const form = panel.closest('form');
    const strings = new Map([...panel.querySelectorAll('[data-point-localization] [data-key]')]
        .map(node => [node.dataset.key, node.textContent]));
    const t = key => strings.get(key) || key;
    const selected = panel.querySelector('[data-selected-knowledge-points]');
    const ids = () => [...selected.querySelectorAll('input')].map(input => input.value);
    const element = (tag, classes, text) => {
        const node = document.createElement(tag);
        node.className = classes;
        if (text !== undefined) node.textContent = text;
        return node;
    };
    const syncButtons = () => {
        const current = new Set(ids());
        panel.querySelectorAll('[data-associate-point]').forEach(button => {
            button.disabled = current.has(button.dataset.associatePoint);
            button.textContent = t(button.disabled ? 'selected' : 'associate');
        });
    };
    const select = item => {
        if (ids().includes(item.id)) return;
        const row = element('div', 'd-flex align-items-center gap-2 border rounded p-2 mb-2');
        row.dataset.selectedPoint = item.id;
        const input = document.createElement('input');
        input.type = 'hidden'; input.name = 'SelectedKnowledgePointIds'; input.value = item.id;
        const remove = element('button', 'btn btn-sm btn-outline-danger flex-shrink-0 text-nowrap', t('remove'));
        remove.type = 'button'; remove.dataset.removePoint = '';
        row.append(input, element('span', 'flex-grow-1 text-break', item.title), remove);
        selected.append(row);
        syncButtons();
    };
    selected.addEventListener('click', event => {
        const button = event.target.closest('[data-remove-point]');
        if (!button) return;
        button.closest('[data-selected-point]').remove();
        syncButtons();
    });
    const render = (container, results, emptyKey) => {
        container.replaceChildren();
        if (!results.length) container.append(element('p', 'text-muted', t(emptyKey)));
        for (const item of results) {
            const card = element('div', 'border rounded p-2 mb-2 text-break');
            const title = element('div', 'fw-semibold mb-2', item.title);
            const actions = element('div', 'd-flex flex-wrap gap-2');
            const button = element('button', 'btn btn-sm btn-outline-primary', t('associate'));
            button.type = 'button'; button.dataset.associatePoint = item.id;
            button.addEventListener('click', () => select(item));
            const link = element('a', 'btn btn-sm btn-outline-secondary', t('open'));
            link.href = `${panel.dataset.detailsUrl}/${encodeURIComponent(item.id)}`;
            link.target = '_blank'; link.rel = 'noopener';
            actions.append(button, link);
            const preview = element('details', 'mt-2');
            preview.append(element('summary', '', t('preview')), element('p', 'text-break', item.preview));
            card.append(title, actions, preview); container.append(card);
        }
        syncButtons();
    };
    const query = panel.querySelector('[data-point-query]');
    const mode = panel.querySelector('[data-point-search-mode]');
    const status = panel.querySelector('[data-point-search-status]');
    const results = panel.querySelector('[data-point-search-results]');
    let searchRequest;
    const clearSearch = () => {
        searchRequest?.abort(); searchRequest = null;
        results.replaceChildren(); status.textContent = '';
    };
    const search = async () => {
        clearSearch();
        if (!query.value.trim()) { status.textContent = t('enter'); return; }
        const controller = new AbortController(); searchRequest = controller;
        const params = new URLSearchParams({ query: query.value.trim(), useAi: mode.value === 'smart' });
        status.textContent = t('loading');
        try {
            const response = await fetch(`${panel.dataset.searchUrl}?${params}`, { signal: controller.signal });
            if (!response.ok) throw new Error('Search failed');
            const data = await response.json();
            if (searchRequest !== controller) return;
            status.textContent = t(data.usedAi ? 'ai' : mode.value === 'keywords' ? 'keywords' : 'fallback');
            render(results, data.results, 'empty');
        } catch (error) {
            if (error.name !== 'AbortError' && searchRequest === controller) status.textContent = t('failed');
        }
    };
    panel.querySelector('[data-search-points]').addEventListener('click', search);
    query.addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); search(); }
    });
    query.addEventListener('input', clearSearch);
    mode.addEventListener('change', clearSearch);
    const recommendations = panel.querySelector('[data-point-recommendations]');
    const refresh = panel.querySelector('[data-refresh-points]');
    let recommendRequest;
    const recommend = async draft => {
        recommendRequest?.abort();
        const controller = new AbortController(); recommendRequest = controller;
        recommendations.textContent = t('loading'); refresh.disabled = true;
        const content = form.querySelector('#Content')?.value || '';
        const text = draft ? [content, ...[...form.querySelectorAll('.option-input')].map(input => input.value),
            form.querySelector('input[name="StandardAnswer"]:checked')?.value || form.querySelector('#standard-answer-input')?.value || '',
            form.querySelector('#Explanation')?.value || ''].join('\n') : null;
        try {
            const response = await fetch(panel.dataset.recommendUrl, {
                method: 'POST', signal: controller.signal,
                headers: { 'Content-Type': 'application/json',
                    RequestVerificationToken: form.querySelector('[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ id: panel.dataset.contentId, text, excludeIds: ids(), refresh: draft })
            });
            if (!response.ok) throw new Error('Recommendations failed');
            const data = await response.json();
            if (recommendRequest !== controller) return;
            if (data.available) render(recommendations, data.results, 'no-recommendations');
            else recommendations.textContent = t('unavailable');
        } catch (error) {
            if (error.name !== 'AbortError' && recommendRequest === controller) recommendations.textContent = t('unavailable');
        } finally {
            if (recommendRequest === controller) refresh.disabled = false;
        }
    };
    refresh.addEventListener('click', () => recommend(true));
    recommend(false);
});
