(function () {
    'use strict';

    function cloneForDrawer(source, target) {
        if (!source || !target || target.children.length) {
            return;
        }

        var clone = source.cloneNode(true);
        var nodesWithIds = clone.querySelectorAll('[id]');

        for (var i = 0; i < nodesWithIds.length; i++) {
            nodesWithIds[i].removeAttribute('id');
        }

        target.appendChild(clone);
    }

    function init() {
        if (document.body.getAttribute('data-jb-shell-init') === 'true') {
            return;
        }
        document.body.setAttribute('data-jb-shell-init', 'true');

        var menuToggle = document.querySelector('.jb-menu-toggle');
        var menuClose = document.querySelector('.jb-drawer-close');
        var menuDrawer = document.querySelector('#jb-mobile-menu-drawer');
        var searchToggle = document.querySelector('.jb-search-toggle');
        var searchClose = document.querySelector('.jb-search-close');
        var searchOverlay = document.querySelector('#jb-mobile-search-overlay');
        var searchInput = document.querySelector('.jb-search-overlay .search-box-text');
        var drawerOverlay = document.querySelector('.jb-drawer-overlay');

        var headerLinksWrapper = document.querySelector('.header-links-wrapper .header-links ul');
        var drawerAccountLinks = document.querySelector('.jb-drawer-account-links');
        var selectorWrapper = document.querySelector('.header-selectors-wrapper');
        var drawerSelectors = document.querySelector('.jb-drawer-selectors');

        if (headerLinksWrapper && drawerAccountLinks && !drawerAccountLinks.children.length) {
            var clonedLinks = headerLinksWrapper.cloneNode(true);
            var itemsToKeep = [];
            var listItems = clonedLinks.querySelectorAll('li');

            for (var j = 0; j < listItems.length; j++) {
                var li = listItems[j];

                if (li.hasAttribute('id')) {
                    li.removeAttribute('id');
                }

                var link = li.querySelector('a');
                if (!link) {
                    continue;
                }

                var isExcluded = link.classList.contains('ico-cart') ||
                    link.classList.contains('ico-wishlist') ||
                    link.classList.contains('ico-inbox');

                if (!isExcluded) {
                    itemsToKeep.push(li);
                }
            }

            clonedLinks.innerHTML = '';
            for (var k = 0; k < itemsToKeep.length; k++) {
                clonedLinks.appendChild(itemsToKeep[k]);
            }

            drawerAccountLinks.appendChild(clonedLinks);
        }

        cloneForDrawer(selectorWrapper, drawerSelectors);

        function syncExpandedState() {
            var menuOpen = document.body.classList.contains('jb-menu-open');
            var searchOpen = document.body.classList.contains('jb-search-open');

            if (menuToggle) {
                menuToggle.setAttribute('aria-expanded', menuOpen ? 'true' : 'false');
            }

            if (searchToggle) {
                searchToggle.setAttribute('aria-expanded', searchOpen ? 'true' : 'false');
            }

            if (menuDrawer) {
                menuDrawer.setAttribute('aria-hidden', menuOpen ? 'false' : 'true');
            }

            if (searchOverlay) {
                searchOverlay.setAttribute('aria-hidden', searchOpen ? 'false' : 'true');
            }
        }

        function closeMenu(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            document.body.classList.remove('jb-menu-open');
            syncExpandedState();
        }

        function closeSearch(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            document.body.classList.remove('jb-search-open');
            syncExpandedState();
        }

        function openMenu(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            if (document.body.classList.contains('jb-menu-open')) {
                return;
            }

            closeSearch();
            document.body.classList.add('jb-menu-open');
            syncExpandedState();
        }

        function openSearch(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            if (document.body.classList.contains('jb-search-open')) {
                return;
            }

            closeMenu();
            document.body.classList.add('jb-search-open');
            syncExpandedState();

            if (searchInput) {
                setTimeout(function () {
                    searchInput.focus();
                }, 50);
            }
        }

        function attachClick(element, handler) {
            if (!element) {
                return;
            }

            element.addEventListener('click', function (e) {
                handler(e);
            });
        }

        attachClick(menuToggle, openMenu);
        attachClick(menuClose, closeMenu);
        attachClick(searchToggle, openSearch);
        attachClick(searchClose, closeSearch);

        if (drawerOverlay) {
            drawerOverlay.addEventListener('click', function () {
                closeMenu();
                closeSearch();
            });
        }

        if (searchOverlay) {
            searchOverlay.addEventListener('click', function (e) {
                if (e.target === searchOverlay) {
                    closeSearch(e);
                }
            });
        }

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                closeMenu();
                closeSearch();
            }
        });

        window.addEventListener('resize', function () {
            if (window.innerWidth > 1000) {
                closeMenu();
                closeSearch();
            }
        });

        syncExpandedState();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
