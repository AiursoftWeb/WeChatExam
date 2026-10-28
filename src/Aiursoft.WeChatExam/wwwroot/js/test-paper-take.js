(() => {
    const text = (key, fallback) => document.querySelector(`#loc-data [data-key="${key}"]`)?.textContent ?? fallback;
    const form = document.getElementById('testPaperForm');
    const paperId = form.dataset.paperId;
    const timeLimit = Number(form.dataset.timeLimit);
    const storageKey = `wechat-exam:paper-test:${form.dataset.paperKind}:${paperId}`;
    const timerKey = `${storageKey}:started-at`;
    const flagsKey = `${storageKey}:flags`;
    const answerInputs = [...form.querySelectorAll('[data-answer]')];
    const cards = [...document.querySelectorAll('[data-question-id]')];
    const saveState = document.getElementById('saveState');
    let submitting = false;

    const readJson = (key, fallback) => {
        try { return JSON.parse(localStorage.getItem(key)) ?? fallback; }
        catch { return fallback; }
    };
    const answers = readJson(storageKey, {});
    const flags = new Set(readJson(flagsKey, []));

    answerInputs.forEach(input => {
        const saved = answers[input.dataset.answer];
        if (input.type === 'radio') input.checked = saved === input.value;
        else if (typeof saved === 'string') input.value = saved;
    });

    const getAnswer = id => {
        const inputs = answerInputs.filter(input => input.dataset.answer === id);
        const radio = inputs.find(input => input.type === 'radio');
        return radio ? (inputs.find(input => input.checked)?.value ?? '') : (inputs[0]?.value ?? '').trim();
    };

    const updateState = () => {
        const current = {};
        let answered = 0;
        cards.forEach(card => {
            const id = card.dataset.questionId;
            const answer = getAnswer(id);
            if (answer) {
                current[id] = answer;
                answered++;
            }
            document.querySelectorAll(`[data-nav-question="${id}"]`).forEach(link => {
                link.classList.toggle('answered', Boolean(answer));
                link.classList.toggle('flagged', flags.has(id));
            });
            card.classList.toggle('is-flagged', flags.has(id));
        });
        localStorage.setItem(storageKey, JSON.stringify(current));
        document.getElementById('answeredCount').textContent = answered;
        document.getElementById('mobileAnsweredCount').textContent = answered;
        document.getElementById('modalAnsweredCount').textContent = answered;
        if (saveState) saveState.textContent = text('saved', 'Saved in this browser');
        window.lucide?.createIcons();
    };

    answerInputs.forEach(input => input.addEventListener(input.type === 'radio' ? 'change' : 'input', () => {
        if (saveState) saveState.textContent = text('saving', 'Saving...');
        updateState();
    }));

    document.querySelectorAll('[data-flag-question]').forEach(button => button.addEventListener('click', () => {
        const id = button.dataset.flagQuestion;
        flags.has(id) ? flags.delete(id) : flags.add(id);
        localStorage.setItem(flagsKey, JSON.stringify([...flags]));
        updateState();
    }));

    const totalMs = Math.max(0, timeLimit) * 60 * 1000;
    let startedAt = Number(localStorage.getItem(timerKey));
    if (!startedAt || Number.isNaN(startedAt)) {
        startedAt = Date.now();
        localStorage.setItem(timerKey, String(startedAt));
    }

    const submit = () => {
        if (submitting) return;
        submitting = true;
        const button = document.getElementById('confirmSubmitButton');
        button.disabled = true;
        button.textContent = text('grading', 'Grading...');
        form.requestSubmit();
    };

    const updateTimer = () => {
        if (totalMs <= 0) {
            document.getElementById('countdown').textContent = text('no-limit', 'No time limit');
            return;
        }
        const remaining = Math.max(0, totalMs - (Date.now() - startedAt));
        const totalSeconds = Math.floor(remaining / 1000);
        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.floor((totalSeconds % 3600) / 60);
        const seconds = totalSeconds % 60;
        document.getElementById('countdown').textContent = `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
        const ratio = Math.max(0, remaining / totalMs * 100);
        document.getElementById('timeProgress').style.width = `${ratio}%`;
        document.getElementById('timeProgress').classList.toggle('bg-danger', ratio < 15);
        if (remaining <= 0) submit();
    };

    const observer = new IntersectionObserver(entries => {
        entries.filter(entry => entry.isIntersecting).forEach(entry => {
            document.querySelectorAll('.test-exam-number.active').forEach(link => link.classList.remove('active'));
            document.querySelectorAll(`[data-nav-question="${entry.target.dataset.questionId}"]`).forEach(link => link.classList.add('active'));
        });
    }, { rootMargin: '-25% 0px -60% 0px' });
    cards.forEach(card => observer.observe(card));

    document.getElementById('confirmSubmitButton').addEventListener('click', submit);
    form.addEventListener('submit', () => { submitting = true; });
    updateState();
    updateTimer();
    setInterval(updateTimer, 1000);
})();
