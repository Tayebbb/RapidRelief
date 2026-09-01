// RapidMap JS interop (foundation-owned — features never edit internals, plan §8.8).
// ES module imported by RapidMap.razor.cs; relies on the vendored Leaflet UMD global `L`
// loaded from lib/leaflet/leaflet.js in index.html (no CDN — PWA/offline rule).
// Extension points (polygons, heat layer) are added HERE later, never inline in features.

const instances = new Map(); // elementId -> { map, markers: Map<id, L.Marker>, dotnetRef }

export function init(elementId, dotnetRef, centerLat, centerLng, zoom) {
    if (instances.has(elementId)) {
        dispose(elementId);
    }

    const map = L.map(elementId).setView([centerLat, centerLng], zoom);

    // Map shell is offline-capable; OSM tiles need network (accepted, blueprint B7).
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    }).addTo(map);

    map.on("click", (e) => {
        dotnetRef.invokeMethodAsync("OnMapClicked", e.latlng.lat, e.latlng.lng);
    });

    instances.set(elementId, { map, markers: new Map(), dotnetRef });
}

export function setView(elementId, lat, lng, zoom) {
    const instance = instances.get(elementId);
    if (instance) {
        instance.map.setView([lat, lng], zoom);
    }
}

// Diffs by id: moves/updates existing markers, adds new ones. Never touches unlisted ids.
export function upsertMarkers(elementId, markers) {
    const instance = instances.get(elementId);
    if (!instance) {
        return;
    }

    for (const m of markers) {
        const existing = instance.markers.get(m.id);
        if (existing) {
            existing.setLatLng([m.lat, m.lng]);
            existing.setPopupContent(popupHtml(m));
        } else {
            const marker = L.marker([m.lat, m.lng], { title: m.title });
            marker.bindPopup(popupHtml(m));
            marker.addTo(instance.map);
            instance.markers.set(m.id, marker);
        }
    }
}

export function removeMarkers(elementId, ids) {
    const instance = instances.get(elementId);
    if (!instance) {
        return;
    }

    for (const id of ids) {
        const marker = instance.markers.get(id);
        if (marker) {
            marker.remove();
            instance.markers.delete(id);
        }
    }
}

export function dispose(elementId) {
    const instance = instances.get(elementId);
    if (instance) {
        instance.map.remove();
        instances.delete(elementId);
    }
}

function popupHtml(m) {
    const div = document.createElement("div");
    const strong = document.createElement("strong");
    strong.textContent = m.title;           // textContent — marker titles are data, never HTML
    div.appendChild(strong);
    if (m.kind) {
        const small = document.createElement("small");
        small.textContent = ` (${m.kind})`;
        div.appendChild(small);
    }
    return div;
}
