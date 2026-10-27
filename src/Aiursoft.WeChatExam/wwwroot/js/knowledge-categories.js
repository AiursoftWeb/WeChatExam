
    import mermaid from '/node_modules/mermaid/dist/mermaid.esm.min.mjs';

    function getLocalizedText(key, defaultText) {
        const el = document.querySelector(`#loc-data span[data-key="${key}"]`);
        return el ? el.innerText : defaultText;
    }

    mermaid.initialize({
        startOnLoad: false,
        maxTextSize: 100000,
        securityLevel: 'loose',
        theme: 'base',
        themeVariables: {
            fontFamily: 'Inter, system-ui, sans-serif',
            fontSize: '14px',
            lineColor: '#607D8B'
        },
        flowchart: {
            htmlLabels: true,
            curve: 'basis',
            useMaxWidth: false
        }
    });

    const renderedGraphs = new Map();
    let renderRequest = 0;

    async function renderMermaid(button) {
        if (!button) return;
        const sourceId = button.dataset.sourceId;
        const graphId = button.dataset.graphId;
        const sourceElement = document.getElementById(sourceId);
        const output = document.getElementById('mermaid-output');
        if (!sourceElement || !output) return;
        const request = ++renderRequest;

        document.querySelectorAll('.category-tree-root-selector').forEach(candidate => {
            const selected = candidate === button;
            candidate.classList.toggle('active', selected);
            candidate.classList.toggle('btn-primary', selected);
            candidate.classList.toggle('btn-outline-primary', !selected);
            candidate.setAttribute('aria-pressed', selected ? 'true' : 'false');
        });

        if (renderedGraphs.has(sourceId)) {
            output.innerHTML = renderedGraphs.get(sourceId);
            return;
        }

        const source = sourceElement.textContent.trim();
        try {
            const { svg } = await mermaid.render(graphId, source);
            renderedGraphs.set(sourceId, svg);
            if (request === renderRequest) {
                output.innerHTML = svg;
            }
        } catch (e) {
            if (request === renderRequest) {
                const message = document.createElement('p');
                message.className = 'text-danger';
                message.textContent = `${getLocalizedText('failed-to-render', 'Failed to render tree:')} ${e.message}`;
                output.replaceChildren(message);
            }
        }
    }

    // Render when tree tab is shown (tab must be visible for foreignObject dimension measurement)
    const treeTab = document.getElementById('tree-tab');
    const rootButtons = Array.from(document.querySelectorAll('.category-tree-root-selector'));
    if (treeTab) {
        treeTab.addEventListener('shown.bs.tab', () => {
            renderMermaid(rootButtons.find(button => button.classList.contains('active')) ?? rootButtons[0]);
        });
    }
    rootButtons.forEach(button => button.addEventListener('click', () => renderMermaid(button)));

import Sortable from '/node_modules/sortablejs/modular/sortable.esm.js';
(function () {

    function getLocalizedText(key, defaultText) {
        const el = document.querySelector(`#loc-data span[data-key="${key}"]`);
        return el ? el.innerText : defaultText;
    }

    const el = document.getElementById('categories-sortable');
    const saveBtn = document.getElementById('save-order-btn');

    if (el) {
        Sortable.create(el, {
            animation: 150,
            handle: '.drag-handle',
            onEnd: function () {
                saveBtn.style.display = 'inline-block';
            }
        });
    }

    if (saveBtn) {
        saveBtn.addEventListener('click', async function () {
            saveBtn.disabled = true;
            const originalHtml = saveBtn.innerHTML;
            saveBtn.innerHTML = `<span class="spinner-border spinner-border-sm me-2"></span> ${getLocalizedText('saving', 'Saving...')}`;

            const ids = Array.from(el.querySelectorAll('tr.category-row')).map(tr => tr.dataset.id);
            const updateUrl = el.dataset.updateUrl;

            try {
                const response = await fetch(updateUrl, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
                    },
                    body: JSON.stringify(ids)
                });

                if (response.ok) {
                    window.location.reload();
                } else {
                    alert(getLocalizedText('failed-to-save', 'Failed to save order.'));
                    saveBtn.disabled = false;
                    saveBtn.innerHTML = originalHtml;
                }
            } catch (error) {
                console.error('Error saving order:', error);
                alert(getLocalizedText('error-occurred', 'An error occurred while saving order.'));
                saveBtn.disabled = false;
                saveBtn.innerHTML = originalHtml;
            }
        });
    }

})();