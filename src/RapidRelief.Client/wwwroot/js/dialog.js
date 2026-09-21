// Modal behaviour that cannot be expressed in Blazor markup: focus containment,
// initial focus, background scroll lock and focus restoration.
// Loaded as a classic script (the CSP forbids inline scripts) and exposed on window.
(function () {
    'use strict';

    var FOCUSABLE = [
        'a[href]',
        'button:not([disabled])',
        'input:not([disabled]):not([type="hidden"])',
        'select:not([disabled])',
        'textarea:not([disabled])',
        '[tabindex]:not([tabindex="-1"])'
    ].join(',');

    // A stack, not a single slot: two dialogs can be open at once (a confirm on
    // top of a detail panel), and a shared singleton leaked the scroll lock
    // permanently the second time one closed.
    var stack = [];
    var scroll = { locked: false, overflow: '', paddingRight: '', top: 0 };
    var keydownBound = false;

    function visibleFocusable(container) {
        return Array.prototype.filter.call(container.querySelectorAll(FOCUSABLE), function (el) {
            if (el.getAttribute('aria-hidden') === 'true') return false;
            var rect = el.getBoundingClientRect();
            return rect.width > 0 && rect.height > 0;
        });
    }

    function lockScroll() {
        if (scroll.locked) return;
        scroll.locked = true;
        scroll.top = window.scrollY || document.documentElement.scrollTop || 0;
        scroll.overflow = document.body.style.overflow;
        scroll.paddingRight = document.body.style.paddingRight;

        var scrollbar = window.innerWidth - document.documentElement.clientWidth;
        // position:fixed rather than overflow:hidden alone — iOS Safari ignores
        // overflow:hidden on body and scrolls the page behind the dialog anyway.
        document.body.style.overflow = 'hidden';
        document.body.style.position = 'fixed';
        document.body.style.width = '100%';
        document.body.style.top = '-' + scroll.top + 'px';
        if (scrollbar > 0) {
            document.body.style.paddingRight = scrollbar + 'px';
        }
    }

    function unlockScroll() {
        if (!scroll.locked) return;
        scroll.locked = false;
        document.body.style.overflow = scroll.overflow;
        document.body.style.paddingRight = scroll.paddingRight;
        document.body.style.position = '';
        document.body.style.width = '';
        document.body.style.top = '';
        window.scrollTo(0, scroll.top);
    }

    // Bound on document with capture, not on the container: if focus ever escapes
    // (Blazor removed the focused node, the user came back from the URL bar), a
    // container-scoped listener never sees the Tab and focus walks the page behind.
    function onKeyDown(e) {
        if (e.key !== 'Tab' || stack.length === 0) return;
        var container = stack[stack.length - 1].container;
        if (!document.contains(container)) return;

        var items = visibleFocusable(container);
        if (items.length === 0) {
            e.preventDefault();
            container.focus();
            return;
        }

        var first = items[0];
        var last = items[items.length - 1];
        var active = document.activeElement;
        var inside = container.contains(active);

        if (e.shiftKey) {
            if (!inside || active === first || active === container) {
                e.preventDefault();
                last.focus();
            }
        } else if (!inside || active === last) {
            e.preventDefault();
            first.focus();
        }
    }

    function ensureKeydownBound() {
        if (keydownBound) return;
        document.addEventListener('keydown', onKeyDown, true);
        keydownBound = true;
    }

    function focusInitial(container) {
        // Prefer an explicitly marked element, then the first real control, then
        // the container itself so screen readers announce the dialog.
        var preferred = container.querySelector('[data-autofocus]');
        var items = visibleFocusable(container);
        var target = preferred || items[0] || container;
        try { target.focus({ preventScroll: true }); } catch (err) { target.focus(); }
    }

    window.rrDialog = {
        // selector points at the dialog container rendered by RrModal.
        open: function (selector) {
            var container = document.querySelector(selector);
            if (!container) return false;

            // Re-entrant open for the same dialog (a re-render) must not stack.
            for (var i = 0; i < stack.length; i++) {
                if (stack[i].selector === selector) {
                    return true;
                }
            }

            stack.push({
                selector: selector,
                container: container,
                previouslyFocused: document.activeElement
            });

            ensureKeydownBound();
            lockScroll();
            focusInitial(container);
            return true;
        },

        // Takes the selector so a dialog releases its OWN entry, whatever order
        // the components happen to dispose in.
        close: function (selector) {
            var index = -1;
            for (var i = stack.length - 1; i >= 0; i--) {
                if (stack[i].selector === selector) { index = i; break; }
            }
            if (index === -1) return false;

            var entry = stack.splice(index, 1)[0];

            if (stack.length === 0) {
                unlockScroll();
            }

            var restore = entry.previouslyFocused;
            if (restore && typeof restore.focus === 'function' && document.contains(restore)) {
                try { restore.focus({ preventScroll: true }); } catch (err) { restore.focus(); }
            } else if (stack.length > 0) {
                focusInitial(stack[stack.length - 1].container);
            }
            return true;
        },

        // Moves focus into an element that is not an RrModal (the armed SOS
        // prompt), without taking a scroll lock or a focus trap.
        focus: function (selector) {
            var el = document.querySelector(selector);
            if (!el) return false;
            try { el.focus({ preventScroll: true }); } catch (err) { el.focus(); }
            return true;
        }
    };
})();
