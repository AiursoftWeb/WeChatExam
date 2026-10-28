(() => {
    const text = (key, fallback) => document.querySelector(`#loc-data [data-key="${key}"]`)?.textContent ?? fallback;
    const result = document.getElementById('testPaperResult');
    const storageKey = `wechat-exam:paper-test:${result.dataset.paperKind}:${result.dataset.paperId}`;
    localStorage.removeItem(storageKey);
    localStorage.removeItem(`${storageKey}:started-at`);
    localStorage.removeItem(`${storageKey}:flags`);

    const button = document.getElementById('toggleAll');
    let expanded = false;
    button.addEventListener('click', () => {
        expanded = !expanded;
        document.querySelectorAll('#resultQuestions .collapse').forEach(element => {
            bootstrap.Collapse.getOrCreateInstance(element, { toggle: false })[expanded ? 'show' : 'hide']();
        });
        button.textContent = expanded ? text('collapse', 'Collapse all') : text('expand', 'Expand all');
    });
})();
