(function () {
    if (window.__aiInterviewJobCardInit) {
        return;
    }

    window.__aiInterviewJobCardInit = true;

    var activeDrawer = null;
    var activeTrigger = null;

    function executeScripts(container) {
        Array.prototype.forEach.call(container.querySelectorAll('script'), function (oldScript) {
            var newScript = document.createElement('script');

            Array.prototype.forEach.call(oldScript.attributes, function (attribute) {
                newScript.setAttribute(attribute.name, attribute.value);
            });

            newScript.text = oldScript.text || oldScript.textContent || '';
            oldScript.parentNode.replaceChild(newScript, oldScript);
        });
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

    function showErrorMessage(message) {
        if (!message) {
            return;
        }

        var text = Array.isArray(message) ? message.join('\n') : message;
        if (window.displayBarNotification) {
            displayBarNotification(text, 'error', 0);
        }
    }

    function getJobAiPanel(target) {
        return target && target.closest ? target.closest('[data-job-ai-panel="true"]') : null;
    }

    function getJobAiNode(panel, key) {
        if (!panel) {
            return null;
        }

        var id = panel.getAttribute('data-' + key + '-id');
        return id ? document.getElementById(id) : null;
    }

    function getJobAiButtons(panel) {
        return panel ? panel.querySelectorAll('[data-job-ai-action]') : [];
    }

    function setJobAiFeedback(panel, message, isError) {
        var feedback = getJobAiNode(panel, 'feedback');
        if (!feedback) {
            return;
        }

        feedback.textContent = message || '';
        feedback.classList.toggle('is-visible', !!message);
        feedback.classList.toggle('is-error', !!message && !!isError);
        feedback.classList.toggle('is-success', !!message && !isError);
        feedback.classList.toggle('is-info', false);
    }

    function setJobAiBusy(panel, isBusy) {
        Array.prototype.forEach.call(getJobAiButtons(panel), function (button) {
            if (!button.hasAttribute('data-job-ai-original-disabled')) {
                button.setAttribute('data-job-ai-original-disabled', button.disabled ? 'true' : 'false');
            }

            button.disabled = !!isBusy || button.getAttribute('data-job-ai-original-disabled') === 'true';
            button.classList.toggle('is-loading', !!isBusy);
        });
    }

    function appendFormInputsFromPanel(formData, panel) {
        var applyPanel = getJobAiNode(panel, 'apply-panel');
        if (!applyPanel) {
            return;
        }

        Array.prototype.forEach.call(applyPanel.querySelectorAll('input, select, textarea'), function (field) {
            if (!field.name) {
                return;
            }

            if (field.type === 'file') {
                Array.prototype.forEach.call(field.files || [], function (file) {
                    formData.append(field.name, file);
                });
                return;
            }

            if ((field.type === 'radio' || field.type === 'checkbox') && !field.checked) {
                return;
            }

            formData.append(field.name, field.value);
        });
    }

    function buildJobAiFormData(panel) {
        var productForm = getJobAiNode(panel, 'product-form');
        var formData = productForm ? new FormData(productForm) : new FormData();

        if (!productForm) {
            appendFormInputsFromPanel(formData, panel);
        }

        var productId = panel && panel.getAttribute('data-product-id');
        if (productId) {
            formData.set('productId', productId);
        }

        var sponsorToken = panel && panel.getAttribute('data-sponsor-token');
        if (sponsorToken) {
            formData.set('sponsorToken', sponsorToken);
        }

        return formData;
    }

    function postJobAiJson(url, formData, requestErrorText) {
        return fetch(url, {
            method: 'POST',
            body: formData
        }).then(function (response) {
            var contentType = response.headers.get('content-type') || '';
            if (contentType.indexOf('application/json') === -1) {
                return { success: false, error: requestErrorText };
            }

            return response.json().catch(function () {
                return { success: false, error: requestErrorText };
            });
        }).catch(function () {
            return { success: false, error: requestErrorText };
        });
    }

    function handleJobAiAction(panel, action) {
        if (!panel) {
            return Promise.resolve();
        }

        var url = panel.getAttribute(action === 'start' ? 'data-start-url' : 'data-apply-url');
        var requestErrorText = panel.getAttribute('data-request-error') || 'Unable to reach the interview service. Please check your network and try again.';
        if (!url) {
            setJobAiFeedback(panel, requestErrorText, true);
            return Promise.resolve();
        }

        setJobAiBusy(panel, true);
        return postJobAiJson(url, buildJobAiFormData(panel), requestErrorText)
            .then(function (result) {
                if (result && result.requiresLogin) {
                    var loginRedirect = result.redirect || panel.getAttribute('data-login-url');
                    if (loginRedirect) {
                        window.location.href = loginRedirect;
                        return;
                    }
                }

                if (result && result.redirect && action !== 'start') {
                    window.location.href = result.redirect;
                    return;
                }

                if (action === 'start' && result && result.runtimeUrl) {
                    window.location.href = result.runtimeUrl;
                    return;
                }

                setJobAiFeedback(panel, (result && (result.message || result.error)) || requestErrorText, !result || result.success !== true);
            })
            .finally(function () {
                setJobAiBusy(panel, false);
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

        if (drawer.parentElement !== document.body) {
            document.body.appendChild(drawer);
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

    function loadDrawerContent(drawer) {
        var drawerBody = drawer.querySelector('[data-ai-job-drawer-body]');
        var drawerUrl = drawer.getAttribute('data-drawer-url');
        var productUrl = drawer.getAttribute('data-product-url');
        var productLinkText = drawer.getAttribute('data-product-link-text') || '';
        var drawerErrorText = drawer.getAttribute('data-error-text') || '';

        if (!drawerBody || !drawerUrl || drawer.dataset.loaded === 'true') {
            return Promise.resolve();
        }

        drawer.dataset.loaded = 'pending';

        return fetch(drawerUrl, {
            credentials: 'same-origin'
        }).then(function (response) {
            if (!response.ok) {
                throw new Error('Unable to load drawer content.');
            }

            return response.text();
        }).then(function (html) {
            drawerBody.innerHTML = html;
            executeScripts(drawerBody);
            drawer.dataset.loaded = 'true';
        }).catch(function () {
            drawer.dataset.loaded = 'false';
            drawerBody.innerHTML = '';

            var message = document.createElement('div');
            message.className = 'ai-job-preview-loading';
            message.textContent = drawerErrorText;
            drawerBody.appendChild(message);

            if (productUrl) {
                var link = document.createElement('a');
                link.className = 'button-2 ai-job-preview-fallback-link';
                link.href = productUrl;
                link.textContent = productLinkText;
                drawerBody.appendChild(link);
            }
        });
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
            var drawer = document.getElementById(openTrigger.getAttribute('data-ai-job-preview-open'));
            openDrawer(drawer, openTrigger);
            loadDrawerContent(drawer);
            return;
        }

        var closeTrigger = event.target.closest('[data-ai-job-preview-close="true"]');
        if (closeTrigger) {
            event.preventDefault();
            closeDrawer(closeTrigger.closest('.ai-job-preview-drawer'));
            return;
        }

        var jobAiAction = event.target.closest('[data-job-ai-action]');
        if (jobAiAction) {
            event.preventDefault();
            handleJobAiAction(getJobAiPanel(jobAiAction), jobAiAction.getAttribute('data-job-ai-action'));
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
