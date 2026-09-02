// Geolocation interop for the AI emergency assistant (F16).
// OPT-IN ONLY: Assistant.razor imports and calls this exclusively after the user clicks
// "Use my location" — nothing here runs on page load. Never throws and never rejects:
// a denial, an error, a dismissed prompt or a timeout all resolve to null, and the page
// simply sends no coordinates (which means no shelter context, never a broken chat).

export function tryGetPosition(timeoutMs) {
    return new Promise((resolve) => {
        if (!navigator.geolocation) {
            resolve(null);
            return;
        }

        let settled = false;
        const finish = (value) => {
            if (!settled) {
                settled = true;
                resolve(value);
            }
        };

        // Some browsers never invoke either callback when the permission prompt is dismissed.
        const guard = setTimeout(() => finish(null), timeoutMs + 500);

        try {
            navigator.geolocation.getCurrentPosition(
                (position) => {
                    clearTimeout(guard);
                    finish({ lat: position.coords.latitude, lng: position.coords.longitude });
                },
                () => {
                    clearTimeout(guard);
                    finish(null);
                },
                { enableHighAccuracy: false, timeout: timeoutMs, maximumAge: 300000 });
        } catch {
            clearTimeout(guard);
            finish(null);
        }
    });
}
