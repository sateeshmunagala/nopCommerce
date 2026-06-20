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
        var accountToggle = document.querySelector('.jb-account-toggle');
        var accountPopup = document.querySelector('#jb-mobile-account-popup');
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

        if (drawerSelectors) {
            var selectorInputs = drawerSelectors.querySelectorAll('select, input, button, a');
            var selectorText = drawerSelectors.textContent ? drawerSelectors.textContent.replace(/\s+/g, '') : '';

            if (!selectorInputs.length && !selectorText.length) {
                drawerSelectors.innerHTML = '';
            }
        }

        function syncExpandedState() {
            var menuOpen = document.body.classList.contains('jb-menu-open');
            var searchOpen = document.body.classList.contains('jb-search-open');
            var accountOpen = document.body.classList.contains('jb-account-open');

            if (menuToggle) {
                menuToggle.setAttribute('aria-expanded', menuOpen ? 'true' : 'false');
            }

            if (searchToggle) {
                searchToggle.setAttribute('aria-expanded', searchOpen ? 'true' : 'false');
            }

            if (accountToggle) {
                accountToggle.setAttribute('aria-expanded', accountOpen ? 'true' : 'false');
            }

            if (menuDrawer) {
                menuDrawer.setAttribute('aria-hidden', menuOpen ? 'false' : 'true');
            }

            if (searchOverlay) {
                searchOverlay.setAttribute('aria-hidden', searchOpen ? 'false' : 'true');
            }

            if (accountPopup) {
                accountPopup.setAttribute('aria-hidden', accountOpen ? 'false' : 'true');
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

        function closeAccount(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            document.body.classList.remove('jb-account-open');
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
            closeAccount();
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
            closeAccount();
            document.body.classList.add('jb-search-open');
            syncExpandedState();

            if (searchInput) {
                setTimeout(function () {
                    searchInput.focus();
                }, 50);
            }
        }

        function toggleAccount(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            if (document.body.classList.contains('jb-account-open')) {
                closeAccount();
                return;
            }

            closeMenu();
            closeSearch();
            document.body.classList.add('jb-account-open');
            syncExpandedState();
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
        attachClick(accountToggle, toggleAccount);

        if (drawerOverlay) {
            drawerOverlay.addEventListener('click', function () {
                closeMenu();
                closeSearch();
                closeAccount();
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
                closeAccount();
            }
        });

        document.addEventListener('click', function (e) {
            if (!document.body.classList.contains('jb-account-open')) {
                return;
            }

            if (accountToggle && accountToggle.contains(e.target)) {
                return;
            }

            if (accountPopup && accountPopup.contains(e.target)) {
                return;
            }

            closeAccount();
        });

        window.addEventListener('resize', function () {
            if (window.innerWidth > 1000) {
                closeMenu();
                closeSearch();
                closeAccount();
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
