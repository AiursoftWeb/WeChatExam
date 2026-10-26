const config = document.getElementById('ai-task-config').dataset;
const taskId = config.taskId;
const isCategorization = config.isCategorization === 'true';
const questionsContainer = document.getElementById('questionsContainer');
const taskStatusBadge = document.getElementById('taskStatus');
const adoptAllBtn = document.getElementById('adoptAllBtn');
const cancelAllBtn = document.getElementById('cancelAllBtn');
let pollingInterval;
let isWaiting = false;
let isTaskFinished = false;

function getLocalizedText(key, defaultText) {
    const el = document.querySelector(`#loc-data span[data-key="${key}"]`);
    return el ? el.innerText : defaultText;
}

async function pollStatus() {
    if (isWaiting) return;
    
    try {
        const response = await fetch(`${config.getStatusUrl}?taskId=${taskId}`);
        if (!response.ok) {
            if (response.status === 429) {
                isWaiting = true;
                taskStatusBadge.textContent = getLocalizedText('too-many-requests', 'Too many requests, waiting 30s...');
                taskStatusBadge.className = 'badge bg-warning text-dark';
                setTimeout(() => {
                    isWaiting = false;
                    taskStatusBadge.textContent = getLocalizedText('processing', 'Processing...');
                    taskStatusBadge.className = 'badge bg-primary';
                }, 30000);
                return;
            }
            clearInterval(pollingInterval);
            taskStatusBadge.textContent = getLocalizedText('error', 'Error');
            taskStatusBadge.className = 'badge bg-danger';
            return;
        }
        const data = await response.json();
        
        data.items.forEach(item => {
            const card = document.querySelector(`.question-card[data-question-id="${item.questionId}"]`);
            if (!card) return;

            const container = card.querySelector('.new-value-container');
            const footer = card.querySelector('.action-buttons');
            
            if (isCategorization && item.newEntityId) {
                 const hiddenInput = card.querySelector('.suggested-category-id');
                 if(hiddenInput) hiddenInput.value = item.newEntityId;
            }

            if (item.status === 2) { // Completed
                container.textContent = item.newValue;
                footer.classList.remove('d-none');
            } else if (item.status === 3) { // Failed
                container.textContent = item.error || getLocalizedText('error', 'Error');
                footer.classList.add('d-none'); 
            }
        });

        // Show adopt all button if there are completed items
        const completedItemsCount = data.items.filter(i => i.status === 2).length;
        if (completedItemsCount > 0) {
            adoptAllBtn.classList.remove('d-none');
        }

        if (data.isCompleted) {
            clearInterval(pollingInterval);
            isTaskFinished = true;
            taskStatusBadge.textContent = getLocalizedText('completed', 'Completed');
            taskStatusBadge.className = 'badge bg-success';
            cancelAllBtn.classList.add('d-none');
        }
    } catch (error) {
        console.error('Polling error:', error);
    }
}

async function adopt(questionId) {
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    const response = await fetch(`${config.adoptUrl}?taskId=${taskId}&questionId=${questionId}`, {
        method: 'POST',
        headers: { 'RequestVerificationToken': token }
    });
    if (response.ok) {
        dismissCard(questionId);
    } else {
        alert(await response.text());
    }
}

async function adoptAll() {
    let message = getLocalizedText('confirm-adopt-all', 'Are you sure you want to adopt all AI suggestions?');
    if (!isTaskFinished) {
        message = getLocalizedText('confirm-adopt-partial', 'Tasks are still processing. Do you want to adopt completed ones and abandon the rest?');
    }

    if (!confirm(message)) return;
    
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    adoptAllBtn.disabled = true;
    adoptAllBtn.innerHTML = `<span class="spinner-border spinner-border-sm me-1"></span> ${getLocalizedText('processing', 'Processing...')}`;
    
    const response = await fetch(`${config.adoptAllUrl}?taskId=${taskId}`, {
        method: 'POST',
        headers: { 'RequestVerificationToken': token }
    });
    
    if (response.ok) {
        window.location.href = config.returnUrl;
    } else {
        alert(await response.text() || getLocalizedText('failed-adopt', 'Failed to adopt suggestions.'));
        adoptAllBtn.disabled = false;
        adoptAllBtn.innerHTML = `<i class="align-middle me-1" data-lucide="check-check"></i> ${getLocalizedText('adopt-all-text', 'Adopt All')}`;
    }
}

async function abandon(questionId) {
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    const response = await fetch(`${config.abandonUrl}?taskId=${taskId}&questionId=${questionId}`, {
        method: 'POST',
        headers: { 'RequestVerificationToken': token }
    });
    if (response.ok) {
        dismissCard(questionId);
    }
}

function showEdit(questionId) {
    const card = document.querySelector(`.question-card[data-question-id="${questionId}"]`);
    document.getElementById('editQuestionId').value = questionId;

    if (isCategorization) {
         const hiddenInput = card.querySelector('.suggested-category-id');
         const suggestedId = hiddenInput ? hiddenInput.value : null;
         const select = document.getElementById('editSelect');
         select.value = suggestedId || '';
    } else {
        const currentContent = card.querySelector('.new-value-container').textContent.trim();
        document.getElementById('editTextarea').value = currentContent;
    }
    new bootstrap.Modal(document.getElementById('editModal')).show();
}

async function saveEdit() {
    const questionId = document.getElementById('editQuestionId').value;
    let newContent;
    
    if (isCategorization) {
        const select = document.getElementById('editSelect');
        newContent = select.value;
    } else {
        newContent = document.getElementById('editTextarea').value;
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

    const response = await fetch(`${config.editUrl}?taskId=${taskId}&questionId=${questionId}`, {
        method: 'POST',
        headers: { 
            'Content-Type': 'application/json',
            'RequestVerificationToken': token 
        },
        body: JSON.stringify(newContent)
    });

    if (response.ok) {
        bootstrap.Modal.getInstance(document.getElementById('editModal')).hide();
        dismissCard(questionId);
    } else {
        alert(await response.text());
    }
}

function dismissCard(questionId) {
    const card = document.querySelector(`.question-card[data-question-id="${questionId}"]`);
    if (card) {
        card.style.transition = 'all 0.5s ease';
        card.style.opacity = '0';
        card.style.transform = 'translateX(100px)';
        setTimeout(() => {
            card.remove();
            checkAllDismissed();
        }, 500);
    }
}

function checkAllDismissed() {
    if (questionsContainer.children.length === 0) {
        window.location.href = config.returnUrl;
    }
}

async function cancelAll() {
    if (!confirm(getLocalizedText('confirm-cancel-all', 'Are you sure you want to cancel all pending tasks?'))) return;
    
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    const response = await fetch(`${config.cancelAllUrl}?taskId=${taskId}`, {
        method: 'POST',
        headers: { 'RequestVerificationToken': token }
    });
    
    if (response.ok) {
        document.getElementById('cancelAllBtn').disabled = true;
        pollStatus();
    }
}

async function keepAlive() {
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    try {
        await fetch(`${config.keepAliveUrl}?taskId=${taskId}`, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token }
        });
    } catch (e) {
        console.error("Keep alive failed", e);
    }
}

pollingInterval = setInterval(pollStatus, 2000);
setInterval(keepAlive, 20000);
pollStatus();
