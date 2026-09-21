// Theme boot + toggle. Loaded synchronously in <head> so the correct theme is set
// before first paint (no flash). CSP forbids inline scripts, hence this file.
// localStorage here stores ONLY the UI theme preference — never tokens or user data.
(function () {
    'use strict';

    var STORAGE_KEY = 'rr-theme';
    var root = document.documentElement;
    var media = window.matchMedia('(prefers-color-scheme: dark)');

    function stored() {
        try {
            var value = localStorage.getItem(STORAGE_KEY);
            return value === 'light' || value === 'dark' ? value : null;
        } catch (_) {
            return null;
        }
    }

    function systemTheme() {
        return media.matches ? 'dark' : 'light';
    }

    function apply(theme) {
        root.setAttribute('data-theme', theme);
        var meta = document.querySelector('meta[name="theme-color"]');
        if (meta) {
            meta.setAttribute('content', theme === 'dark' ? '#111513' : '#faf7f2');
        }
    }

    apply(stored() || systemTheme());

    // Follow OS changes only while the user has not chosen explicitly.
    media.addEventListener('change', function () {
        if (!stored()) {
            apply(systemTheme());
        }
    });

    window.rrTheme = {
        get: function () {
            return root.getAttribute('data-theme') || 'light';
        },
        set: function (theme) {
            if (theme !== 'light' && theme !== 'dark') {
                return this.get();
            }
            try { localStorage.setItem(STORAGE_KEY, theme); } catch (_) { /* private mode */ }
            apply(theme);
            return theme;
        },
        toggle: function () {
            return this.set(this.get() === 'dark' ? 'light' : 'dark');
        }
    };
})();
