// Offline outbox for emergency reports.
// A report typed during a disaster must survive a dead connection, a refresh and a crash, so it is
// written to IndexedDB BEFORE any network attempt and only removed once the server has accepted it.
// Every function resolves (never rejects) so a storage failure can never swallow a report silently.

const DB_NAME = 'rapidrelief-outbox';
const DB_VERSION = 2;
const STORE = 'reports';

let connectivityRef = null;
let recovered = false;

function openOnce() {
    return new Promise((resolve) => {
        try {
            const request = indexedDB.open(DB_NAME, DB_VERSION);
            request.onupgradeneeded = () => {
                const db = request.result;
                if (!db.objectStoreNames.contains(STORE)) {
                    db.createObjectStore(STORE, { keyPath: 'id' });
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => resolve(null);
            request.onblocked = () => resolve(null);
        } catch (_) {
            resolve(null);
        }
    });
}

function deleteDb() {
    return new Promise((resolve) => {
        try {
            const request = indexedDB.deleteDatabase(DB_NAME);
            request.onsuccess = () => resolve(true);
            request.onerror = () => resolve(false);
            request.onblocked = () => resolve(false);
        } catch (_) {
            resolve(false);
        }
    });
}

// A store left in a bad state (interrupted upgrade, quota eviction, a browser that changed its
// mind about the schema) would otherwise brick reporting for good. Rebuild it once per session
// so a citizen can at least file the report in front of them.
async function openDb() {
    const db = await openOnce();
    if (db || recovered) {
        return db;
    }

    recovered = true;
    await deleteDb();
    return openOnce();
}

/** True when this session had to rebuild the store — .NET warns the citizen that queued work was lost. */
export function storeWasRebuilt() {
    return recovered;
}

function tx(db, mode, work) {
    return new Promise((resolve) => {
        try {
            const transaction = db.transaction(STORE, mode);
            const store = transaction.objectStore(STORE);
            const request = work(store);
            transaction.oncomplete = () => resolve(request ? request.result : true);
            transaction.onerror = () => resolve(null);
            transaction.onabort = () => resolve(null);
        } catch (_) {
            resolve(null);
        }
    });
}

export function isOnline() {
    return navigator.onLine !== false;
}

/**
 * navigator.onLine only proves a link exists — a captive portal, a dead uplink or a downed server
 * all still report "online". Ask the server directly before telling a citizen their report was sent.
 * An auth challenge counts as reachable: the network is fine, the session is the problem.
 */
export async function probe(url, timeoutMs) {
    if (!isOnline()) {
        return false;
    }

    const controller = typeof AbortController === 'function' ? new AbortController() : null;
    const timer = controller ? setTimeout(() => controller.abort(), timeoutMs || 4000) : null;

    try {
        const response = await fetch(url, {
            method: 'GET',
            cache: 'no-store',
            signal: controller ? controller.signal : undefined,
        });
        return response.ok || response.status === 401 || response.status === 403;
    } catch (_) {
        return false;
    } finally {
        if (timer) {
            clearTimeout(timer);
        }
    }
}

export async function save(item) {
    const db = await openDb();
    if (!db) {
        return false;
    }

    const stored = await tx(db, 'readwrite', (store) => store.put(item));
    db.close();
    return stored !== null;
}

export async function list() {
    const db = await openDb();
    if (!db) {
        return [];
    }

    const all = await tx(db, 'readonly', (store) => store.getAll());
    db.close();
    // A row written by an older schema, or damaged on disk, must still come back so .NET can
    // quarantine it and show it to the citizen — dropping it here is exactly the silent loss
    // this queue exists to prevent. Only rows with no usable key are unrecoverable.
    return (all ?? []).filter((x) => x && typeof x.id === 'string' && x.id.length > 0);
}

export async function remove(id) {
    const db = await openDb();
    if (!db) {
        return false;
    }

    const done = await tx(db, 'readwrite', (store) => store.delete(id));
    db.close();
    return done !== null;
}

export async function clearAll() {
    const db = await openDb();
    if (!db) {
        return false;
    }

    const done = await tx(db, 'readwrite', (store) => store.clear());
    db.close();
    return done !== null;
}

/** Pushes browser online/offline transitions into .NET so the UI can state the truth. */
export function registerConnectivity(dotnetRef) {
    connectivityRef = dotnetRef;
    const notify = () => {
        if (connectivityRef) {
            connectivityRef.invokeMethodAsync('OnConnectivityChanged', isOnline());
        }
    };
    window.addEventListener('online', notify);
    window.addEventListener('offline', notify);
    return isOnline();
}

export function unregisterConnectivity() {
    connectivityRef = null;
}
