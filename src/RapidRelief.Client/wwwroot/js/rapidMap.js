// RapidMap JS interop (foundation-owned — features never edit internals, plan §8.8).
// ES module imported by RapidMap.razor.cs; relies on the vendored Leaflet UMD global `L`
// loaded from lib/leaflet/leaflet.js in index.html (no CDN — PWA/offline rule).
// Tile settings arrive from the server (/api/foundation/map-config) so no provider key is
// ever written into this file. Tile failures degrade to a marker-only map, never a blank page.

const instances = new Map(); // elementId -> { map, markers, dotnetRef, user, heat, tileErrors }

const DEFAULT_TILES = {
    tileUrl: "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    maxZoom: 19,
};

// One place decides what each marker kind looks like; features only choose a kind.
const KIND_STYLE = {
    sos: { color: "#e53935", glyph: "!", ring: true },
    incident: { color: "#fb8c00", glyph: "!" },
    team: { color: "#1e7a5a", glyph: "T" },
    shelter: { color: "#2cb67d", glyph: "H" },
    relief: { color: "#8e5bb5", glyph: "R" },
    pin: { color: "#1e7a5a", glyph: "+" },
    default: { color: "#6b7280", glyph: "•" },
};

function styleFor(kind) {
    return KIND_STYLE[(kind || "").toLowerCase()] || KIND_STYLE.default;
}

function iconFor(kind) {
    const style = styleFor(kind);
    return L.divIcon({
        className: "rapid-marker-wrap",
        html:
            `<span class="rapid-marker${style.ring ? " rapid-marker-ring" : ""}" ` +
            `style="--marker-color:${style.color}">${style.glyph}</span>`,
        iconSize: [22, 22],
        iconAnchor: [11, 11],
        popupAnchor: [0, -12],
    });
}

export function init(elementId, dotnetRef, centerLat, centerLng, zoom, tiles) {
    if (instances.has(elementId)) {
        dispose(elementId);
    }

    const map = L.map(elementId).setView([centerLat, centerLng], zoom);
    const settings = { ...DEFAULT_TILES, ...(tiles || {}) };

    const layer = L.tileLayer(settings.tileUrl, {
        maxZoom: settings.maxZoom,
        attribution: settings.attribution,
    });

    const instance = { map, markers: new Map(), dotnetRef, user: null, heat: null, tileErrors: 0 };

    // A tile CDN that is down, blocked or out of quota must not look like a broken app: the
    // basemap goes grey, every marker still renders, and .NET is told once so it can say so.
    layer.on("tileerror", () => {
        instance.tileErrors += 1;
        if (instance.tileErrors === 3) {
            map.getContainer().classList.add("rapid-map-tiles-down");
            try {
                dotnetRef.invokeMethodAsync("OnTilesUnavailable");
            } catch (_) {
                // The component may already be disposed — nothing to report to.
            }
        }
    });

    layer.addTo(map);

    map.on("click", (e) => {
        dotnetRef.invokeMethodAsync("OnMapClicked", e.latlng.lat, e.latlng.lng);
    });

    instances.set(elementId, instance);
}

// "You are here": a dot plus an accuracy halo, kept separate from the marker diff so a feature
// can never remove it by omitting an id. Styled from CSS tokens via the .rapid-map-user classes.
export function setUserLocation(elementId, lat, lng, accuracyMeters) {
    const instance = instances.get(elementId);
    if (!instance) {
        return;
    }

    if (!instance.user) {
        const halo = L.circle([lat, lng], {
            radius: Math.max(accuracyMeters || 0, 15),
            className: "rapid-map-user-halo",
            interactive: false,
        }).addTo(instance.map);

        const dot = L.circleMarker([lat, lng], {
            radius: 7,
            className: "rapid-map-user-dot",
        }).addTo(instance.map);
        dot.bindPopup("Your current location");

        instance.user = { halo, dot };
        return;
    }

    instance.user.halo.setLatLng([lat, lng]);
    instance.user.halo.setRadius(Math.max(accuracyMeters || 0, 15));
    instance.user.dot.setLatLng([lat, lng]);
}

