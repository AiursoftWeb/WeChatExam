document.addEventListener('DOMContentLoaded', () => {
    const type = document.getElementById('vipTypeSelect');
    if (!type) return;

    const update = () => {
        for (const [sectionId, fieldId, selectedType] of [
            ['categorySection', 'TotalCategoryId', type.dataset.courseType],
            ['institutionSection', 'InstitutionId', type.dataset.institutionType]
        ]) {
            const section = document.getElementById(sectionId);
            const field = document.getElementById(fieldId);
            if (!section || !field) continue;
            const selected = type.value === selectedType;
            section.hidden = !selected;
            field.required = selected;
            if (!selected) field.value = '';
        }
    };
    type.addEventListener('change', update);
    update();
});
