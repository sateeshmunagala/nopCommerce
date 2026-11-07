(function () {
    'use strict';

    function setSelectedPill(root, selectedPill) {
        root.querySelectorAll('.sfi-category-tab').forEach(function (pill) {
            var isSelected = pill === selectedPill;
            pill.classList.toggle('is-selected', isSelected);
            pill.setAttribute('aria-pressed', isSelected ? 'true' : 'false');
        });

        var viewMore = root.querySelector('[data-sfi-view-more]');
        var viewMoreUrl = selectedPill.getAttribute('data-sfi-view-more-url');
        if (viewMore && viewMoreUrl)
            viewMore.setAttribute('href', viewMoreUrl);
    }

    function updateCarousel(carousel) {
        var viewport = carousel.querySelector('.sfi-products-viewport');
        if (!viewport)
            return;

        var maxScrollLeft = Math.max(0, viewport.scrollWidth - viewport.clientWidth);
        var currentScrollLeft = Math.max(0, viewport.scrollLeft);
        var previous = carousel.querySelector('[data-sfi-carousel-scroll="-1"]');
        var next = carousel.querySelector('[data-sfi-carousel-scroll="1"]');

        carousel.classList.toggle('is-scrollable', maxScrollLeft > 1);

        if (previous)
            previous.disabled = maxScrollLeft <= 1 || currentScrollLeft <= 1;

        if (next)
            next.disabled = maxScrollLeft <= 1 || currentScrollLeft >= maxScrollLeft - 1;
    }

    function initializeCarousels(scope) {
        var carousels = [];

        if (scope && scope.matches && scope.matches('[data-sfi-products-carousel]'))
            carousels.push(scope);

        if (scope && scope.querySelectorAll) {
            scope.querySelectorAll('[data-sfi-products-carousel]').forEach(function (carousel) {
                carousels.push(carousel);
            });
        }

        carousels.forEach(function (carousel) {
            var viewport = carousel.querySelector('.sfi-products-viewport');
            if (!viewport)
                return;

            if (viewport.getAttribute('data-sfi-scroll-bound') !== 'true') {
                viewport.setAttribute('data-sfi-scroll-bound', 'true');
                viewport.addEventListener('scroll', function () {
                    window.requestAnimationFrame(function () {
                        updateCarousel(carousel);
                    });
                }, { passive: true });
            }

            window.requestAnimationFrame(function () {
                updateCarousel(carousel);
            });
        });
    }

    function updateCategoryTabsScrollability() {
        document.querySelectorAll('.sfi-category-tabs').forEach(function (tabs) {
            var wrapper = tabs.closest('.sfi-category-tabs-wrap');
            if (wrapper)
                wrapper.classList.toggle('is-scrollable', tabs.scrollWidth > tabs.clientWidth + 1);
        });
    }

    function getColumnScrollAmount(carousel) {
        var cell = carousel.querySelector('.sfi-grid-cell');
        var track = carousel.querySelector('.sfi-products-grid');
        if (!cell || !track)
            return 0;

        var styles = window.getComputedStyle(track);
        var gap = parseFloat(styles.columnGap || styles.gap) || 0;
        return cell.getBoundingClientRect().width + gap;
    }

    document.addEventListener('click', function (event) {
        var scrollButton = event.target.closest('[data-sfi-carousel-scroll]');
        if (scrollButton) {
            var carousel = scrollButton.closest('[data-sfi-products-carousel]');
            var viewport = carousel && carousel.querySelector('.sfi-products-viewport');
            if (!carousel || !viewport)
                return;

            var direction = Number(scrollButton.getAttribute('data-sfi-carousel-scroll')) || 1;
            var amount = getColumnScrollAmount(carousel);
            var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

            if (amount > 0)
                viewport.scrollBy({ left: direction * amount, behavior: reduceMotion ? 'auto' : 'smooth' });

            return;
        }

        var pill = event.target.closest('.sfi-category-tab');
        if (!pill)
            return;

        var root = pill.closest('[data-sfi-inline-filter]');
        if (root)
            setSelectedPill(root, pill);
    });

    document.addEventListener('htmx:beforeRequest', function (event) {
        var root = event.target.closest && event.target.closest('[data-sfi-inline-filter]');
        var results = root && root.querySelector('.sfi-results');
        if (results)
            results.setAttribute('aria-busy', 'true');
    });

    document.addEventListener('htmx:afterRequest', function (event) {
        var root = event.target.closest && event.target.closest('[data-sfi-inline-filter]');
        var results = root && root.querySelector('.sfi-results');
        if (results)
            results.setAttribute('aria-busy', 'false');
    });

    document.addEventListener('htmx:afterSwap', function (event) {
        initializeCarousels(event.target);
        updateCategoryTabsScrollability();
    });

    var resizeFrame;
    window.addEventListener('resize', function () {
        window.cancelAnimationFrame(resizeFrame);
        resizeFrame = window.requestAnimationFrame(function () {
            initializeCarousels(document);
            updateCategoryTabsScrollability();
        });
    }, { passive: true });

    function initialize() {
        initializeCarousels(document);
        updateCategoryTabsScrollability();
    }

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', initialize);
    else
        initialize();
})();
