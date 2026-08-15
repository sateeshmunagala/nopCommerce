(function () {
    'use strict';

    function normalizeMyActivityTab(tab) {
        switch ((tab || '').toLowerCase()) {
            case 'saved-jobs':
                return 'saved-jobs';
            case 'mock-interviews':
                return 'mock-interviews';
            case 'credits':
                return 'credits';
            default:
                return 'applied-jobs';
        }
    }

    function getMyActivityShell() {
        return document.querySelector('[data-my-activity-shell="true"]');
    }

    function getMyActivityContent() {
        return document.getElementById('my-activity-content');
    }

    function getMyActivityPanel() {
        var content = getMyActivityContent();
        if (!content) {
            return null;
        }

        return content.querySelector('[data-my-activity-tab-panel="true"]');
    }

    function getMyActivityTabFromUrl(url) {
        try {
            var parsedUrl = new URL(url || window.location.href, window.location.origin);
            return normalizeMyActivityTab(parsedUrl.searchParams.get('tab'));
        } catch (error) {
            return 'applied-jobs';
        }
    }

    function buildComparableMyActivityUrl(url) {
        try {
            var parsedUrl = new URL(url || window.location.href, window.location.origin);
            return parsedUrl.pathname + parsedUrl.search;
        } catch (error) {
            return window.location.pathname + window.location.search;
        }
    }

    function setMyActivityLoading(isLoading) {
        var shell = getMyActivityShell();
        if (!shell) {
            return;
        }

        shell.classList.toggle('is-loading', !!isLoading);
    }

    function syncMyActivityTabs(activeTab) {
        var shell = getMyActivityShell();
        if (!shell) {
            return;
        }

        var normalizedTab = normalizeMyActivityTab(activeTab || getMyActivityTabFromUrl(window.location.href));
        var tabLinks = shell.querySelectorAll('[data-my-activity-tab]');
        for (var i = 0; i < tabLinks.length; i++) {
            var tabLink = tabLinks[i];
            var isActive = normalizeMyActivityTab(tabLink.getAttribute('data-my-activity-tab')) === normalizedTab;
            tabLink.classList.toggle('is-active', isActive);
            if (isActive) {
                tabLink.setAttribute('aria-current', 'page');
            } else {
                tabLink.removeAttribute('aria-current');
            }
        }
    }

    function syncMyActivityFromContent() {
        var panel = getMyActivityPanel();
        var activeTab = panel ? panel.getAttribute('data-active-tab') : getMyActivityTabFromUrl(window.location.href);
        syncMyActivityTabs(activeTab);
    }

    function restoreMyActivityStateFromUrl() {
        var shell = getMyActivityShell();
        var content = getMyActivityContent();
        if (!shell || !content) {
            return;
        }

        var currentUrl = buildComparableMyActivityUrl(window.location.href);
        var panel = getMyActivityPanel();
        var panelUrl = panel ? panel.getAttribute('data-request-url') : '';

        syncMyActivityTabs(getMyActivityTabFromUrl(window.location.href));

        if (panelUrl === currentUrl) {
            return;
        }

        if (!window.htmx) {
            syncMyActivityFromContent();
            return;
        }

        setMyActivityLoading(true);
        window.htmx.ajax('GET', currentUrl, {
            source: content,
            target: content,
            swap: 'innerHTML'
        });
    }

    function bindMyActivityHtmx() {
        if (window.__jbMyActivityHtmxBound || !window.htmx || !document.body) {
            return false;
        }

        window.__jbMyActivityHtmxBound = true;

        document.body.addEventListener('htmx:beforeRequest', function (event) {
            if (!event.detail || !event.detail.target || event.detail.target.id !== 'my-activity-content') {
                return;
            }

            var trigger = event.detail && event.detail.elt && event.detail.elt.closest('[data-my-activity-tab]');
            if (trigger) {
                syncMyActivityTabs(trigger.getAttribute('data-my-activity-tab'));
            }

            setMyActivityLoading(true);
        });

        document.body.addEventListener('htmx:afterSwap', function (event) {
            if (!event.detail || !event.detail.target || event.detail.target.id !== 'my-activity-content') {
                return;
            }

            setMyActivityLoading(false);
            syncMyActivityFromContent();
        });

        document.body.addEventListener('htmx:responseError', function (event) {
            if (event.detail && event.detail.target && event.detail.target.id === 'my-activity-content') {
                setMyActivityLoading(false);
                syncMyActivityFromContent();
            }
        });

        document.body.addEventListener('htmx:sendError', function (event) {
            if (event.detail && event.detail.target && event.detail.target.id === 'my-activity-content') {
                setMyActivityLoading(false);
                syncMyActivityFromContent();
            }
        });

        return true;
    }

    function scheduleMyActivityHtmxBind(attempt) {
        if (bindMyActivityHtmx()) {
            return;
        }

        if (attempt >= 20) {
            return;
        }

        window.setTimeout(function () {
            scheduleMyActivityHtmxBind(attempt + 1);
        }, 50);
    }

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

    function initializeAuthHero() {
        var hero = document.querySelector('[data-jb-auth-hero]');
        if (!hero || hero.getAttribute('data-jb-auth-hero-initialized') === 'true') {
            return;
        }

        var typingTarget = hero.querySelector('[data-jb-auth-phrase]');
        var announcement = hero.querySelector('[data-jb-auth-announcement]');
        var indicators = hero.querySelectorAll('[data-jb-auth-phrase-index]');
        if (!typingTarget || !indicators.length) {
            return;
        }

        hero.setAttribute('data-jb-auth-hero-initialized', 'true');

        var phrases = [
            'practice that builds confidence.',
            'opportunities shaped around skills.',
            'insights that sharpen your story.'
        ];
        var reducedMotionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
        var hiddenHeroQuery = window.matchMedia('(max-width: 768px)');
        var activeIndex = 0;
        var rotationTimer = 0;
        var characterTimer = 0;

        function clearAuthTimers() {
            window.clearTimeout(rotationTimer);
            window.clearTimeout(characterTimer);
        }

        function animationCanRun() {
            return document.visibilityState !== 'hidden' && !hiddenHeroQuery.matches;
        }

        function synchronizeIndicators(index) {
            activeIndex = index;
            for (var i = 0; i < indicators.length; i++) {
                var isActive = i === activeIndex;
                indicators[i].classList.toggle('is-active', isActive);
                indicators[i].setAttribute('aria-pressed', isActive ? 'true' : 'false');
            }
        }

        function announcePhrase(phrase) {
            if (announcement) {
                announcement.textContent = 'Career highlight: ' + phrase;
            }
        }

        function scheduleNextPhrase() {
            window.clearTimeout(rotationTimer);
            if (!animationCanRun()) {
                return;
            }

            rotationTimer = window.setTimeout(function () {
                showPhrase((activeIndex + 1) % phrases.length, true, true);
            }, 4000);
        }

        function typePhrase(phrase, position, shouldAnnounce) {
            if (!animationCanRun()) {
                clearAuthTimers();
                return;
            }

            typingTarget.textContent = phrase.slice(0, position);
            if (position < phrase.length) {
                characterTimer = window.setTimeout(function () {
                    typePhrase(phrase, position + 1, shouldAnnounce);
                }, 48);
                return;
            }

            if (shouldAnnounce) {
                announcePhrase(phrase);
            }
            scheduleNextPhrase();
        }

        function deletePhrase(nextIndex, shouldAnnounce) {
            if (!animationCanRun()) {
                clearAuthTimers();
                return;
            }

            var currentText = typingTarget.textContent || '';
            if (currentText.length) {
                typingTarget.textContent = currentText.slice(0, -1);
                characterTimer = window.setTimeout(function () {
                    deletePhrase(nextIndex, shouldAnnounce);
                }, 24);
                return;
            }

            synchronizeIndicators(nextIndex);
            typePhrase(phrases[nextIndex], 1, shouldAnnounce);
        }

        function showPhrase(index, animate, shouldAnnounce) {
            clearAuthTimers();
            if (!animationCanRun()) {
                return;
            }

            if (reducedMotionQuery.matches || !animate) {
                synchronizeIndicators(index);
                typingTarget.textContent = phrases[index];
                if (shouldAnnounce) {
                    announcePhrase(phrases[index]);
                }
                scheduleNextPhrase();
                return;
            }

            deletePhrase(index, shouldAnnounce);
        }

        for (var i = 0; i < indicators.length; i++) {
            indicators[i].addEventListener('click', function () {
                var selectedIndex = Number(this.getAttribute('data-jb-auth-phrase-index'));
                if (Number.isInteger(selectedIndex) && selectedIndex !== activeIndex) {
                    showPhrase(selectedIndex, !reducedMotionQuery.matches, true);
                }
            });
        }

        function handleAnimationAvailabilityChange() {
            clearAuthTimers();
            if (animationCanRun()) {
                showPhrase(activeIndex, false, false);
            }
        }

        if (typeof reducedMotionQuery.addEventListener === 'function') {
            reducedMotionQuery.addEventListener('change', handleAnimationAvailabilityChange);
        } else if (typeof reducedMotionQuery.addListener === 'function') {
            reducedMotionQuery.addListener(handleAnimationAvailabilityChange);
        }

        if (typeof hiddenHeroQuery.addEventListener === 'function') {
            hiddenHeroQuery.addEventListener('change', handleAnimationAvailabilityChange);
        } else if (typeof hiddenHeroQuery.addListener === 'function') {
            hiddenHeroQuery.addListener(handleAnimationAvailabilityChange);
        }

        document.addEventListener('visibilitychange', handleAnimationAvailabilityChange);
        showPhrase(0, false, false);
    }

    function init() {
        if (document.body.getAttribute('data-jb-shell-init') === 'true') {
            return;
        }
        document.body.setAttribute('data-jb-shell-init', 'true');

        initializeAuthHero();

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
        var customerNavToggle = document.querySelector('.jb-account-nav-toggle');
        var customerNavClose = document.querySelector('.jb-account-nav-close');
        var customerNavDrawer = document.querySelector('#jb-account-navigation');
        var customerNavOverlay = document.querySelector('.jb-account-nav-overlay');

        if (customerNavToggle && customerNavDrawer) {
            document.body.classList.add('jb-account-nav-enhanced');
        }

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

        function syncCustomerNavState() {
            if (!customerNavToggle || !customerNavDrawer) {
                return;
            }

            var isMobile = window.innerWidth <= 1000;
            var isOpen = isMobile && document.body.classList.contains('jb-account-nav-open');

            customerNavToggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
            customerNavDrawer.setAttribute('aria-hidden', isMobile && !isOpen ? 'true' : 'false');

            if (customerNavOverlay) {
                customerNavOverlay.setAttribute('aria-hidden', isOpen ? 'false' : 'true');
            }
        }

        function closeCustomerNav(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            document.body.classList.remove('jb-account-nav-open');
            if (customerNavDrawer) {
                customerNavDrawer.classList.remove('is-open');
            }
            syncCustomerNavState();
        }

        function openCustomerNav(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            if (!customerNavDrawer || window.innerWidth > 1000) {
                return;
            }

            closeMenu();
            closeSearch();
            closeAccount();
            document.body.classList.add('jb-account-nav-open');
            customerNavDrawer.classList.add('is-open');
            syncCustomerNavState();
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

            // On vendor portal: desktop toggles sidebar collapse; mobile toggles slide-in drawer
            if (document.documentElement.classList.contains('html-vendor-portal-page')) {
                if (window.innerWidth > 1000) {
                    document.documentElement.classList.toggle('vp-sidebar-collapsed');
                } else {
                    document.documentElement.classList.toggle('vp-nav-open');
                    if (document.documentElement.classList.contains('vp-nav-open')) {
                        document.body.addEventListener('click', function closeVpNav(e) {
                            if (!e.target.closest('.side-2')) {
                                document.documentElement.classList.remove('vp-nav-open');
                                document.body.removeEventListener('click', closeVpNav);
                            }
                        });
                    }
                }
                return;
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
        attachClick(customerNavToggle, openCustomerNav);
        attachClick(customerNavClose, closeCustomerNav);

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

        if (customerNavOverlay) {
            customerNavOverlay.addEventListener('click', closeCustomerNav);
        }

        if (customerNavDrawer) {
            var customerNavLinks = customerNavDrawer.querySelectorAll('a');
            for (var customerNavIndex = 0; customerNavIndex < customerNavLinks.length; customerNavIndex++) {
                customerNavLinks[customerNavIndex].addEventListener('click', function () {
                    closeCustomerNav();
                });
            }
        }

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                closeMenu();
                closeSearch();
                closeAccount();
                closeCustomerNav();
            }
        });

        document.addEventListener('click', function (e) {
            if (document.body.classList.contains('jb-search-open')) {
                if (searchToggle && searchToggle.contains(e.target)) {
                    return;
                }

                if (searchOverlay && searchOverlay.contains(e.target)) {
                    return;
                }

                closeSearch();
            }

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

        var filtersPanel = document.querySelector('.product-filters');
        var filterPanelToggle = null;
        var filterPanelOverlay = null;
        var filterPanelClose = null;
        var filterPanelPlaceholder = null;
        var filterPanelOriginalParent = null;
        var filterPanelMode = null;
        var filterPanelBreakpoint = 1079;
        var filterPanelBreakpointBuffer = 32;
        var responsiveSyncFrame = 0;

        function getViewportWidth() {
            return window.innerWidth || document.documentElement.clientWidth || 0;
        }

        function isFilterPanelMobileMode() {
            var viewportWidth = getViewportWidth();
            var keepMobileMode = filterPanelMode === 'mobile' && document.body.classList.contains('jb-filter-panel-open');
            return keepMobileMode ? viewportWidth <= filterPanelBreakpoint + filterPanelBreakpointBuffer : viewportWidth <= filterPanelBreakpoint;
        }

        function syncFilterPanelModeClass() {
            document.body.classList.toggle('jb-filter-panel-mobile-mode', filterPanelMode === 'mobile');
        }

        function syncFilterPanelMount() {
            if (!filtersPanel) {
                return;
            }

            var shouldBeMobilePanel = isFilterPanelMobileMode();
            var nextMode = shouldBeMobilePanel ? 'mobile' : 'desktop';

            if (!filterPanelPlaceholder && filtersPanel.parentNode) {
                filterPanelPlaceholder = document.createElement('div');
                filterPanelPlaceholder.className = 'jb-filter-panel-placeholder';
                filterPanelOriginalParent = filtersPanel.parentNode;
                filterPanelOriginalParent.insertBefore(filterPanelPlaceholder, filtersPanel);
            }

            if (filterPanelMode === nextMode) {
                if (shouldBeMobilePanel && filtersPanel.parentNode === document.body) {
                    syncFilterPanelModeClass();
                    return;
                }

                if (!shouldBeMobilePanel && filterPanelPlaceholder && filterPanelPlaceholder.parentNode && filtersPanel.parentNode === filterPanelPlaceholder.parentNode) {
                    syncFilterPanelModeClass();
                    return;
                }
            }

            filterPanelMode = nextMode;
            syncFilterPanelModeClass();

            if (shouldBeMobilePanel) {
                if (filtersPanel.parentNode !== document.body) {
                    document.body.appendChild(filtersPanel);
                }
            } else if (filterPanelPlaceholder && filterPanelPlaceholder.parentNode && filtersPanel.parentNode !== filterPanelPlaceholder.parentNode) {
                filterPanelPlaceholder.parentNode.insertBefore(filtersPanel, filterPanelPlaceholder.nextSibling);
            }
        }

        function closeFilterPanel(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            document.body.classList.remove('jb-filter-panel-open');

            if (filterPanelToggle) {
                filterPanelToggle.setAttribute('aria-expanded', 'false');
            }
        }

        function openFilterPanel(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            syncFilterPanelMount();

            if (filterPanelMode !== 'mobile' || !filtersPanel) {
                return;
            }

            closeMenu();
            closeSearch();
            closeAccount();
            document.body.classList.add('jb-filter-panel-open');

            if (filterPanelToggle) {
                filterPanelToggle.setAttribute('aria-expanded', 'true');
            }
        }

        if (filtersPanel) {
            var filtersHost = document.querySelector('.page-body .center-2') || document.querySelector('.page-body');

            if (!filtersPanel.querySelector('.jb-filter-panel-header')) {
                var panelHeader = document.createElement('div');
                panelHeader.className = 'jb-filter-panel-header';
                panelHeader.innerHTML = '<span class="jb-filter-panel-title">Filter by</span><button class="jb-filter-panel-close" type="button" aria-label="Close filters">&times;</button>';
                filtersPanel.insertBefore(panelHeader, filtersPanel.firstChild);
            }

            if (!document.querySelector('.jb-filter-panel-overlay')) {
                filterPanelOverlay = document.createElement('div');
                filterPanelOverlay.className = 'jb-filter-panel-overlay';
                document.body.appendChild(filterPanelOverlay);
            } else {
                filterPanelOverlay = document.querySelector('.jb-filter-panel-overlay');
            }

            if (filtersHost && !document.querySelector('.jb-filter-panel-toggle')) {
                filterPanelToggle = document.createElement('button');
                filterPanelToggle.className = 'jb-filter-panel-toggle';
                filterPanelToggle.type = 'button';
                filterPanelToggle.setAttribute('aria-expanded', 'false');
                filterPanelToggle.setAttribute('aria-controls', 'jb-mobile-filter-panel');
                filterPanelToggle.innerHTML = '<span class="jb-filter-panel-toggle__icon" aria-hidden="true"></span><span class="jb-filter-panel-toggle__label">Filter</span>';
                filtersHost.insertBefore(filterPanelToggle, filtersHost.firstChild);
            } else {
                filterPanelToggle = document.querySelector('.jb-filter-panel-toggle');
            }

            filtersPanel.id = 'jb-mobile-filter-panel';
            filterPanelClose = filtersPanel.querySelector('.jb-filter-panel-close');

            attachClick(filterPanelToggle, openFilterPanel);
            attachClick(filterPanelClose, closeFilterPanel);
            filtersPanel.addEventListener('click', function (e) {
                e.stopPropagation();
            });

            if (filterPanelOverlay) {
                filterPanelOverlay.addEventListener('click', closeFilterPanel);
            }
        }

        var filterTitles = document.querySelectorAll('.product-filter .filter-title');
        var sideBlockTitles = document.querySelectorAll('.side-2 .block .title');

        function syncMobileFilters() {
            if (!filterTitles.length) {
                return;
            }

            for (var m = 0; m < filterTitles.length; m++) {
                var title = filterTitles[m];
                var content = title.nextElementSibling;

                if (!content || !content.classList.contains('filter-content')) {
                    continue;
                }

                var isOpen = title.classList.contains('is-open');
                content.style.display = 'block';
                content.style.overflow = 'hidden';
                content.style.paddingTop = isOpen ? '16px' : '0px';
                content.style.paddingBottom = isOpen ? '' : '0px';
                content.style.marginBottom = isOpen ? '' : '0px';
                content.style.borderTopWidth = isOpen ? '' : '0px';
                content.style.maxHeight = isOpen ? content.scrollHeight + 24 + 'px' : '0px';
                content.style.opacity = isOpen ? '1' : '0';
                content.style.visibility = isOpen ? 'visible' : 'hidden';
            }
        }

        function syncSideBlocks() {
            if (!sideBlockTitles.length) {
                return;
            }

            var isMobile = getViewportWidth() <= filterPanelBreakpoint;

            for (var p = 0; p < sideBlockTitles.length; p++) {
                var blockTitle = sideBlockTitles[p];
                var blockContent = blockTitle.nextElementSibling;

                if (!blockContent || !blockContent.classList.contains('listbox')) {
                    continue;
                }

                if (!isMobile) {
                    blockTitle.classList.remove('jb-block-title');
                    blockTitle.classList.add('is-open');
                    blockContent.style.display = 'block';
                    blockContent.style.overflow = '';
                    blockContent.style.maxHeight = '';
                    blockContent.style.opacity = '';
                    blockContent.style.visibility = '';
                    blockContent.style.paddingTop = '';
                    blockContent.style.marginBottom = '';
                    continue;
                }

                blockTitle.classList.add('jb-block-title');
                var isOpen = blockTitle.classList.contains('is-open');
                blockContent.style.display = 'block';
                blockContent.style.overflow = 'hidden';
                blockContent.style.paddingTop = isOpen ? '12px' : '0px';
                blockContent.style.marginBottom = isOpen ? '' : '0px';
                blockContent.style.maxHeight = isOpen ? blockContent.scrollHeight + 18 + 'px' : '0px';
                blockContent.style.opacity = isOpen ? '1' : '0';
                blockContent.style.visibility = isOpen ? 'visible' : 'hidden';
            }
        }

        for (var n = 0; n < filterTitles.length; n++) {
            if (!filterTitles[n].classList.contains('is-open')) {
                filterTitles[n].classList.add('is-open');
            }

            filterTitles[n].addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                if (typeof e.stopImmediatePropagation === 'function') {
                    e.stopImmediatePropagation();
                }

                this.classList.toggle('is-open');
                syncMobileFilters();
            }, true);
        }

        for (var q = 0; q < sideBlockTitles.length; q++) {
            if (!sideBlockTitles[q].classList.contains('is-open')) {
                sideBlockTitles[q].classList.add('is-open');
            }

            sideBlockTitles[q].addEventListener('click', function (e) {
                if (window.innerWidth > filterPanelBreakpoint) {
                    return;
                }

                var nextBox = this.nextElementSibling;
                if (!nextBox || !nextBox.classList.contains('listbox')) {
                    return;
                }

                e.preventDefault();
                e.stopPropagation();
                this.classList.toggle('is-open');
                syncSideBlocks();
            }, true);
        }

        function syncCatalogResponsiveUI() {
            var viewportWidth = getViewportWidth();
            var wasMobilePanel = filterPanelMode === 'mobile';

            if (viewportWidth > filterPanelBreakpoint) {
                closeMenu();
                closeSearch();
                closeAccount();
                closeCustomerNav();
            }

            syncFilterPanelMount();

            if (wasMobilePanel && filterPanelMode !== 'mobile') {
                closeFilterPanel();
            }

            syncMobileFilters();
            syncSideBlocks();
            syncCustomerNavState();
        }

        window.addEventListener('resize', function () {
            if (responsiveSyncFrame) {
                window.cancelAnimationFrame(responsiveSyncFrame);
            }

            responsiveSyncFrame = window.requestAnimationFrame(function () {
                responsiveSyncFrame = 0;
                syncCatalogResponsiveUI();
            });
        });

        syncCatalogResponsiveUI();
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                closeFilterPanel();
            }
        });
        syncExpandedState();
        syncCustomerNavState();
        syncMyActivityFromContent();
        scheduleMyActivityHtmxBind(0);
        window.addEventListener('popstate', function () {
            setMyActivityLoading(false);
            window.setTimeout(restoreMyActivityStateFromUrl, 0);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
