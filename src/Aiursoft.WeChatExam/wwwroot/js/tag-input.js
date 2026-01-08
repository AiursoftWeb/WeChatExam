// Tag Input Component with Autocomplete
// Usage: Initialize with TagInput.init(inputElement, hiddenInputElement, initialTags)

const TagInput = (function() {
    'use strict';

    function init(inputElement, hiddenInputElement, initialTags = []) {
        const container = document.createElement('div');
        container.className = 'tag-input-container';
        
        const tagsDisplay = document.createElement('div');
        tagsDisplay.className = 'tags-display mb-2';
        
        const autocompleteWrapper = document.createElement('div');
        autocompleteWrapper.className = 'position-relative';
        autocompleteWrapper.style.display = 'inline-block';
        autocompleteWrapper.style.width = '100%';
        
        const autocompleteList = document.createElement('div');
        autocompleteList.className = 'autocomplete-list list-group position-absolute w-100';
        autocompleteList.style.display = 'none';
        autocompleteList.style.zIndex = '1000';
        autocompleteList.style.maxHeight = '200px';
        autocompleteList.style.overflowY = 'auto';
        
        // Insert elements
        inputElement.parentNode.insertBefore(container, inputElement);
        container.appendChild(tagsDisplay);
        container.appendChild(autocompleteWrapper);
        autocompleteWrapper.appendChild(inputElement);
        autocompleteWrapper.appendChild(autocompleteList);
        
        let selectedTags = initialTags.map(tag => tag.trim()).filter(tag => tag);
        let autocompleteItems = [];
        let selectedIndex = -1;
        let debounceTimer;
        
        function updateHiddenInput() {
            hiddenInputElement.value = selectedTags.join(' ');
        }
        
        function renderTags() {
            tagsDisplay.innerHTML = '';
            selectedTags.forEach((tag, index) => {
                const pill = document.createElement('span');
                pill.className = 'badge bg-primary me-2 mb-2';
                pill.style.fontSize = '0.9rem';
                pill.style.cursor = 'default';
                pill.innerHTML = `${escapeHtml(tag)} <button type="button" class="btn-close btn-close-white ms-1" style="font-size: 0.7rem;" data-index="${index}"></button>`;
                tagsDisplay.appendChild(pill);
            });
            
            // Attach remove handlers
            tagsDisplay.querySelectorAll('.btn-close').forEach(btn => {
                btn.addEventListener('click', function() {
                    const index = parseInt(this.getAttribute('data-index'));
                    selectedTags.splice(index, 1);
                    renderTags();
                    updateHiddenInput();
                });
            });
        }
        
        function escapeHtml(text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }
        
        function addTag(tag) {
            tag = tag.trim();
            if (tag && !selectedTags.includes(tag)) {
                selectedTags.push(tag);
                renderTags();
                updateHiddenInput();
                inputElement.value = '';
                hideAutocomplete();
            }
        }
        
        function showAutocomplete(items) {
            autocompleteItems = items;
            selectedIndex = -1;
            autocompleteList.innerHTML = '';
            
            if (items.length === 0) {
                hideAutocomplete();
                return;
            }
            
            items.forEach((item, index) => {
                const listItem = document.createElement('a');
                listItem.href = '#';
                listItem.className = 'list-group-item list-group-item-action';
                listItem.textContent = item.displayName;
                listItem.dataset.index = index;
                listItem.addEventListener('click', function(e) {
                    e.preventDefault();
                    addTag(item.displayName);
                });
                autocompleteList.appendChild(listItem);
            });
            
            autocompleteList.style.display = 'block';
        }
        
        function hideAutocomplete() {
            autocompleteList.style.display = 'none';
            selectedIndex = -1;
        }
        
        function updateAutocompleteSelection() {
            const items = autocompleteList.querySelectorAll('.list-group-item');
            items.forEach((item, index) => {
                if (index === selectedIndex) {
                    item.classList.add('active');
                } else {
                    item.classList.remove('active');
                }
            });
        }
        
        inputElement.addEventListener('input', function() {
            const query = this.value.trim();
            
            clearTimeout(debounceTimer);
            
            if (query.length < 1) {
                hideAutocomplete();
                return;
            }
            
            debounceTimer = setTimeout(() => {
                fetch(`/Tags/Autocomplete?query=${encodeURIComponent(query)}`)
                    .then(response => response.json())
                    .then(data => {
                        // Filter out already selected tags
                        const filtered = data.filter(item => !selectedTags.includes(item.displayName));
                        showAutocomplete(filtered);
                    })
                    .catch(error => console.error('Autocomplete error:', error));
            }, 300);
        });
        
        inputElement.addEventListener('keydown', function(e) {
            // Check for delimiter keys: space, comma, semicolon
            if (e.key === ' ' || e.key === ',' || e.key === ';') {
                e.preventDefault();
                const value = this.value.trim();
                if (value) {
                    addTag(value);
                }
                return;
            }
            
            if (e.key === 'Enter') {
                e.preventDefault();
                
                if (selectedIndex >= 0 && autocompleteItems[selectedIndex]) {
                    addTag(autocompleteItems[selectedIndex].displayName);
                } else if (this.value.trim()) {
                    addTag(this.value.trim());
                }
            } else if (e.key === 'Escape') {
                hideAutocomplete();
            } else if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (autocompleteItems.length > 0) {
                    selectedIndex = Math.min(selectedIndex + 1, autocompleteItems.length - 1);
                    updateAutocompleteSelection();
                }
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                if (autocompleteItems.length > 0) {
                    selectedIndex = Math.max(selectedIndex - 1, -1);
                    updateAutocompleteSelection();
                }
            } else if (e.key === 'Backspace' && this.value === '' && selectedTags.length > 0) {
                selectedTags.pop();
                renderTags();
                updateHiddenInput();
            }
        });
        
        // Click outside to close
        document.addEventListener('click', function(e) {
            if (!autocompleteWrapper.contains(e.target)) {
                hideAutocomplete();
            }
        });
        
        // Initialize
        renderTags();
        updateHiddenInput();
    }
    
    return {
        init: init
    };
})();
