(function () {
    if (window.__aiInterviewJobCardInit) {
        return;
    }

    window.__aiInterviewJobCardInit = true;

    var activeDrawer = null;
    var activeTrigger = null;

    function normalizePath(url) {
        try {
            return new URL(url, window.location.origin).pathname.toLowerCase();
        } catch (error) {
            return (url || '').toLowerCase();
        }
    }

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

    function lookupWishlistItemId(productUrl) {
        return fetch('/wishlist', {
            credentials: 'same-origin'
        }).then(function (response) {
            return response.text();
        }).then(function (html) {
            var parser = new DOMParser();
            var documentFragment = parser.parseFromString(html, 'text/html');
            var targetPath = normalizePath(productUrl);
            var matchingLink = Array.prototype.find.call(documentFragment.querySelectorAll('a[href]'), function (link) {
                return normalizePath(link.getAttribute('href')) === targetPath;
            });

            if (!matchingLink) {
                return 0;
            }

            var row = matchingLink.closest('tr');
            var removeCheckbox = row ? row.querySelector('input[name="removefromcart"]') : null;
            return removeCheckbox ? parseInt(removeCheckbox.value, 10) || 0 : 0;
        });
    }

    function postAdd(button) {
        return $.ajax({
            cache: false,
            url: button.getAttribute('data-add-url'),
            type: 'POST',
            data: addAntiForgeryToken({})
        }).then(function (response) {
            if (window.AjaxCart && typeof AjaxCart.success_process === 'function') {
                AjaxCart.success_process(response);
            }

            if (response && response.redirect) {
                setLocation(response.redirect);
                return $.Deferred().reject().promise();
            }

            if (!response || response.success !== true) {
                return $.Deferred().reject(response).promise();
            }

            return lookupWishlistItemId(button.getAttribute('data-product-url')).then(function (wishlistItemId) {
                setSavedState(button.getAttribute('data-product-id'), true, wishlistItemId);
                setStatus(button.getAttribute('data-product-id'), button.getAttribute('data-saved-text') || '');
            });
        });
    }

    function postRemove(button) {
        var wishlistItemId = parseInt(button.getAttribute('data-wishlist-item-id'), 10) || 0;
        if (!wishlistItemId) {
            return lookupWishlistItemId(button.getAttribute('data-product-url')).then(function (resolvedId) {
                if (!resolvedId) {
                    return $.Deferred().reject().promise();
                }

                button.setAttribute('data-wishlist-item-id', resolvedId);
                return postRemove(button);
            });
        }

        var requestData = addAntiForgeryToken({
            updatecart: 'updatecart',
            listId: 0,
            removefromcart: wishlistItemId
        });

        return $.ajax({
            cache: false,
            url: button.getAttribute('data-remove-url'),
            type: 'POST',
            data: requestData
        }).then(function () {
            setSavedState(button.getAttribute('data-product-id'), false, 0);
            setStatus(button.getAttribute('data-product-id'), button.getAttribute('data-removed-text') || '');
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

        var request = saveButton.getAttribute('data-is-saved') === 'true'
            ? postRemove(saveButton)
            : postAdd(saveButton);

        request.fail(function () {
            // nopCommerce ajax notifications are already handled on add failures
        }).always(function () {
            delete saveButton.dataset.pending;
        });
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && activeDrawer) {
            closeDrawer(activeDrawer);
        }
    });
})();
