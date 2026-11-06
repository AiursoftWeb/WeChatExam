// Use the same device time zone as the shared data-utc-time renderer.
const couponFilters = document.querySelector('[data-coupon-filters]');
if (couponFilters) {
    const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    const zoneInput = couponFilters.querySelector('[name="timeZone"]');
    if (zoneInput && timeZone) {
        zoneInput.value = timeZone;
        const url = new URL(window.location.href);
        // A bookmarked date filter may have been created on a different device.
        if ((url.searchParams.get('from') || url.searchParams.get('to')) && url.searchParams.get('timeZone') !== timeZone) {
            url.searchParams.set('timeZone', timeZone);
            window.location.replace(url.href);
        }
    }
}
