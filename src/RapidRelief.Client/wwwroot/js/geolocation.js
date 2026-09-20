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

        const tryPosition = (useHighAcc, isFallback) => {
            let settled = false;
            const finish = (value) => {
                if (!settled) {
                    settled = true;
                    resolve(value);
                }
            };

            const guard = setTimeout(() => {
                if (!settled) {
                    settled = true;
                    if (useHighAcc && !isFallback) {
                        // Fallback to low accuracy on timeout
                        tryPosition(false, true);
                    } else {
                        resolve({ ok: false, reason: REASON.timeout });
                    }
                }
            }, (isFallback ? Math.min(timeoutMs, 5000) : timeoutMs) + 500);

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
                        if (useHighAcc && !isFallback && error?.code !== 1) {
                            // High accuracy failed (e.g. position unavailable), try low accuracy fallback
                            tryPosition(false, true);
                        } else {
                            finish({ ok: false, reason: mapError(error) });
                        }
                    },
                    {
                        enableHighAccuracy: useHighAcc === true,
                        timeout: isFallback ? Math.min(timeoutMs, 5000) : timeoutMs,
                        maximumAge: 60000,
                    });
            } catch {
                clearTimeout(guard);
                finish({ ok: false, reason: REASON.unavailable });
            }
        };

        tryPosition(highAccuracy, false);
    });
}

function mapError(error) {
    switch (error?.code) {
        case 1: return REASON.denied;       // PERMISSION_DENIED
        case 3: return REASON.timeout;      // TIMEOUT
        default: return REASON.unavailable; // POSITION_UNAVAILABLE / unknown
    }
}
