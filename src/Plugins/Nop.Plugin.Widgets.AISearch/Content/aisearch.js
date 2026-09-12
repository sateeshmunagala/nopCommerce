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

        function render(payload) {
            results.replaceChildren();
            var message = document.createElement('p');
            message.className = 'aisearch-message';
            message.textContent = payload.message || payload.Message || '';
            results.appendChild(message);

            var products = payload.products || payload.Products || [];
            if (!products.length)
                return;

            var grid = document.createElement('div');
            grid.className = 'aisearch-product-grid';
            products.forEach(function (product) {
                var id = product.id || product.Id;
                var name = product.name || product.Name || '';
                var pictureUrl = product.pictureUrl || product.PictureUrl || '';
                var formattedPrice = product.formattedPrice || product.FormattedPrice || '';
                var url = product.url || product.Url || '#';
                var card = document.createElement('article');
                card.className = 'aisearch-product-card';

                var image = document.createElement('img');
                image.src = pictureUrl;
                image.alt = name;
                image.loading = 'lazy';

                var title = document.createElement('h3');
                title.textContent = name;

                var price = document.createElement('p');
                price.className = 'aisearch-product-price';
                price.textContent = formattedPrice;

                var link = document.createElement('a');
                link.href = url;
                link.textContent = 'View';
                link.setAttribute('data-product-id', id);

                card.append(image, title, price, link);
                grid.appendChild(card);
            });
            results.appendChild(grid);
        }

        async function search(query) {
            query = (query || promptValue()).trim();
            if (!query)
                return;

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
                render(await response.json());
            } catch (error) {
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
