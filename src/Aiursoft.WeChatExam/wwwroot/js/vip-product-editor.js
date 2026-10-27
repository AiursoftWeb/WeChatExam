document.addEventListener('DOMContentLoaded', () => {
    const type = document.getElementById('vipTypeSelect');
    const section = document.getElementById('categorySection');
    const category = document.getElementById('TotalCategoryId');
    if (!type || !section || !category) return;

    const update = () => {
        const isCourse = type.value === type.dataset.courseType;
        section.hidden = !isCourse;
        category.required = isCourse;
        if (!isCourse) category.value = '';
    };
    type.addEventListener('change', update);
    update();
});
