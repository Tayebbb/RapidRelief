// RapidRelief — Landing Page Scroll Reveal & Navigation Enhancements
// Respects prefers-reduced-motion and CSP strict rules (no inline execution, no innerHTML).

(function () {
    let observer = null;

    function initLandingInteractions() {
        const reveals = document.querySelectorAll('.rr-reveal-item:not(.is-revealed)');
        
        // Check reduced motion preference
        const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        if (prefersReducedMotion) {
            reveals.forEach(function (el) {
                el.classList.add('is-revealed');
            });
        } else if ('IntersectionObserver' in window) {
            if (observer) {
                observer.disconnect();
            }

            observer = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    if (entry.isIntersecting) {
                        entry.target.classList.add('is-revealed');
                        observer.unobserve(entry.target);
                    }
                });
            }, {
                root: null,
                rootMargin: '0px 0px -40px 0px',
                threshold: 0.12
            });

            reveals.forEach(function (el) {
                observer.observe(el);
            });
        } else {
            // Fallback for environments without IntersectionObserver
            reveals.forEach(function (el) {
                el.classList.add('is-revealed');
            });
        }

        // Header scroll detection
        const navbar = document.querySelector('.landing-nav');
        if (navbar) {
            function updateNavScroll() {
                if (window.scrollY > 24) {
                    navbar.classList.add('is-scrolled');
                } else {
                    navbar.classList.remove('is-scrolled');
                }
            }
            window.removeEventListener('scroll', updateNavScroll);
            window.addEventListener('scroll', updateNavScroll, { passive: true });
            updateNavScroll();
        }
    }

    // Expose init helper for Blazor component invocations or DOM readiness
    window.rapidReliefLanding = {
        init: initLandingInteractions
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initLandingInteractions);
    } else {
        initLandingInteractions();
    }
})();
