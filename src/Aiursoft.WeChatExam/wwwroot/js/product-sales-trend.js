(() => {
    'use strict';
    document.querySelectorAll('[data-daily-trend]').forEach(root => {
        const points = Array.from(root.querySelectorAll('.sales-trend-series > span'), point => point.dataset);
        const slider = root.querySelector('[data-trend-day]');
        const plot = root.querySelector('[data-trend-plot]');
        const cursor = root.querySelector('[data-trend-cursor]');
        if (!slider || !plot || !cursor || points.length < 2) return;
        const date = root.querySelector('[data-trend-date]');
        const receipts = root.querySelector('[data-trend-receipts]');
        const orders = root.querySelector('[data-trend-orders]');
        const select = index => {
            const point = points[index];
            if (!point) return;
            slider.value = String(index);
            slider.setAttribute('aria-valuetext', point.valueText);
            date.textContent = point.date;
            receipts.textContent = point.receipts;
            orders.textContent = point.orders;
            const x = String(1000 * index / (points.length - 1));
            cursor.setAttribute('x1', x);
            cursor.setAttribute('x2', x);
        };
        slider.addEventListener('input', () => select(Number(slider.value)));
        const pointAt = event => {
            const bounds = plot.getBoundingClientRect();
            const fraction = Math.max(0, Math.min(1, (event.clientX - bounds.left) / bounds.width));
            select(Math.round(fraction * (points.length - 1)));
        };
        plot.addEventListener('pointerdown', pointAt);
        plot.addEventListener('pointermove', event => {
            if (event.pointerType === 'mouse' || event.buttons === 1) pointAt(event);
        });
        select(points.length - 1);
    });
})();
