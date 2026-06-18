(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {

        // --- MOBILE DRAWER (MENU) ---
        var menuToggle = document.querySelector('.jb-menu-toggle');
        var menuClose = document.querySelector('.jb-drawer-close');
        var drawerOverlay = document.querySelector('.jb-drawer-overlay');

        function openMenu() {
            document.body.classList.add('jb-menu-open');
        }

        function closeMenu() {
            document.body.classList.remove('jb-menu-open');
        }

        if (menuToggle) {
            menuToggle.addEventListener('click', openMenu);
        }
        if (menuClose) {
            menuClose.addEventListener('click', closeMenu);
        }
        if (drawerOverlay) {
            drawerOverlay.addEventListener('click', closeMenu);
        }

        // --- MOBILE SEARCH OVERLAY ---
        var searchToggle = document.querySelector('.jb-search-toggle');
        var searchClose = document.querySelector('.jb-search-close');
        var searchInput = document.querySelector('.jb-search-overlay .search-box-text');

        function openSearch() {
            document.body.classList.add('jb-search-open');
            if (searchInput) {
                // small delay to allow display:flex to apply before focusing
                setTimeout(function() { searchInput.focus(); }, 50);
            }
        }

        function closeSearch() {
            document.body.classList.remove('jb-search-open');
        }

        if (searchToggle) {
            searchToggle.addEventListener('click', openSearch);
        }
        if (searchClose) {
            searchClose.addEventListener('click', closeSearch);
        }

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
    });
})();
