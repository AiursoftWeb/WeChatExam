import Sortable from '/node_modules/sortablejs/modular/sortable.esm.js';
(() => {
    'use strict';
    const root = document.getElementById('paper-import');
    if (!root) return;
    const el = id => document.getElementById(id);
    const tr = el('import-translations').dataset;
    root.dataset.actions = tr.actions;
    const typeLabels = [tr.choice, tr.blank, tr.bool, tr.shortAnswer, tr.essay, tr.nounExplanation];
    let questions = [], taskId = null, generation = 0, customTag = false, titleEdited = false, confirmations = [], tagSequence = 0;
    const types = ['Choice', 'Blank', 'Bool', 'ShortAnswer', 'Essay', 'NounExplanation'];
    const token = root.querySelector('[name="__RequestVerificationToken"]').value;
    const status = text => { el('import-status').textContent = text; };
    const extras = () => el('extra-tags').value.split('\n').map(s => s.trim()).filter(Boolean);
    const tagRequest = () => ({ sourceTagName: el('source-tag').value, extraTags: extras(), categoryId: el('category').value || null, taxonomyId: el('taxonomy').value ? Number(el('taxonomy').value) : null });
    async function request(path, body) {
        const response = await fetch(path, body === undefined ? {} : { method: 'POST', headers: { 'Content-Type': 'application/json', RequestVerificationToken: token }, body: JSON.stringify(body) });
        if (!response.ok) throw new Error(await response.text());
        return response.json();
    }
    function syncTag() { if (!customTag) el('source-tag').value = el('title').value; el('tag-mode').textContent = customTag ? root.dataset.custom : root.dataset.follow; }
    function invalidateTags() { tagSequence++; confirmations = []; el('tag-check').replaceChildren(); }
    el('title').addEventListener('input', () => { titleEdited = true; syncTag(); scheduleTagCheck(); });
    el('source-tag').addEventListener('input', () => { customTag = true; syncTag(); scheduleTagCheck(); });
    el('follow-title').onclick = () => { customTag = false; syncTag(); invalidateTags(); };
    let checkTimer; function scheduleTagCheck() { invalidateTags(); clearTimeout(checkTimer); if (el('source-tag').value.trim()) checkTimer = setTimeout(() => checkTags().catch(e => status(e.message)), 400); }
    ['category', 'taxonomy'].forEach(id => el(id).addEventListener('change', scheduleTagCheck));
    el('extra-tags').addEventListener('input', invalidateTags);
    function button(text, action) { const b = document.createElement('button'); b.type = 'button'; b.className = 'btn btn-sm btn-outline-secondary me-1'; b.textContent = text; b.onclick = action; return b; }
    function render() {
        el('questions').replaceChildren();
        questions.forEach((q, index) => {
            const card = document.createElement('div'); card.className = 'card'; card.dataset.id = q.clientId;
            const body = document.createElement('div'); body.className = 'card-body'; card.append(body);
            const handle = document.createElement('span'); handle.className = 'drag-handle me-2'; handle.textContent = '↕'; handle.style.cursor = 'grab'; body.append(handle);
            const heading = document.createElement('strong'); heading.textContent = `${index + 1}. (${q.originalNumber || ''}) ${q.content}`; body.append(heading);
            const note = document.createElement('p'); note.className = 'text-muted'; note.textContent = q.standardAnswer || root.dataset.missing; body.append(note);
            const optionList = document.createElement('ol'); optionList.className = 'mb-2';
            const explanation = document.createElement('p'); explanation.className = 'text-muted';
            const info = document.createElement('p'); info.className = 'small';
            function updatePreview() {
                optionList.replaceChildren(); q.options.forEach(o => { const li = document.createElement('li'); li.textContent = o; optionList.append(li); });
                explanation.textContent = `${tr.explanation}: ${q.explanation || root.dataset.missing}`;
                info.textContent = `${typeLabels[q.questionType]} · ${tr.score}: ${q.score}`;
            }
            updatePreview(); body.append(info, optionList, explanation);
            const edit = document.createElement('div'); edit.hidden = true;
            const actions = document.createElement('details'); const summary = document.createElement('summary'); summary.textContent = root.dataset.actions; actions.append(summary, handle, button(root.dataset.edit, () => { edit.hidden = !edit.hidden; }), button(root.dataset.delete, () => { questions = questions.filter(item => item !== q); render(); }), button(root.dataset.up, () => move(index, -1)), button(root.dataset.down, () => move(index, 1))); body.append(actions);
            const type = document.createElement('select'); type.className = 'form-select mb-2';
            types.forEach((t, i) => { const opt = new Option(typeLabels[i], i); type.add(opt); }); type.value = q.questionType; type.onchange = () => { q.questionType = Number(type.value); updatePreview(); }; edit.append(type);
            [['content', tr.content, 2000], ['options', tr.options, 5000], ['standardAnswer', tr.answer, 5000], ['explanation', tr.explanation, 3000]].forEach(([key, label, max]) => {
                const l = document.createElement('label'); l.className = 'form-label d-block'; l.textContent = label;
                const input = document.createElement('textarea'); input.className = 'form-control mb-2'; input.maxLength = max; input.value = key === 'options' ? q.options.join('\n') : q[key];
                input.oninput = () => { q[key] = key === 'options' ? input.value.split('\n').filter(s => s.trim()) : input.value; heading.textContent = `${index + 1}. (${q.originalNumber || ''}) ${q.content}`; note.textContent = q.standardAnswer || root.dataset.missing; updatePreview(); };
                l.append(input); edit.append(l);
            });
            const score = document.createElement('input'); score.type = 'number'; score.min = '1'; score.value = q.score; score.className = 'form-control'; score.oninput = () => { q.score = Number(score.value); updatePreview(); }; const scoreLabel = document.createElement('label'); scoreLabel.textContent = tr.score; scoreLabel.className = 'form-label d-block'; scoreLabel.append(score); edit.append(scoreLabel); body.append(edit); el('questions').append(card);
        });
        el('save-import').disabled = !taskId || !questions.length;
    }
    function move(index, delta) { const target = index + delta; if (target < 0 || target >= questions.length) return; [questions[index], questions[target]] = [questions[target], questions[index]]; render(); }
    new Sortable(el('questions'), { handle: '.drag-handle', animation: 150, onEnd: () => { const order = [...el('questions').children].map(c => c.dataset.id); questions.sort((a, b) => order.indexOf(a.clientId) - order.indexOf(b.clientId)); render(); } });
    el('extract').onclick = async () => {
        if (questions.length && !confirm(root.dataset.confirm)) return;
        if (!el('source').value.trim()) { el('source').reportValidity(); return; }
        const current = ++generation; el('extract').disabled = true; el('save-import').disabled = true;
        try {
            const started = await request('/Papers/ImportExtract', { source: el('source').value });
            const deadline = Date.now() + 6 * 60 * 1000;
            while (current === generation) {
                if (Date.now() > deadline) throw new Error(tr.timeout);
                const result = await request('/Papers/ImportStatus?id=' + encodeURIComponent(started.taskId));
                if (current !== generation) return;
                status(tr[result.status.toLowerCase()] || result.status);
                if (result.status === 'Failed') throw new Error(result.error);
                if (result.status === 'Completed') {
                    taskId = started.taskId;
                    questions = result.preview.questions.map(q => ({ ...q, questionType: typeof q.questionType === 'string' ? types.indexOf(q.questionType) : q.questionType, clientId: crypto.randomUUID() }));
                    if (!titleEdited) el('title').value = result.preview.title;
                    syncTag(); invalidateTags(); render(); break;
                }
                await new Promise(resolve => setTimeout(resolve, 1500));
            }
        } catch (e) { status(e.message); el('save-import').disabled = !taskId || !questions.length; }
        finally { if (current === generation) el('extract').disabled = false; }
    };
    async function checkTags() {
        const sequence = ++tagSequence; const payload = tagRequest(); const snapshot = JSON.stringify(payload);
        const result = await request('/Papers/ImportCheckTags', payload);
        if (sequence !== tagSequence || snapshot !== JSON.stringify(tagRequest())) return;
        confirmations = []; el('tag-check').replaceChildren();
        const warning = document.createElement('p'); warning.textContent = `${tr.sourceReuse} ${tr.extraReuse}`; el('tag-check').append(warning);
        for (const conflict of result.conflicts) {
            const label = document.createElement('label'); label.className = 'd-block mb-2'; const checkbox = document.createElement('input'); checkbox.type = 'checkbox';
            checkbox.onchange = () => { confirmations = confirmations.filter(c => c.id !== conflict.id); if (checkbox.checked) confirmations.push({ id: conflict.id, isFree: conflict.isFree, taxonomyId: conflict.taxonomyId }); };
            label.append(checkbox, document.createTextNode(` ${root.dataset.reuse}: ${conflict.displayName} (${conflict.isFree ? root.dataset.free : root.dataset.paid}; ${tr.taxonomy} ${conflict.taxonomyId ?? tr.none}). ${conflict.isSource ? tr.sourceReuse : tr.extraReuse}`)); el('tag-check').append(label);
        }
        if (result.mismatch) {
            const text = document.createElement('p'); text.textContent = tr.mismatch; el('tag-check').append(text);
            result.categories.forEach(c => el('tag-check').append(button(c.title, () => { const option = [...el('category').options].find(o => o.value.toLowerCase() === c.id.toLowerCase()); if (option) el('category').value = option.value; invalidateTags(); checkTags().catch(e => status(e.message)); })));
            if (!result.categories.length) { const p = document.createElement('p'); p.textContent = tr.noCategory; el('tag-check').append(p); }
        }
    }
    el('check-tags').onclick = () => checkTags().catch(e => status(e.message));
    el('import-form').onsubmit = async event => {
        event.preventDefault(); if (!taskId || !questions.length || !el('import-form').reportValidity()) return;
        if (window.jQuery?.fn.valid && !window.jQuery(el('import-form')).valid()) return;
        const unanswered = questions.findIndex(q => !q.standardAnswer?.trim());
        if (unanswered >= 0) { status(tr.missingAnswer.replace('{0}', unanswered + 1)); return; }
        el('save-import').disabled = true;
        try {
            const result = await request('/Papers/ImportSave', { ...tagRequest(), taskId, title: el('title').value, questions: questions.map(({ clientId, ...q }) => q), confirmations, timeLimit: Number(el('time-limit').value), isRealExam: el('real-exam').checked, isFree: el('is-free').checked });
            window.location.assign(result.url);
        } catch (e) { status(e.message); el('save-import').disabled = false; }
    };
    syncTag();
})();
