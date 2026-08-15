(function () {
    'use strict';

    var documentRoot = document.documentElement;
    var body = document.body;
    var isLoginPage = documentRoot.classList.contains('html-login-page') ||
        (body && body.classList.contains('html-login-page'));
    var isRegistrationPage = documentRoot.classList.contains('html-registration-page') ||
        (body && body.classList.contains('html-registration-page'));

    if ((!isLoginPage && !isRegistrationPage) || !body) {
        return;
    }

    var page = isLoginPage
        ? document.querySelector('.login-page')
        : document.querySelector('.registration-page');
    var pageBody = page && page.querySelector('.page-body');
    var config = pageBody && pageBody.querySelector('.aiinterview-auth-config');

    if (!pageBody || !config || pageBody.querySelector('.auth-split-container')) {
        return;
    }

    var loginUrl = config.getAttribute('data-login-url');
    var registerUrl = config.getAttribute('data-register-url');

    if (!loginUrl || !registerUrl) {
        return;
    }

    config.remove();

    var mobileHiddenLeftQuery = window.matchMedia('(max-width: 768px)');
    var phrases = [
        'practice that builds confidence.',
        'opportunities shaped around skills.',
        'insights that sharpen your story.'
    ];
    var shell = document.createElement('div');
    var leftPanel = document.createElement('section');
    var rightPanel = document.createElement('section');
    var hero = document.createElement('div');
    var eyebrow = document.createElement('p');
    var title = document.createElement('h2');
    var subtitle = document.createElement('p');
    var typingTarget = document.createElement('span');
    var cursor = document.createElement('span');
    var indicators = document.createElement('ol');
    var tabs = document.createElement('nav');

    shell.className = 'auth-split-container';
    leftPanel.className = 'auth-left-panel';
    rightPanel.className = 'auth-right-panel';
    hero.className = 'auth-hero-content';
    eyebrow.className = 'auth-hero-eyebrow';
    title.className = 'auth-hero-title';
    subtitle.className = 'auth-hero-subtitle';
    typingTarget.className = 'auth-typing-target';
    typingTarget.id = 'aiinterview-auth-phrase';
    cursor.className = 'auth-typing-cursor';
    cursor.setAttribute('aria-hidden', 'true');
    indicators.className = 'auth-indicators';
    indicators.setAttribute('aria-label', 'Career journey highlights');
    tabs.className = 'auth-tabs';
    tabs.setAttribute('role', 'tablist');
    tabs.setAttribute('aria-label', 'Account access');

    eyebrow.textContent = 'AI-powered career journeys';
    title.textContent = 'Make your next move with confidence.';
    subtitle.appendChild(document.createTextNode('Find your direction with '));
    subtitle.appendChild(typingTarget);
    subtitle.appendChild(cursor);

    phrases.forEach(function (phrase, index) {
        var item = document.createElement('li');
        var indicator = document.createElement('button');

        indicator.type = 'button';
        indicator.className = 'auth-indicator';
        indicator.setAttribute('aria-label', 'Show highlight ' + (index + 1) + ': ' + phrase);
        indicator.setAttribute('aria-controls', typingTarget.id);
        indicator.setAttribute('aria-pressed', index === 0 ? 'true' : 'false');
        indicator.dataset.phraseIndex = index.toString();
        item.appendChild(indicator);
        indicators.appendChild(item);
    });

    function createTab(label, href, isActive) {
        var tab = document.createElement('a');

        tab.className = 'auth-tab' + (isActive ? ' is-active' : '');
        tab.href = href;
        tab.textContent = label;
        tab.setAttribute('role', 'tab');
        tab.setAttribute('aria-selected', isActive ? 'true' : 'false');
        if (isActive) {
            tab.setAttribute('aria-current', 'page');
        }

        return tab;
    }

    tabs.appendChild(createTab('Login', loginUrl, isLoginPage));
    tabs.appendChild(createTab('Register', registerUrl, isRegistrationPage));
    hero.appendChild(eyebrow);
    hero.appendChild(title);
    hero.appendChild(subtitle);
    hero.appendChild(indicators);
    leftPanel.appendChild(hero);

    while (pageBody.firstChild) {
        rightPanel.appendChild(pageBody.firstChild);
    }

    rightPanel.insertBefore(tabs, rightPanel.firstChild);
    shell.appendChild(leftPanel);
    shell.appendChild(rightPanel);
    pageBody.appendChild(shell);

    var indicatorButtons = Array.prototype.slice.call(
        indicators.querySelectorAll('.auth-indicator'));
    var reducedMotionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
    var activeIndex = 0;
    var animationTimer = 0;
    var characterTimer = 0;

    function synchronize(index) {
        activeIndex = index;
        indicatorButtons.forEach(function (indicator, indicatorIndex) {
            var isActive = indicatorIndex === activeIndex;
            indicator.classList.toggle('is-active', isActive);
            indicator.setAttribute('aria-pressed', isActive ? 'true' : 'false');
        });
    }

    function clearTimers() {
        window.clearTimeout(animationTimer);
        window.clearTimeout(characterTimer);
    }

    function animationCanRun() {
        return document.visibilityState !== 'hidden' && !mobileHiddenLeftQuery.matches;
    }

    function scheduleNext() {
        window.clearTimeout(animationTimer);
        if (!animationCanRun()) {
            return;
        }

        animationTimer = window.setTimeout(function () {
            showPhrase((activeIndex + 1) % phrases.length, true);
        }, 4000);
    }

    function typePhrase(phrase, position) {
        if (!animationCanRun()) {
            clearTimers();
            return;
        }

        typingTarget.textContent = phrase.slice(0, position);
        if (position < phrase.length) {
            characterTimer = window.setTimeout(function () {
                typePhrase(phrase, position + 1);
            }, 48);
            return;
        }

        scheduleNext();
    }

    function deletePhrase(nextIndex) {
        if (!animationCanRun()) {
            clearTimers();
            return;
        }

        var currentText = typingTarget.textContent || '';
        if (currentText.length > 0) {
            typingTarget.textContent = currentText.slice(0, -1);
            characterTimer = window.setTimeout(function () {
                deletePhrase(nextIndex);
            }, 24);
            return;
        }

        synchronize(nextIndex);
        typePhrase(phrases[nextIndex], 1);
    }

    function showPhrase(index, animate) {
        clearTimers();

        if (!animationCanRun()) {
            return;
        }

        if (reducedMotionQuery.matches || !animate) {
            synchronize(index);
            typingTarget.textContent = phrases[index];
            scheduleNext();
            return;
        }

        deletePhrase(index);
    }

    indicatorButtons.forEach(function (indicator) {
        indicator.addEventListener('click', function () {
            var selectedIndex = Number(indicator.dataset.phraseIndex);
            if (Number.isInteger(selectedIndex) && selectedIndex !== activeIndex) {
                showPhrase(selectedIndex, !reducedMotionQuery.matches);
            }
        });
    });

    function handleMotionPreferenceChange() {
        showPhrase(activeIndex, false);
    }

    function handleAnimationAvailabilityChange() {
        clearTimers();
        if (animationCanRun()) {
            showPhrase(activeIndex, false);
        }
    }

    if (typeof reducedMotionQuery.addEventListener === 'function') {
        reducedMotionQuery.addEventListener('change', handleMotionPreferenceChange);
    } else if (typeof reducedMotionQuery.addListener === 'function') {
        reducedMotionQuery.addListener(handleMotionPreferenceChange);
    }

    if (typeof mobileHiddenLeftQuery.addEventListener === 'function') {
        mobileHiddenLeftQuery.addEventListener('change', handleAnimationAvailabilityChange);
    } else if (typeof mobileHiddenLeftQuery.addListener === 'function') {
        mobileHiddenLeftQuery.addListener(handleAnimationAvailabilityChange);
    }

    document.addEventListener('visibilitychange', handleAnimationAvailabilityChange);

    showPhrase(0, false);
}());
