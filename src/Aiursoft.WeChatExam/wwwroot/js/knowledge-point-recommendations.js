document.addEventListener('DOMContentLoaded', () => {
    const form = document.querySelector('form[data-content-editor]');
    const panel = document.querySelector('[data-knowledge-point-recommendations]');
    if (!form || !panel) return;
    const target = panel.querySelector('[data-knowledge-point-results]');
    const refresh = panel.querySelector('[data-refresh-knowledge-points]');
    const localize = (key, fallback) => panel.querySelector(`[data-key="${key}"]`)?.textContent || fallback;
    const element = (tag, className, text) => {
        const node = document.createElement(tag);
        node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    };
    const card = point => {
        const node = element('div', 'border-bottom pb-3 mb-3');
        node.dataset.similarKnowledgePointId = point.id;
        node.append(element('p', 'text-break fw-semibold mb-2', point.title));
        const preview = element('details', 'mb-2');
        preview.append(element('summary', '', localize('preview', 'Preview')),
            element('p', 'text-break mt-2 mb-2', point.preview));
        node.append(preview);
        const actions = element('div', 'd-flex flex-wrap gap-2');
        for (const [url, key, fallback] of [[panel.dataset.detailsUrl, 'open', 'Open Details'], [panel.dataset.editUrl, 'edit', 'Edit in New Tab']]) {
            const link = element('a', 'btn btn-sm btn-outline-secondary', localize(key, fallback));
            link.href = `${url}?id=${encodeURIComponent(point.id)}`;
            link.target = '_blank'; link.rel = 'noopener noreferrer';
            actions.append(link);
        }
        node.append(actions);
        return node;
    };
    const isDraft = panel.dataset.draft === 'true';
    let activeRequest;
    async function load(draft = false) {
        const text = draft ? `${form.querySelector('#Title')?.value || ''}\n${form.querySelector('#Content')?.value || ''}` : null;
        if (isDraft && !text?.trim()) {
            target.replaceChildren(element('p', 'text-muted mb-0', localize('draft-prompt', 'Enter a title or content, then find similar knowledge points.')));
            return;
        }
        activeRequest?.abort();
        const controller = new AbortController();
        activeRequest = controller;
        refresh.disabled = true;
        target.replaceChildren(element('p', 'text-muted mb-0', localize('loading', 'Finding similar knowledge points...')));
        try {
            const response = await fetch(panel.dataset.endpoint, {
                method: 'POST', signal: controller.signal,
                headers: { 'Content-Type': 'application/json',
                    RequestVerificationToken: form.querySelector('input[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ id: panel.dataset.contentId, text, refresh: draft })
            });
            if (!response.ok || response.redirected) throw new Error('Recommendation request failed');
            const data = await response.json();
            if (activeRequest !== controller) return;
            if (!data.available) target.replaceChildren(element('p', 'text-muted mb-0', localize('unavailable', 'Recommendations are unavailable. Please try again later.')));
            else if (!data.results.length) target.replaceChildren(element('p', 'text-muted mb-0', localize('empty', 'No similar knowledge points found yet.')));
            else target.replaceChildren(...data.results.map(card));
        } catch (error) {
            if (error.name !== 'AbortError' && activeRequest === controller)
                target.replaceChildren(element('p', 'text-muted mb-0', localize('unavailable', 'Recommendations are unavailable. Please try again later.')));
        } finally {
            if (activeRequest === controller) refresh.disabled = false;
        }
    }
    refresh.addEventListener('click', () => load(true));
    if (isDraft) target.append(element('p', 'text-muted mb-0', localize('draft-prompt', 'Enter a title or content, then find similar knowledge points.')));
    else load();
});
