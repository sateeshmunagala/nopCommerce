(function () {
    'use strict';

    function initialize(root) {
        var overlay = root.querySelector('.aisearch-overlay');
        var openButton = root.querySelector('.aisearch-floating-button');
        var closeButton = root.querySelector('.aisearch-close-button');
        var searchButton = root.querySelector('.aisearch-search-button');
        var results = root.querySelector('#aisearch-results');
        var licenseKey = root.dataset.licenseKey;
        var assistView;

        if (licenseKey && window.ej && ej.base)
            ej.base.registerLicense(licenseKey);

        function setOpen(open) {
            document.body.classList.toggle('aisearch-overlay-open', open);
            overlay.setAttribute('aria-hidden', open ? 'false' : 'true');
            if (open)
                window.setTimeout(focusPrompt, 200);
        }

        function promptValue() {
            var input = root.querySelector('.e-assist-textarea, .e-prompt-textarea, textarea');
            return input ? input.value : (assistView && assistView.prompt ? assistView.prompt : '');
        }

        function focusPrompt() {
            var input = root.querySelector('.e-assist-textarea, .e-prompt-textarea, textarea');
            if (input)
                input.focus();
        }

        function showLoading() {
            results.replaceChildren();
            var spinner = document.createElement('div');
            spinner.className = 'aisearch-loading-spinner';
            spinner.setAttribute('role', 'status');
            spinner.setAttribute('aria-label', 'Searching');
            results.appendChild(spinner);
        }

        function showError() {
            results.replaceChildren();
            var message = document.createElement('p');
            message.className = 'aisearch-message';
            message.textContent = 'Something went wrong, please try again';
            results.appendChild(message);
        }

        function escapeHtml(value) {
            var element = document.createElement('div');
            element.textContent = value == null ? '' : String(value);
            return element.innerHTML;
        }

        function render(payload) {
            var html = '<p class="aisearch-message">' +
                escapeHtml(payload.message || payload.Message || '') + '</p>';

            var products = payload.products || payload.Products || [];
            if (!products.length)
                return html;

            html += '<div class="aisearch-product-grid">';
            products.forEach(function (product) {
                var id = product.id || product.Id;
                var name = product.name || product.Name || '';
                var pictureUrl = product.pictureUrl || product.PictureUrl || '';
                var formattedPrice = product.formattedPrice || product.FormattedPrice || '';
                var url = product.url || product.Url || '#';
                html += '<article class="aisearch-product-card">' +
                    '<img src="' + escapeHtml(pictureUrl) + '" alt="' + escapeHtml(name) + '" loading="lazy">' +
                    '<h3>' + escapeHtml(name) + '</h3>' +
                    '<p class="aisearch-product-price">' + escapeHtml(formattedPrice) + '</p>' +
                    '<a href="' + escapeHtml(url) + '" data-product-id="' + escapeHtml(id) + '">View</a>' +
                    '</article>';
            });
            return html + '</div>';
        }

        async function search(query) {
            query = (query || promptValue()).trim();
            if (!query)
                return;

            if (!assistView)
                showLoading();
            try {
                var token = root.querySelector('input[name="__RequestVerificationToken"]');
                var response = await fetch(root.dataset.searchUrl, {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': token ? token.value : ''
                    },
                    body: JSON.stringify({ query: query, storeId: Number(root.dataset.storeId) })
                });
                if (!response.ok)
                    throw new Error('Search request failed');
                var responseHtml = render(await response.json());
                if (assistView)
                    assistView.addPromptResponse(responseHtml);
                else
                    results.innerHTML = responseHtml;
            } catch (error) {
                if (assistView)
                    assistView.addPromptResponse('<p class="aisearch-message">Something went wrong, please try again</p>');
                else
                    showError();
            }
        }

        openButton.addEventListener('click', function () { setOpen(true); });
        closeButton.addEventListener('click', function () { setOpen(false); });
        overlay.addEventListener('click', function (event) {
            if (event.target === overlay)
                setOpen(false);
        });
        searchButton.addEventListener('click', function () { search(); });

        if (window.ej && ej.interactivechat) {
            assistView = new ej.interactivechat.AIAssistView({
                promptPlaceholder: 'Describe what you are looking for',
                promptRequest: function (args) { search(args.prompt); }
            });
            assistView.appendTo(root.querySelector('#aisearch-assistview'));
        }
    }

    function start() {
        document.querySelectorAll('.aisearch-widget-root').forEach(initialize);
    }

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', start);
    else
        start();
})();
