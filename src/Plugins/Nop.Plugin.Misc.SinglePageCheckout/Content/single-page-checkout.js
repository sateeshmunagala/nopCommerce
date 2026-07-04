var SinglePageCheckout = (function () {
  var summaryUrl = '';
  var failureUrl = '';
  var estimateShippingUrl = '';
  var noShippingOptionsMessage = '';
  var lastSubmitClicked = null;
  var billingAutoSubmitted = false;
  var autoSaveTimers = {};
  var autoSubmittedSelections = {
    shippingMethod: false,
    paymentMethod: false,
    paymentInfo: false
  };

  function init(config) {
    summaryUrl = config.summaryUrl || '';
    failureUrl = config.failureUrl || '';
    estimateShippingUrl = config.estimateShippingUrl || '';
    noShippingOptionsMessage = config.noShippingOptionsMessage || 'No shipping options found.';

    initAccordion();
    initCheckoutPipeline();
    bindSummaryForm();
    bindStepBehaviors();
    bindEstimateShipping();
    normalizePromoControls();
    maybeAutoSubmitBilling();
    updateConfirmButtonState();
    updatePrimaryColumnLayout();
  }

  function updatePrimaryColumnLayout() {
    var primaryColumn = $('.spc-column-primary');
    if (primaryColumn.length === 0) return;

    var visibleCards = primaryColumn.children('.spc-card').filter(function() {
      return $(this).css('display') !== 'none';
    });

    if (visibleCards.length === 0) {
      $('.spc-layout').addClass('spc-layout-empty-primary');
    } else {
      $('.spc-layout').removeClass('spc-layout-empty-primary');
    }
  }

  function getSelectValue(selectorOrElement) {
    var value = $(selectorOrElement).val();
    return value === undefined || value === null ? '' : value.toString();
  }

  function isBillingNewAddressSelected() {
    var value = getSelectValue('#billing-address-select');
    return value === '' || value === '0';
  }

  function isBillingExistingAddressSelected() {
    return !isBillingNewAddressSelected();
  }

  function isShippingExistingAddressSelected() {
    return getSelectValue('#shipping-address-select') !== '';
  }

  function setSummaryBusy(isBusy) {
    $('#spc-summary-container').toggleClass('spc-is-busy', isBusy);
  }

  function cleanupEmbeddedOrderSummaries() {
    $('#checkout-payment-info-load .order-summary, #checkout-confirm-order-load .order-summary').remove();
  }

  function normalizePromoControls() {
    $('#spc-promo-grid input, #spc-promo-grid button').attr('form', 'shopping-cart-form');
    $('#applydiscountcouponcode, #applygiftcardcouponcode')
      .removeClass('button-2')
      .addClass('button-1');
  }

  function updatePromoBoxesFromCartResponse(responseHtml) {
    if (typeof responseHtml !== 'string' || (responseHtml.indexOf('coupon-box') === -1 && responseHtml.indexOf('giftcard-box') === -1)) {
      return;
    }

    var responseDom = $('<div />').html(responseHtml);
    var discountBox = responseDom.find('.coupon-box').first();
    var giftCardBox = responseDom.find('.giftcard-box').first();

    if (discountBox.length && $('#spc-promo-grid .spc-card-discount .spc-card-body').length) {
      $('#spc-promo-grid .spc-card-discount .spc-card-body').html(discountBox);
    }

    if (giftCardBox.length && $('#spc-promo-grid .spc-card-giftcard .spc-card-body').length) {
      $('#spc-promo-grid .spc-card-giftcard .spc-card-body').html(giftCardBox);
    }

    normalizePromoControls();
  }

  function resetSelectionAutoSubmitState() {
    autoSubmittedSelections.shippingMethod = false;
    autoSubmittedSelections.paymentMethod = false;
    autoSubmittedSelections.paymentInfo = false;
  }

  function initAccordion() {
    Accordion.init('checkout-steps', '.step-title', true);
    Accordion.openSection('#opc-billing');
  }

  function initCheckoutPipeline() {
    Checkout.init(failureUrl);
    Accordion.disallowAccessToNextSections = false;
    Accordion.closeExistingSection = function () { };

    var originalSetStepResponse = Checkout.setStepResponse;
    Checkout.setStepResponse = function (response) {
      var result = originalSetStepResponse.apply(Checkout, arguments);

      syncDynamicSectionState(response);
      cleanupEmbeddedOrderSummaries();
      bindStepBehaviors();
      updateConfirmButtonState();

      if (!response.error) {
        resetSelectionAutoSubmitState();
        refreshSummary();
        autoAdvanceStep(response);
        ensureSelectedMethodsAdvance();
      }

      return result;
    };

    if (Billing.disableBillingAddressCheckoutStep) {
      Accordion.hideSection('#opc-billing');
      Billing.save();
    }
  }

  function syncDynamicSectionState(response) {
    Billing.initializeCountrySelect();
    Shipping.initializeCountrySelect();

    if ($('#billing-address-select').length > 0) {
      Billing.newAddress(isBillingNewAddressSelected());
    } else {
      Billing.newAddress(true);
    }

    if ($('#shipping-address-select').length > 0) {
      Shipping.newAddress(
        response && response.selected_id !== undefined ? response.selected_id : getSelectValue('#shipping-address-select'),
        getSelectValue('#billing-address-select')
      );
    }

    updatePrimaryColumnLayout();
  }

  function bindStepBehaviors() {
    $(document)
      .off('change.spcBilling', '#billing-address-select')
      .on('change.spcBilling', '#billing-address-select', function () {
        if (isBillingExistingAddressSelected()) {
          Billing.save();
        }
      });

    $(document)
      .off('change.spcShipSame', '#ShipToSameAddress')
      .on('change.spcShipSame', '#ShipToSameAddress', function () {
        Billing.save();
      });

    $(document)
      .off('change.spcShipping', '#shipping-address-select')
      .on('change.spcShipping', '#shipping-address-select', function () {
        if (isShippingExistingAddressSelected()) {
          Shipping.save();
        }
      });

    $(document)
      .off('change.spcShippingMethod', '#checkout-shipping-method-load input[name="shippingoption"]')
      .on('change.spcShippingMethod', '#checkout-shipping-method-load input[name="shippingoption"]', function () {
        autoSubmittedSelections.shippingMethod = true;
        ShippingMethod.save();
      });

    $(document)
      .off('change.spcPaymentMethod', '#checkout-payment-method-load input[name="paymentmethod"], #checkout-payment-method-load input[name="UseRewardPoints"], #checkout-payment-method-load input[id="UseRewardPoints"]')
      .on('change.spcPaymentMethod', '#checkout-payment-method-load input[name="paymentmethod"], #checkout-payment-method-load input[name="UseRewardPoints"], #checkout-payment-method-load input[id="UseRewardPoints"]', function () {
        autoSubmittedSelections.paymentMethod = true;
        PaymentMethod.save();
      });

    $(document)
      .off('blur.spcPaymentInfo change.spcPaymentInfo', '#co-payment-info-form input, #co-payment-info-form select, #co-payment-info-form textarea')
      .on('blur.spcPaymentInfo change.spcPaymentInfo', '#co-payment-info-form input, #co-payment-info-form select, #co-payment-info-form textarea', function (e) {
        if ($(this).is(':hidden') || $(this).is(':disabled') || $(this).attr('type') === 'hidden') {
          return;
        }

        scheduleSectionSave('payment-info', function () {
          PaymentInfo.save();
        }, e.type === 'change' ? 250 : 700);
      });
  }

  function scheduleSectionSave(sectionKey, callback, delay) {
    clearTimeout(autoSaveTimers[sectionKey]);

    autoSaveTimers[sectionKey] = setTimeout(function () {
      if (Checkout.loadWaiting !== false) {
        scheduleSectionSave(sectionKey, callback, 300);
        return;
      }

      callback();
    }, delay);
  }

  function bindSummaryForm() {
    $(document)
      .off('click.spcSummary', '#shopping-cart-form button[type="submit"], #spc-promo-grid button[type="submit"]')
      .on('click.spcSummary', '#shopping-cart-form button[type="submit"], #spc-promo-grid button[type="submit"]', function () {
        lastSubmitClicked = $(this);
      });

    $(document)
      .off('change.spcSummaryQty', '#shopping-cart-form .qty-input, #shopping-cart-form .qty-dropdown')
      .on('change.spcSummaryQty', '#shopping-cart-form .qty-input, #shopping-cart-form .qty-dropdown', function () {
        lastSubmitClicked = $('<button type="submit" name="updatecart" value="updatecart"></button>');
        $('#shopping-cart-form').trigger('submit');
      });

    $(document)
      .off('submit.spcSummary', '#shopping-cart-form')
      .on('submit.spcSummary', '#shopping-cart-form', function (e) {
        var form = $(this);
        var submitIntent = lastSubmitClicked ? lastSubmitClicked.attr('name') : '';

        if (submitIntent === 'continueshopping' || submitIntent === 'checkout') {
          return true;
        }

        e.preventDefault();

        var data = form.serialize();
        var promoData = $('#spc-promo-grid :input').serialize();
        if (promoData) {
          data += (data ? '&' : '') + promoData;
        }

        if (lastSubmitClicked && lastSubmitClicked.attr('name')) {
          data += '&' + encodeURIComponent(lastSubmitClicked.attr('name')) + '=' + encodeURIComponent(lastSubmitClicked.attr('value') || '');
        }

        $.ajax({
          cache: false,
          type: form.attr('method') || 'POST',
          url: form.attr('action'),
          data: data,
          beforeSend: function () {
            setSummaryBusy(true);
          },
          success: function (response) {
            updatePromoBoxesFromCartResponse(response);
            refreshSummary();
          },
          error: function (xhr, ajaxOptions, thrownError) {
            setSummaryBusy(false);
            if (typeof Checkout !== 'undefined' && typeof Checkout.ajaxFailure === 'function') {
              Checkout.ajaxFailure(xhr, ajaxOptions, thrownError);
            }
          }
        });
      });
  }

  function maybeAutoSubmitBilling() {
    var billingSelect = $('#billing-address-select');
    if (billingAutoSubmitted || billingSelect.length === 0 || !isBillingExistingAddressSelected()) {
      return;
    }

    billingAutoSubmitted = true;
    Billing.save();
  }

  function autoAdvanceStep(response) {
    if (response.goto_section === 'shipping_method') {
      maybeAutoSubmitSingleOption('#checkout-shipping-method-load input[name="shippingoption"]', ShippingMethod);
    }

    if (response.goto_section === 'payment_method') {
      maybeAutoSubmitSingleOption('#checkout-payment-method-load input[name="paymentmethod"]', PaymentMethod);
    }
  }

  function maybeAutoSubmitSingleOption(selector, stepHandler) {
    setTimeout(function () {
      var options = $(selector);
      if (options.length === 1) {
        options.prop('checked', true);
        stepHandler.save();
      }
    }, 100);
  }

  function ensureSelectedMethodsAdvance() {
    setTimeout(function () {
      if (Checkout.loadWaiting !== false) {
        return;
      }

      var shippingOptions = $('#checkout-shipping-method-load input[name="shippingoption"]');
      if (shippingOptions.length === 1 &&
        shippingOptions.is(':checked') &&
        !autoSubmittedSelections.shippingMethod &&
        $('#checkout-payment-method-load').find('input[name="paymentmethod"], input[name="UseRewardPoints"], input[id="UseRewardPoints"]').length === 0) {
        autoSubmittedSelections.shippingMethod = true;
        ShippingMethod.save();
        return;
      }

      var paymentMethods = $('#checkout-payment-method-load input[name="paymentmethod"]');
      var paymentInfoLoaded = $('#checkout-payment-info-load').children().length > 0;
      var paymentInfoFields = $('#co-payment-info-form').find('input, select, textarea').filter(function () {
        return !$(this).is(':hidden') && !$(this).is(':disabled') && $(this).attr('type') !== 'hidden';
      });

      var paymentInfoActions = $('#checkout-payment-info-load').find('button, input[type="button"], input[type="submit"], a.button-1, a.button-2').filter(function () {
        return !$(this).is(':hidden') && !$(this).is(':disabled');
      });

      var confirmLoaded = $('#checkout-confirm-order-load').children().length > 0 && $('#checkout-confirm-order-load .spc-placeholder').length === 0;

      if (paymentMethods.length === 1 &&
        paymentMethods.is(':checked') &&
        !paymentInfoLoaded &&
        !confirmLoaded &&
        !autoSubmittedSelections.paymentMethod) {
        autoSubmittedSelections.paymentMethod = true;
        PaymentMethod.save();
        return;
      }

      if (paymentInfoLoaded &&
        paymentInfoFields.length === 0 &&
        paymentInfoActions.length === 0 &&
        !confirmLoaded &&
        !autoSubmittedSelections.paymentInfo) {
        autoSubmittedSelections.paymentInfo = true;
        PaymentInfo.save();
      }
    }, 120);
  }

  function refreshSummary() {
    if (!summaryUrl) {
      return;
    }

    $.ajax({
      cache: false,
      type: 'GET',
      url: summaryUrl,
      beforeSend: function () {
        setSummaryBusy(true);
      },
      success: function (html) {
        $('#spc-summary-content').html(html);
        normalizePromoControls();
        bindSummaryForm();
        updateConfirmButtonState();
        ensureSelectedMethodsAdvance();
        updatePrimaryColumnLayout();
      },
      complete: function () {
        setSummaryBusy(false);
      }
    });
  }

  function bindEstimateShipping() {
    var countrySelect = $('#spc-estimate-shipping select[data-trigger="country-select"]');
    if (countrySelect.length) {
      countrySelect.countrySelect();
    }

    $(document)
      .off('click.spcEstimate', '#spc-estimate-shipping-button')
      .on('click.spcEstimate', '#spc-estimate-shipping-button', function () {
        var summaryData = $('#shopping-cart-form').serialize();
        var estimateData = $('#spc-estimate-shipping :input').serialize();
        var requestData = summaryData ? summaryData + '&' + estimateData : estimateData;

        $('#spc-estimate-shipping-errors').hide().empty();
        $('#spc-estimate-shipping-results').hide().empty();

        $.ajax({
          cache: false,
          type: 'POST',
          url: estimateShippingUrl,
          data: requestData,
          success: function (response) {
            if (!response.success) {
              renderEstimateErrors(response.errors || []);
              return;
            }

            renderEstimateResults(response.shippingOptions || []);
          },
          error: function (xhr, ajaxOptions, thrownError) {
            if (typeof Checkout !== 'undefined' && typeof Checkout.ajaxFailure === 'function') {
              Checkout.ajaxFailure(xhr, ajaxOptions, thrownError);
            }
          }
        });
      });
  }

  function renderEstimateErrors(errors) {
    var container = $('#spc-estimate-shipping-errors');
    if (!errors.length) {
      return;
    }

    var list = $('<ul />');
    $.each(errors, function (_, error) {
      list.append($('<li />').text(error));
    });

    container.append(list).show();
  }

  function renderEstimateResults(options) {
    var container = $('#spc-estimate-shipping-results');

    if (!options.length) {
      renderEstimateErrors([noShippingOptionsMessage]);
      return;
    }

    var list = $('<ul class="spc-estimate-option-list" />');

    $.each(options, function (_, option) {
      var item = $('<li class="spc-estimate-option" />');
      item.append($('<strong class="spc-estimate-option-name" />').text(option.name + ' - ' + option.price));

      if (option.description) {
        item.append($('<div class="spc-estimate-option-description" />').html(option.description));
      }

      list.append(item);
    });

    container.append(list).show();
  }

  function updateConfirmButtonState() {
    cleanupEmbeddedOrderSummaries();

    var confirmContainer = $('#checkout-confirm-order-load');
    var hasLoadedContent = confirmContainer.children().length > 0 &&
      confirmContainer.find('.spc-placeholder').length === 0 &&
      confirmContainer.text().trim().length > 0;
    var hasConfirmContent = hasLoadedContent || confirmContainer.find('.checkout-data, .confirm-order, .terms-of-service, .captcha-box').length > 0;

    $('#spc-confirm-order-button').prop('disabled', !hasConfirmContent);
    $('#confirm-order-buttons-container').toggleClass('spc-disabled', !hasConfirmContent);
  }

  return {
    init: init,
    syncDynamicSectionState: syncDynamicSectionState
  };
})();