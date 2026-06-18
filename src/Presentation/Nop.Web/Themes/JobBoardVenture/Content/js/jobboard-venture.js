(function () {
    'use strict';

    function init() {
        // --- CLONE ACCOUNT LINKS FOR MOBILE DRAWER ---
        var headerLinksWrapper = document.querySelector('.header-links-wrapper .header-links ul');
        var drawerAccountLinks = document.querySelector('.jb-drawer-account-links');
        if (headerLinksWrapper && drawerAccountLinks) {
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

        function openMenu() {
            document.body.classList.add('jb-menu-open');
            if (menuToggle) menuToggle.setAttribute('aria-expanded', 'true');
        }

        function closeMenu() {
            document.body.classList.remove('jb-menu-open');
            if (menuToggle) menuToggle.setAttribute('aria-expanded', 'false');
        }

        if (menuToggle) {
            menuToggle.addEventListener('click', openMenu);
        }
        if (menuClose) {
            menuClose.addEventListener('click', closeMenu);
        }

        // --- MOBILE SEARCH OVERLAY ---
        var searchToggle = document.querySelector('.jb-search-toggle');
        var searchClose = document.querySelector('.jb-search-close');
        var searchInput = document.querySelector('.jb-search-overlay .search-box-text');

        function openSearch() {
            document.body.classList.add('jb-search-open');
            if (searchToggle) searchToggle.setAttribute('aria-expanded', 'true');
            if (searchInput) {
                // small delay to allow display:flex to apply before focusing
                setTimeout(function() { searchInput.focus(); }, 50);
            }
        }

        function closeSearch() {
            document.body.classList.remove('jb-search-open');
            if (searchToggle) searchToggle.setAttribute('aria-expanded', 'false');
        }

        if (searchToggle) {
            searchToggle.addEventListener('click', openSearch);
        }
        if (searchClose) {
            searchClose.addEventListener('click', closeSearch);
        }

        if (drawerOverlay) {
            drawerOverlay.addEventListener('click', function() {
                closeMenu();
                closeSearch();
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

        // --- MOBILE FOOTER ACCORDION OVERRIDE ---
        // Nopcommerce has a default footer script, but we want to ensure our jb-footer-open class handles the visual state if needed.
        var footerTitles = document.querySelectorAll('.footer-block .title');
        if (footerTitles.length > 0) {
            footerTitles.forEach(function(title) {
                // Ensure we don't interfere with desktop clicks by checking window width
                title.addEventListener('click', function(e) {
                    if (window.innerWidth <= 1000) {
                        var block = this.closest('.footer-block');
                        if (block) {
                            block.classList.toggle('jb-footer-open');
                        }
                    }
                });
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
