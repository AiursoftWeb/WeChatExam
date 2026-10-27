import Sortable from '/node_modules/sortablejs/modular/sortable.esm.js';

        const list = document.getElementById('knowledge-points-sortable');
        const saveButton = document.getElementById('save-knowledge-point-order');

        if (list && saveButton) {
            Sortable.create(list, {
                animation: 150,
                handle: '.knowledge-point-drag-handle',
                ghostClass: 'knowledge-point-sortable-ghost',
                onEnd: () => saveButton.hidden = false
            });

            saveButton.addEventListener('click', async () => {
                saveButton.disabled = true;
                const knowledgePointIds = Array.from(list.querySelectorAll('tr[data-knowledge-point-id]'))
                    .map(row => row.dataset.knowledgePointId);

                try {
                    const response = await fetch(list.dataset.updateUrl, {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? ''
                        },
                        body: JSON.stringify(knowledgePointIds)
                    });

                    if (!response.ok) {
                        throw new Error(`Failed to save order: ${response.status}`);
                    }

                    window.location.reload();
                } catch (error) {
                    console.error(error);
                    alert(document.getElementById("knowledge-order-error").textContent);
                    saveButton.disabled = false;
                }
            });
        }
