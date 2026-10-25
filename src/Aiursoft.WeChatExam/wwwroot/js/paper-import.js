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
    const comparison = el('import-question-comparison');
    const clearComparison = () => {
        if (!comparison) return;
        comparison.hidden = true;
        el('import-form').dispatchEvent(new CustomEvent('question-draft-selected', { detail: null }));
    };
    el('category').addEventListener('change', clearComparison);
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
        clearComparison();
        el('questions').replaceChildren();
        questions.forEach((q, index) => {
            if (q.questionType === 2) {
                const answer = q.standardAnswer.trim().toLowerCase();
                const positive = [tr.correct.toLowerCase(), 'true', 'correct', '正确', '对', '是', '√'];
                const negative = [tr.incorrect.toLowerCase(), 'false', 'incorrect', '错误', '错', '否', '×'];
                q.standardAnswer = positive.includes(answer) ? tr.correct : negative.includes(answer) ? tr.incorrect : '';
                q.options = [tr.correct, tr.incorrect];
            }
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
            const hasOptions = () => q.questionType === 0 || q.questionType === 2;
            const compare = comparison ? button(tr.findSimilar, () => {
                el('comparison-heading').textContent = tr.comparingQuestion.replace('{0}', index + 1);
                el('comparison-source').textContent = q.content;
                comparison.hidden = false;
                el('import-form').dispatchEvent(new CustomEvent('question-draft-selected', { detail: {
                    text: [q.content, ...(hasOptions() ? q.options : []), q.standardAnswer, q.explanation].join('\n'),
                    categoryIds: [el('category').value].filter(Boolean)
                } }));
                el('comparison-heading').focus({ preventScroll: true });
                comparison.scrollIntoView({ block: 'nearest' });
            }) : null;
            if (compare) { compare.dataset.compareQuestion = q.clientId; body.append(compare); }
            const refresh = () => {
                clearComparison();
                if (compare) compare.disabled = !q.content.trim();
                heading.textContent = `${index + 1}. (${q.originalNumber || ''}) ${q.content}`;
                note.textContent = q.standardAnswer || root.dataset.missing;
                updatePreview();
            };
            const typeLabel = document.createElement('label'); typeLabel.className = 'form-label d-block'; typeLabel.textContent = tr.type;
            const type = document.createElement('select'); type.className = 'form-select mb-2';
            types.forEach((t, i) => type.add(new Option(typeLabels[i], i)));
            type.value = q.questionType; typeLabel.append(type); edit.append(typeLabel);
            function textarea(key, label, max) {
                const l = document.createElement('label'); l.className = 'form-label d-block'; l.textContent = label;
                const input = document.createElement('textarea'); input.className = 'form-control mb-2'; input.maxLength = max; input.value = q[key];
                input.oninput = () => { q[key] = input.value; refresh(); };
                l.append(input); edit.append(l);
                return l;
            }
            textarea('content', tr.content, 2000);
            const optionsEditor = document.createElement('fieldset');
            const legend = document.createElement('legend'); legend.className = 'form-label'; legend.textContent = tr.options; optionsEditor.append(legend);
            const optionRows = document.createElement('div'); optionsEditor.append(optionRows);
            function renderOptions() {
                optionRows.replaceChildren();
                q.options.forEach((value, optionIndex) => {
                    const row = document.createElement('div'); row.className = 'input-group mb-2';
                    const radioLabel = document.createElement('label'); radioLabel.className = 'input-group-text';
                    const radio = document.createElement('input'); radio.type = 'radio'; radio.name = `answer-${q.clientId}`; radio.className = 'form-check-input';
                    radio.checked = !!value && q.standardAnswer === value;
                    radio.setAttribute('aria-label', `${tr.answer}: ${value}`);
                    radio.onchange = () => { q.standardAnswer = q.options[optionIndex]; refresh(); };
                    radioLabel.append(radio);
                    if (q.questionType === 2) {
                        radioLabel.append(document.createTextNode(` ${value}`));
                        row.append(radioLabel); optionRows.append(row);
                        return;
                    }
                    const input = document.createElement('input'); input.type = 'text'; input.className = 'form-control'; input.value = value;
                    input.placeholder = tr.optionContent; input.setAttribute('aria-label', `${tr.optionContent} ${optionIndex + 1}`);
                    input.oninput = () => {
                        const selected = radio.checked;
                        q.options[optionIndex] = input.value;
                        if (selected) q.standardAnswer = input.value;
                        radio.setAttribute('aria-label', `${tr.answer}: ${input.value}`);
                        refresh();
                    };
                    const remove = button(root.dataset.delete, () => {
                        if (q.standardAnswer === q.options[optionIndex]) q.standardAnswer = '';
                        q.options.splice(optionIndex, 1); renderOptions(); refresh();
                    });
                    row.append(radioLabel, input, remove); optionRows.append(row);
                });
            }
            const addOption = button(tr.addOption, () => { q.options.push(''); renderOptions(); refresh(); });
            optionsEditor.append(addOption);
            edit.append(optionsEditor);
            const answerEditor = textarea('standardAnswer', tr.answer, 5000);
            textarea('explanation', tr.explanation, 3000);
            function updateEditor() {
                optionsEditor.hidden = !hasOptions();
                addOption.hidden = q.questionType === 2;
                answerEditor.classList.toggle('d-block', !hasOptions());
                answerEditor.classList.toggle('d-none', hasOptions());
                answerEditor.querySelector('textarea').disabled = hasOptions();
                renderOptions(); refresh();
            }
            type.onchange = () => {
                const previousType = q.questionType;
                const previouslyHadOptions = hasOptions();
                q.questionType = Number(type.value);
                if (previouslyHadOptions !== hasOptions()) { q.options = []; q.standardAnswer = ''; }
                if (q.questionType === 2 && previousType !== 2) {
                    q.options = [tr.correct, tr.incorrect]; q.standardAnswer = '';
                }
                if (hasOptions() && !q.options.length) q.options = ['', ''];
                answerEditor.querySelector('textarea').value = q.standardAnswer;
                updateEditor();
            };
            updateEditor();
            const score = document.createElement('input'); score.type = 'number'; score.min = '1'; score.value = q.score; score.className = 'form-control'; score.oninput = () => { q.score = Number(score.value); updatePreview(); }; const scoreLabel = document.createElement('label'); scoreLabel.textContent = tr.score; scoreLabel.className = 'form-label d-block'; scoreLabel.append(score); edit.append(scoreLabel); body.append(edit); el('questions').append(card);
        });
        el('save-import').disabled = !taskId || !questions.length;
    }
    function move(index, delta) { const target = index + delta; if (target < 0 || target >= questions.length) return; [questions[index], questions[target]] = [questions[target], questions[index]]; render(); }
    new Sortable(el('questions'), { handle: '.drag-handle', animation: 150, onEnd: () => { const order = [...el('questions').children].map(c => c.dataset.id); questions.sort((a, b) => order.indexOf(a.clientId) - order.indexOf(b.clientId)); render(); } });
    el('extract').onclick = async () => {
        if (questions.length && !confirm(root.dataset.confirm)) return;
        if (!el('source').value.trim()) { el('source').reportValidity(); return; }
        clearComparison();
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
            result.categories.forEach(c => el('tag-check').append(button(c.title, () => { const option = [...el('category').options].find(o => o.value.toLowerCase() === c.id.toLowerCase()); if (option) el('category').value = option.value; clearComparison(); invalidateTags(); checkTags().catch(e => status(e.message)); })));
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
