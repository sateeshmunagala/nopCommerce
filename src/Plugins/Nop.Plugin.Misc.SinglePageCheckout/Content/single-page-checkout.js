var SinglePageCheckoutSidebar = (function () {
    var summaryUrl = '';
    var isInitialized = false;

    function init(url) {
        summaryUrl = url;

        if (!isInitialized) {
            hookCheckoutPipeline();
            isInitialized = true;
        }

        bindCartFormBehaviors();
    }

    function hookCheckoutPipeline() {
        if (typeof Checkout !== 'undefined' && typeof Checkout.setStepResponse === 'function') {
            var originalSetStepResponse = Checkout.setStepResponse;
            Checkout.setStepResponse = function (response) {
                originalSetStepResponse.apply(Checkout, arguments);

                if (!response.error) {
                    refreshSidebar();
                    autoContinueShippingMethod(response);
                }
            };
        }
    }

    function autoContinueShippingMethod(response) {
        if (response.goto_section === 'shipping_method') {
            setTimeout(function () {
                var options = $('#checkout-shipping-method-load input[type="radio"][name="shippingoption"]');
                if (options.length === 1) {
                    options.prop('checked', true);
                    if (typeof ShippingMethod !== 'undefined' && typeof ShippingMethod.save === 'function') {
                        ShippingMethod.save();
                    }
                }
            }, 100);
        }
    }

    function bindCartFormBehaviors() {
        var form = $('#shopping-cart-form');
        if (form.length === 0) return;

        var lastSubmitClicked = null;

        form.off('click', 'button[type="submit"]').on('click', 'button[type="submit"]', function () {
            lastSubmitClicked = $(this);
        });

        form.off('change', '.qty-input, select.qty-dropdown').on('change', '.qty-input, select.qty-dropdown', function () {
            lastSubmitClicked = $('<button type="submit" name="updatecart" value="updatecart">updatecart</button>');
            form.submit();
        });

        form.off('submit').on('submit', function (e) {
            var submitIntent = lastSubmitClicked ? lastSubmitClicked.attr('name') : '';

            if (submitIntent === 'continueshopping' || submitIntent === 'checkout') {
                return true;
            }

            e.preventDefault();

            var data = form.serialize();
            if (lastSubmitClicked && lastSubmitClicked.attr('name')) {
                data += '&' + encodeURIComponent(lastSubmitClicked.attr('name')) + '=' + encodeURIComponent(lastSubmitClicked.attr('value') || '');
            }

            $.ajax({
                cache: false,
                type: form.attr('method') || 'POST',
                url: form.attr('action'),
                data: data,
                success: function (response) {
                    var parsedResponse;
                    try {
                        parsedResponse = $(response);
                    } catch (e) { }

                    if (parsedResponse && parsedResponse.find('.order-summary-content').length > 0) {
                        $('#spc-order-summary').html(parsedResponse.find('.order-summary-content').parent().html());
                        bindCartFormBehaviors();
                    } else {
                        refreshSidebar();
                    }
                },
                error: function (xhr, ajaxOptions, thrownError) {
                    if (typeof Checkout !== 'undefined' && typeof Checkout.ajaxFailure === 'function') {
                        Checkout.ajaxFailure(xhr, ajaxOptions, thrownError);
                    } else {
                        alert('Failed to update cart.');
                    }
                }
            });
        });
    }

    function refreshSidebar() {
        if (!summaryUrl) return;

        var container = $('#spc-sidebar-container');
        if (container.length === 0) return;

        $.ajax({
            cache: false,
            type: 'GET',
            url: summaryUrl,
            success: function (html) {
                container.html(html);
                bindCartFormBehaviors();
            }
        });
    }

    return {
        init: init
    };
})();