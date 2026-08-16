(function () {
    'use strict';

    function setSelectedPill(root, selectedPill) {
        root.querySelectorAll('.sfi-category-pill').forEach(function (pill) {
            var isSelected = pill === selectedPill;
            pill.classList.toggle('is-selected', isSelected);
            pill.setAttribute('aria-pressed', isSelected ? 'true' : 'false');
        });
    }

    document.addEventListener('click', function (event) {
        var pill = event.target.closest('.sfi-category-pill');
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
})();
