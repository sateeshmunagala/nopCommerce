(function () {
    if (window.__aiInterviewJobCardInit) {
        return;
    }

    window.__aiInterviewJobCardInit = true;

    var activeDrawer = null;
    var activeTrigger = null;

    function getStatusNodes(productId) {
        return document.querySelectorAll('[data-ai-job-save-status="' + productId + '"]');
    }

    function setStatus(productId, text) {
        getStatusNodes(productId).forEach(function (node) {
            node.textContent = text || '';
        });
    }

    function setSavedState(productId, isSaved, wishlistItemId) {
        document.querySelectorAll('.ai-job-card-save[data-product-id="' + productId + '"]').forEach(function (button) {
            var saveLabel = button.getAttribute('data-save-label') || '';
            var removeLabel = button.getAttribute('data-remove-label') || '';
            var label = isSaved ? removeLabel : saveLabel;
            var srText = button.querySelector('.ai-job-card-visually-hidden');

            button.classList.toggle('is-saved', isSaved);
            button.setAttribute('data-is-saved', isSaved ? 'true' : 'false');
            button.setAttribute('data-wishlist-item-id', wishlistItemId || 0);
            button.setAttribute('aria-pressed', isSaved ? 'true' : 'false');
            button.setAttribute('aria-label', label);
            button.setAttribute('title', label);

            if (srText) {
                srText.textContent = label;
            }
        });
    }

    function showErrorMessage(message) {
        if (!message) {
            return;
        }

        var text = Array.isArray(message) ? message.join('\n') : message;
        if (window.displayBarNotification) {
            displayBarNotification(text, 'error', 0);
        }
    }

    function closeDrawer(drawer) {
        var target = drawer || activeDrawer;
        if (!target) {
            return;
        }

        target.classList.remove('is-open');
        target.setAttribute('aria-hidden', 'true');
        document.body.classList.remove('ai-job-preview-open');

        if (activeTrigger) {
            activeTrigger.setAttribute('aria-expanded', 'false');
            activeTrigger.focus();
        }

        activeDrawer = null;
        activeTrigger = null;
    }

    function openDrawer(drawer, trigger) {
        if (!drawer) {
            return;
        }

        closeDrawer();
        activeDrawer = drawer;
        activeTrigger = trigger || null;

        drawer.classList.add('is-open');
        drawer.setAttribute('aria-hidden', 'false');
        document.body.classList.add('ai-job-preview-open');

        if (trigger) {
            trigger.setAttribute('aria-expanded', 'true');
        }

        var panel = drawer.querySelector('.ai-job-preview-drawer-panel');
        if (panel) {
            panel.focus();
        }
    }

    function postToggle(button) {
        var shouldSave = button.getAttribute('data-is-saved') !== 'true';

        return $.ajax({
            cache: false,
            url: button.getAttribute('data-toggle-url'),
            type: 'POST',
            data: addAntiForgeryToken({
                productId: parseInt(button.getAttribute('data-product-id'), 10) || 0,
                save: shouldSave
            })
        }).then(function (response) {
            if (response && response.redirect) {
                setLocation(response.redirect);
                return $.Deferred().reject().promise();
            }

            if (!response || response.success !== true) {
                showErrorMessage(response && response.message);
                return $.Deferred().reject(response).promise();
            }

            if (window.AjaxCart && typeof AjaxCart.success_process === 'function') {
                AjaxCart.success_process(response);
            }

            setSavedState(button.getAttribute('data-product-id'), response.isSaved === true, response.wishlistItemId || 0);
            setStatus(button.getAttribute('data-product-id'), response.message || '');
        });
    }

    document.addEventListener('click', function (event) {
        var openTrigger = event.target.closest('[data-ai-job-preview-open]');
        if (openTrigger) {
            event.preventDefault();
            openDrawer(document.getElementById(openTrigger.getAttribute('data-ai-job-preview-open')), openTrigger);
            return;
        }

        var closeTrigger = event.target.closest('[data-ai-job-preview-close="true"]');
        if (closeTrigger) {
            event.preventDefault();
            closeDrawer(closeTrigger.closest('.ai-job-preview-drawer'));
            return;
        }

        var saveButton = event.target.closest('.ai-job-card-save');
        if (!saveButton) {
            return;
        }

        event.preventDefault();

        if (saveButton.dataset.pending === 'true') {
            return;
        }

        saveButton.dataset.pending = 'true';

        postToggle(saveButton).always(function () {
            delete saveButton.dataset.pending;
        });
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && activeDrawer) {
            closeDrawer(activeDrawer);
        }
    });
})();
