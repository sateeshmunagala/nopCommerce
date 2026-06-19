(function () {
    'use strict';

    function init() {
        if (document.body.getAttribute('data-jb-shell-init') === 'true') {
            return;
        }
        document.body.setAttribute('data-jb-shell-init', 'true');

        // --- CLONE ACCOUNT LINKS FOR MOBILE DRAWER ---
        var headerLinksWrapper = document.querySelector('.header-links-wrapper .header-links ul');
        var drawerAccountLinks = document.querySelector('.jb-drawer-account-links');
        if (headerLinksWrapper && drawerAccountLinks && !drawerAccountLinks.children.length) {
            var clonedLinks = headerLinksWrapper.cloneNode(true);
            var itemsToKeep = [];

            var listItems = clonedLinks.querySelectorAll('li');
            for (var i = 0; i < listItems.length; i++) {
                var li = listItems[i];
                // Remove IDs to prevent duplicate ID issues
                if (li.hasAttribute('id')) {
                    li.removeAttribute('id');
                }
                var link = li.querySelector('a');
                if (link) {
                    var isExcluded = link.classList.contains('ico-cart') ||
                                     link.classList.contains('ico-wishlist') ||
                                     link.classList.contains('ico-inbox');
                    if (!isExcluded) {
                        itemsToKeep.push(li);
                    }
                }
            }

            // Clear the clone and re-append only the kept items
            clonedLinks.innerHTML = '';
            for (var j = 0; j < itemsToKeep.length; j++) {
                clonedLinks.appendChild(itemsToKeep[j]);
            }

            drawerAccountLinks.appendChild(clonedLinks);
        }

        // --- MOBILE DRAWER (MENU) ---
        var menuToggle = document.querySelector('.jb-menu-toggle');
        var menuClose = document.querySelector('.jb-drawer-close');
        var drawerOverlay = document.querySelector('.jb-drawer-overlay');
        var menuDrawer = document.querySelector('#jb-mobile-menu-drawer');

        function syncExpandedState() {
            var menuOpen = document.body.classList.contains('jb-menu-open');
            var searchOpen = document.body.classList.contains('jb-search-open');

            if (menuToggle) menuToggle.setAttribute('aria-expanded', menuOpen ? 'true' : 'false');
            if (searchToggle) searchToggle.setAttribute('aria-expanded', searchOpen ? 'true' : 'false');
            if (menuDrawer) menuDrawer.setAttribute('aria-hidden', menuOpen ? 'false' : 'true');
            if (searchOverlay) searchOverlay.setAttribute('aria-hidden', searchOpen ? 'false' : 'true');
        }

        function openMenu(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }
            if (document.body.classList.contains('jb-menu-open')) return;
            closeSearch();
            document.body.classList.add('jb-menu-open');
            syncExpandedState();
        }

        function closeMenu(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }
            document.body.classList.remove('jb-menu-open');
            syncExpandedState();
        }

        function attachClick(element, handler) {
            if (!element) return;
            element.addEventListener('click', function(e) {
                handler(e);
            });
        }

        attachClick(menuToggle, openMenu);
        attachClick(menuClose, closeMenu);

        // --- MOBILE SEARCH OVERLAY ---
        var searchToggle = document.querySelector('.jb-search-toggle');
        var searchClose = document.querySelector('.jb-search-close');
        var searchOverlay = document.querySelector('#jb-mobile-search-overlay');
        var searchInput = document.querySelector('.jb-search-overlay .search-box-text');

        function openSearch(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }
            if (document.body.classList.contains('jb-search-open')) return;
            closeMenu();
            document.body.classList.add('jb-search-open');
            syncExpandedState();
            if (searchInput) {
                setTimeout(function() { searchInput.focus(); }, 50);
            }
        }

        function closeSearch(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }
            document.body.classList.remove('jb-search-open');
            syncExpandedState();
        }

        attachClick(searchToggle, openSearch);
        attachClick(searchClose, closeSearch);

        if (drawerOverlay) {
            drawerOverlay.addEventListener('click', function() {
                closeMenu();
                closeSearch();
            });
        }

        if (searchOverlay) {
            searchOverlay.addEventListener('click', function(e) {
                if (e.target === searchOverlay) {
                    closeSearch(e);
                }
            });
        }

        // --- KEYBOARD & RESIZE LISTENERS ---
        document.addEventListener('keydown', function(e) {
            if (e.key === 'Escape') {
                closeMenu();
                closeSearch();
            }
        });

        window.addEventListener('resize', function() {
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
