for (const picker of document.querySelectorAll('[data-category-picker]')) {
    const search = picker.querySelector('[data-category-search]');
    const count = picker.querySelector('[data-category-count]');
    const courses = [...picker.querySelectorAll('[data-category-course]')];
    const toggles = [...picker.querySelectorAll('[data-category-toggle]')];
    let expansion;
    function expand(toggle, value) {
        toggle.setAttribute('aria-expanded', String(value));
        document.getElementById(toggle.getAttribute('aria-controls')).hidden = !value;
    }
    for (const toggle of toggles) {
        toggle.addEventListener('click', () => expand(toggle, toggle.getAttribute('aria-expanded') !== 'true'));
    }
    function updateCount() {
        count.textContent = count.dataset.format.replace('{0}', () => picker.querySelectorAll('input[type="checkbox"]:checked').length);
    }
    picker.addEventListener('change', updateCount);
    updateCount();
    search.addEventListener('keydown', event => { if (event.key === 'Enter') event.preventDefault(); });
    search.addEventListener('input', () => {
        const query = search.value.trim().toLocaleLowerCase();
        if (query && !expansion) {
            expansion = new Map([...courses.map(c => [c, c.open]), ...toggles.map(t => [t, t.getAttribute('aria-expanded') === 'true'])]);
        }
        function filter(node, ancestorMatches) {
            const matches = ancestorMatches || node.dataset.categoryTitle.toLocaleLowerCase().includes(query);
            const branch = node.querySelector(':scope > ul');
            const children = branch ? [...branch.children].map(child => filter(child, matches)) : [];
            const visible = matches || children.some(Boolean);
            node.hidden = !visible;
            const toggle = node.querySelector(':scope > .category-picker-row > [data-category-toggle]');
            if (toggle && query) expand(toggle, visible);
            return visible;
        }
        for (const course of courses) {
            const matches = course.querySelector('summary').textContent.toLocaleLowerCase().includes(query);
            const visible = [...course.querySelector(':scope > ul').children].map(node => filter(node, matches)).some(Boolean);
            course.hidden = !visible;
            if (query) course.open = visible;
        }
        picker.querySelector('[data-category-empty]').hidden = courses.some(c => !c.hidden);
        if (!query && expansion) {
            for (const [element, open] of expansion) {
                if (element.matches('details')) element.open = open;
                else expand(element, open);
            }
            expansion = null;
        }
    });
}