export function clearUserLocation(elementId) {
    const instance = instances.get(elementId);
    if (instance?.user) {
        instance.user.halo.remove();
        instance.user.dot.remove();
        instance.user = null;
    }
}

export function setView(elementId, lat, lng, zoom) {
    const instance = instances.get(elementId);
    if (instance) {
        instance.map.setView([lat, lng], zoom);
    }
}

/** Frames every current marker; a single marker keeps the requested zoom. */
export function fitToMarkers(elementId, padding) {
    const instance = instances.get(elementId);
    if (!instance || instance.markers.size === 0) {
        return;
    }

    const points = [];
    for (const marker of instance.markers.values()) {
        points.push(marker.getLatLng());
    }

    if (points.length === 1) {
        instance.map.setView(points[0], instance.map.getZoom());
        return;
    }

    instance.map.fitBounds(L.latLngBounds(points), { padding: [padding || 32, padding || 32] });
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
            existing.setIcon(iconFor(m.kind));
            existing.setPopupContent(popupHtml(m));
        } else {
            const marker = L.marker([m.lat, m.lng], { title: m.title, icon: iconFor(m.kind) });
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

// ── Heat layer ───────────────────────────────────────────────────────────────────────────────
// Canvas overlay drawing one radial gradient per weighted point, then colourising the alpha
// channel. Dependency-free on purpose: the PWA must not fetch a plugin to show concentration.

const HeatLayer = L.Layer.extend({
    initialize(points, options) {
        this._points = points || [];
        this._radius = (options && options.radius) || 34;
        this._opacity = (options && options.opacity) || 0.7;
    },

    setPoints(points) {
        this._points = points || [];
        this._redraw();
    },

    onAdd(map) {
        this._map = map;
        this._canvas = L.DomUtil.create("canvas", "rapid-map-heat");
        const size = map.getSize();
        this._canvas.width = size.x;
        this._canvas.height = size.y;
        map.getPanes().overlayPane.appendChild(this._canvas);
        map.on("moveend zoomend resize", this._redraw, this);
        this._redraw();
    },

    onRemove(map) {
        map.off("moveend zoomend resize", this._redraw, this);
        if (this._canvas && this._canvas.parentNode) {
            this._canvas.parentNode.removeChild(this._canvas);
        }
        this._canvas = null;
    },

    _redraw() {
        if (!this._map || !this._canvas) {
            return;
        }

        const map = this._map;
        const size = map.getSize();
        this._canvas.width = size.x;
        this._canvas.height = size.y;

        const topLeft = map.containerPointToLayerPoint([0, 0]);
        L.DomUtil.setPosition(this._canvas, topLeft);

        const ctx = this._canvas.getContext("2d");
        ctx.clearRect(0, 0, size.x, size.y);
        if (this._points.length === 0) {
            return;
        }

        let maxWeight = 0;
        for (const p of this._points) {
            maxWeight = Math.max(maxWeight, p.weight || 1);
        }
        maxWeight = maxWeight || 1;

        for (const p of this._points) {
            const point = map.latLngToContainerPoint([p.lat, p.lng]);
            if (point.x < -this._radius || point.y < -this._radius
                || point.x > size.x + this._radius || point.y > size.y + this._radius) {
                continue;
            }

            const intensity = Math.min(1, (p.weight || 1) / maxWeight);
            const gradient = ctx.createRadialGradient(point.x, point.y, 0, point.x, point.y, this._radius);
            gradient.addColorStop(0, `rgba(0,0,0,${intensity})`);
            gradient.addColorStop(1, "rgba(0,0,0,0)");
            ctx.fillStyle = gradient;
            ctx.beginPath();
            ctx.arc(point.x, point.y, this._radius, 0, Math.PI * 2);
            ctx.fill();
        }

        this._colourise(ctx, size);
    },

    /** Maps accumulated alpha onto a green → amber → red ramp; low alpha stays transparent. */
    _colourise(ctx, size) {
        const image = ctx.getImageData(0, 0, size.x, size.y);
        const data = image.data;
        for (let i = 0; i < data.length; i += 4) {
            const alpha = data[i + 3];
            if (alpha === 0) {
                continue;
            }

            const t = alpha / 255;
            if (t < 0.35) {
                data[i] = 44; data[i + 1] = 182; data[i + 2] = 125;
            } else if (t < 0.65) {
                data[i] = 251; data[i + 1] = 140; data[i + 2] = 0;
            } else {
                data[i] = 229; data[i + 1] = 57; data[i + 2] = 53;
            }

            data[i + 3] = Math.min(255, alpha * this._opacity + 40);
        }

        ctx.putImageData(image, 0, 0);
    },
});

export function setHeatmap(elementId, points) {
    const instance = instances.get(elementId);
    if (!instance) {
        return;
    }

    if (!points || points.length === 0) {
        clearHeatmap(elementId);
        return;
    }

    if (instance.heat) {
        instance.heat.setPoints(points);
        return;
    }

    instance.heat = new HeatLayer(points, { radius: 34, opacity: 0.7 });
    instance.heat.addTo(instance.map);
}

export function clearHeatmap(elementId) {
    const instance = instances.get(elementId);
    if (instance?.heat) {
        instance.map.removeLayer(instance.heat);
        instance.heat = null;
    }
}

// ── Safety Zones & Road Closures ─────────────────────────────────────────────

export function setSafetyZones(elementId, zones) {
    const instance = instances.get(elementId);
    if (!instance) return;

    if (!instance.safetyZones) {
        instance.safetyZones = new Map();
    }

    clearSafetyZones(elementId);

    if (!zones || zones.length === 0) return;

    for (const z of zones) {
        if (z.isActive === false) continue;

        let color = "#e53935";
        let fillOpacity = 0.22;
        const typeLower = (z.zoneType || "").toLowerCase();
        if (typeLower.includes("safe") || typeLower.includes("assembly")) {
            color = "#1e7a5a";
            fillOpacity = 0.20;
        } else if (typeLower.includes("restricted") || typeLower.includes("caution")) {
            color = "#fb8c00";
            fillOpacity = 0.20;
        } else if (typeLower.includes("evacuation")) {
            color = "#d32f2f";
            fillOpacity = 0.28;
        }

        let layer = null;
        const isPolygon = (z.geometryType || "").toLowerCase() === "polygon";
        let coords = [];
        try {
            if (z.coordinatesJson && z.coordinatesJson !== "[]") {
                coords = typeof z.coordinatesJson === "string" ? JSON.parse(z.coordinatesJson) : z.coordinatesJson;
            }
        } catch (_) {
            coords = [];
        }

        if (isPolygon && Array.isArray(coords) && coords.length >= 3) {
            layer = L.polygon(coords, {
                color: color,
                fillColor: color,
                fillOpacity: fillOpacity,
                weight: 2,
                dashArray: "4, 4"
            });
        } else if (z.centerLat && z.centerLng) {
            layer = L.circle([z.centerLat, z.centerLng], {
                radius: Math.max(z.radiusMeters || 100, 30),
                color: color,
                fillColor: color,
                fillOpacity: fillOpacity,
                weight: 2
            });
        }

        if (layer) {
            layer.bindPopup(safetyZonePopupHtml(z));
            layer.addTo(instance.map);
            instance.safetyZones.set(z.id, layer);
        }
    }
}

export function clearSafetyZones(elementId) {
    const instance = instances.get(elementId);
    if (!instance || !instance.safetyZones) return;

    for (const layer of instance.safetyZones.values()) {
        layer.remove();
    }
    instance.safetyZones.clear();
}

export function setRoadClosures(elementId, closures) {
    const instance = instances.get(elementId);
    if (!instance) return;

    if (!instance.roadClosures) {
        instance.roadClosures = new Map();
    }

    clearRoadClosures(elementId);

    if (!closures || closures.length === 0) return;

    for (const c of closures) {
        if (c.isActive === false) continue;

        let coords = [];
        try {
            if (c.coordinatesJson && c.coordinatesJson !== "[]") {
                coords = typeof c.coordinatesJson === "string" ? JSON.parse(c.coordinatesJson) : c.coordinatesJson;
            }
        } catch (_) {
            coords = [];
        }

        const layers = [];
        if (Array.isArray(coords) && coords.length >= 2) {
            // Striped barricade line
            const line = L.polyline(coords, {
                color: "#e53935",
                weight: 6,
                opacity: 0.9,
                dashArray: "10, 8"
            });
            line.bindPopup(roadClosurePopupHtml(c));
            line.addTo(instance.map);
            layers.push(line);

            // Warning icon marker at midpoint
            const mid = coords[Math.floor(coords.length / 2)];
            if (Array.isArray(mid) && mid.length >= 2) {
                const marker = L.marker([mid[0], mid[1]], {
                    icon: L.divIcon({
                        className: "rapid-marker-wrap",
                        html: '<span class="rapid-marker rapid-marker-ring" style="--marker-color:#e53935">✕</span>',
                        iconSize: [22, 22],
                        iconAnchor: [11, 11]
                    })
                });
                marker.bindPopup(roadClosurePopupHtml(c));
                marker.addTo(instance.map);
                layers.push(marker);
            }
        }

        if (layers.length > 0) {
            instance.roadClosures.set(c.id, layers);
        }
    }
}

export function clearRoadClosures(elementId) {
    const instance = instances.get(elementId);
    if (!instance || !instance.roadClosures) return;

    for (const layers of instance.roadClosures.values()) {
        for (const l of layers) {
            l.remove();
        }
    }
    instance.roadClosures.clear();
}

function safetyZonePopupHtml(z) {
    const wrapper = document.createElement("div");
    wrapper.className = "rapid-map-popup";

    const title = document.createElement("strong");
    title.textContent = z.name;
    wrapper.appendChild(title);

    const badge = document.createElement("div");
    badge.style.marginTop = "4px";
    badge.style.fontSize = "12px";
    badge.style.fontWeight = "600";
    badge.textContent = `${z.zoneType || "Zone"} · ${z.severity || "Alert"}`;
    wrapper.appendChild(badge);

    if (z.description) {
        const desc = document.createElement("p");
        desc.style.margin = "6px 0 0 0";
        desc.style.fontSize = "12px";
        desc.textContent = z.description;
        wrapper.appendChild(desc);
    }

    if (z.radiusMeters > 0) {
        const rad = document.createElement("small");
        rad.style.display = "block";
        rad.style.marginTop = "4px";
        rad.style.color = "#6b7280";
        rad.textContent = `Perimeter radius: ${z.radiusMeters}m`;
        wrapper.appendChild(rad);
    }

    return wrapper;
}

function roadClosurePopupHtml(c) {
    const wrapper = document.createElement("div");
    wrapper.className = "rapid-map-popup";

    const title = document.createElement("strong");
    title.textContent = c.roadName;
    wrapper.appendChild(title);

    const badge = document.createElement("div");
    badge.style.marginTop = "4px";
    badge.style.fontSize = "12px";
    badge.style.color = "#e53935";
    badge.style.fontWeight = "bold";
    badge.textContent = `ROAD CLOSED · ${c.severity || "Blocked"}`;
    wrapper.appendChild(badge);

    if (c.blockedReason) {
        const reason = document.createElement("p");
        reason.style.margin = "6px 0 0 0";
        reason.style.fontSize = "12px";
        reason.textContent = `Reason: ${c.blockedReason}`;
        wrapper.appendChild(reason);
    }

    if (c.alternateRouteAdvice) {
        const alt = document.createElement("p");
        alt.style.margin = "4px 0 0 0";
        alt.style.fontSize = "12px";
        alt.style.color = "#1e7a5a";
        alt.textContent = `Detour: ${c.alternateRouteAdvice}`;
        wrapper.appendChild(alt);
    }

    return wrapper;
}

function popupHtml(m) {
    const wrapper = document.createElement("div");
    wrapper.className = "rapid-map-popup";

    const title = document.createElement("strong");
    title.textContent = m.title;
    wrapper.appendChild(title);

    if (m.kind) {
        const small = document.createElement("small");
        small.textContent = ` (${m.kind})`;
        wrapper.appendChild(small);
    }

    return wrapper;
}

export function dispose(elementId) {
    const instance = instances.get(elementId);
    if (!instance) {
        return;
    }

    clearHeatmap(elementId);
    clearSafetyZones(elementId);
    clearRoadClosures(elementId);
    instance.map.remove();
    instances.delete(elementId);
}
