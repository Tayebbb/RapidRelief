// Foundation-owned geolocation interop (shared by every feature that needs "where am I").
// The browser only prompts when a user action calls this — nothing here runs on page load.
// Never throws and never rejects: every failure resolves to { ok:false, reason }, so a denial
// or a dead GPS degrades the page instead of breaking it.
// Coordinates are handed to C# only; this module never sends them anywhere.

const REASON = {
    unsupported: "unsupported",
    denied: "denied",
    unavailable: "unavailable",
    timeout: "timeout",
};

export function getCurrentPosition(timeoutMs, highAccuracy) {
    return new Promise((resolve) => {
        if (!navigator.geolocation) {
            resolve({ ok: false, reason: REASON.unsupported });
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
        const guard = setTimeout(() => finish({ ok: false, reason: REASON.timeout }), timeoutMs + 500);

        try {
            navigator.geolocation.getCurrentPosition(
                (position) => {
                    clearTimeout(guard);
                    finish({
                        ok: true,
                        lat: position.coords.latitude,
                        lng: position.coords.longitude,
                        accuracyMeters: position.coords.accuracy ?? 0,
                    });
                },
                (error) => {
                    clearTimeout(guard);
                    finish({ ok: false, reason: mapError(error) });
                },
                {
                    enableHighAccuracy: highAccuracy === true,
                    timeout: timeoutMs,
                    maximumAge: 60000,
                });
        } catch {
            clearTimeout(guard);
            finish({ ok: false, reason: REASON.unavailable });
        }
    });
}

function mapError(error) {
    switch (error?.code) {
        case 1: return REASON.denied;       // PERMISSION_DENIED
        case 3: return REASON.timeout;      // TIMEOUT
        default: return REASON.unavailable; // POSITION_UNAVAILABLE / unknown
    }
}
