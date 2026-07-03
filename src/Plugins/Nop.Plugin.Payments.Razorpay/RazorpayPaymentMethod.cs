using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Razorpay.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Plugin.Payments.Razorpay.Components;
using Nop.Services.Common;

namespace Nop.Plugin.Payments.Razorpay;

public class RazorpayPaymentMethod : BasePlugin, IPaymentMethod
{
    private readonly ISettingService _settingService;
    private readonly RazorpayPaymentSettings _razorpayPaymentSettings;
    private readonly ILocalizationService _localizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly RazorpayHttpClient _razorpayHttpClient;
    private readonly IOrderTotalCalculationService _orderTotalCalculationService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly IWorkContext _workContext;

    public RazorpayPaymentMethod(
        ISettingService settingService,
        RazorpayPaymentSettings razorpayPaymentSettings,
        ILocalizationService localizationService,
        IHttpContextAccessor httpContextAccessor,
        RazorpayHttpClient razorpayHttpClient,
        IOrderTotalCalculationService orderTotalCalculationService,
        IGenericAttributeService genericAttributeService,
        IWorkContext workContext)
    {
        _settingService = settingService;
        _razorpayPaymentSettings = razorpayPaymentSettings;
        _localizationService = localizationService;
        _httpContextAccessor = httpContextAccessor;
        _razorpayHttpClient = razorpayHttpClient;
        _orderTotalCalculationService = orderTotalCalculationService;
        _genericAttributeService = genericAttributeService;
        _workContext = workContext;
    }

    public bool SupportCapture => false;
    public bool SupportPartiallyRefund => false;
    public bool SupportRefund => false;
    public bool SupportVoid => false;
    public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;
    public PaymentMethodType PaymentMethodType => PaymentMethodType.Standard;
    public bool SkipPaymentInfo => false;

    public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
    {
        return Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });
    }

    public Task<bool> CanRePostProcessPaymentAsync(Order order) => Task.FromResult(false);

    public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
    {
        return Task.FromResult(new CapturePaymentResult { Errors = new[] { "Capture method not supported" } });
    }

    public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
    {
        return await _orderTotalCalculationService.CalculatePaymentAdditionalFeeAsync(cart,
            _razorpayPaymentSettings.AdditionalFee, _razorpayPaymentSettings.AdditionalFeePercentage);
    }

    public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
    {
        var request = new ProcessPaymentRequest();
        
        request.CustomValues[RazorpayDefaults.RazorpayOrderIdAttribute] = form["RazorpayOrderId"].ToString();
        request.CustomValues[RazorpayDefaults.RazorpayPaymentIdAttribute] = form["RazorpayPaymentId"].ToString();
        request.CustomValues[RazorpayDefaults.RazorpaySignatureAttribute] = form["RazorpaySignature"].ToString();

        return Task.FromResult(request);
    }

    public Task<string> GetPaymentMethodDescriptionAsync()
    {
        return _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.PaymentMethodDescription");
    }

    public Type GetPublicViewComponent()
    {
        return typeof(RazorpayPaymentInfoViewComponent);
    }

    public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
    {
        if (string.IsNullOrEmpty(_razorpayPaymentSettings.KeyId) || string.IsNullOrEmpty(_razorpayPaymentSettings.KeySecret))
        {
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public async Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
    {
        var result = new ProcessPaymentResult();

        var orderId = processPaymentRequest.CustomValues.TryGetValue(RazorpayDefaults.RazorpayOrderIdAttribute, out var oId) ? oId.ToString() : string.Empty;
        var paymentId = processPaymentRequest.CustomValues.TryGetValue(RazorpayDefaults.RazorpayPaymentIdAttribute, out var pId) ? pId.ToString() : string.Empty;
        var signature = processPaymentRequest.CustomValues.TryGetValue(RazorpayDefaults.RazorpaySignatureAttribute, out var sig) ? sig.ToString() : string.Empty;

        if (string.IsNullOrEmpty(orderId) || string.IsNullOrEmpty(paymentId) || string.IsNullOrEmpty(signature))
        {
            result.AddError(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.PaymentDetailsMissing"));
            return result;
        }

        var customer = await _workContext.GetCurrentCustomerAsync();
        var storeId = processPaymentRequest.StoreId;

        var serverOrderId = await _genericAttributeService.GetAttributeAsync<string>(customer, RazorpayDefaults.RazorpayOrderIdAttribute, storeId);

        if (string.IsNullOrEmpty(serverOrderId) || !serverOrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase))
        {
            result.AddError(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.OrderMismatch"));
            return result;
        }

        var isSignatureValid = _razorpayHttpClient.VerifySignature(orderId, paymentId, signature, _razorpayPaymentSettings.KeySecret);

        if (!isSignatureValid)
        {
            result.AddError(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.VerificationFailed"));
            return result;
        }

        try
        {
            var payment = await _razorpayHttpClient.GetPaymentAsync(_razorpayPaymentSettings.KeyId, _razorpayPaymentSettings.KeySecret, paymentId);

            if (!payment.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.OrderMismatch"));
                return result;
            }

            var serverOrderAmount = await _genericAttributeService.GetAttributeAsync<decimal>(customer, RazorpayDefaults.RazorpayOrderAmountAttribute, storeId);

            var expectedAmountInSubunits = Math.Round(processPaymentRequest.OrderTotal * 100, 0);
            if (payment.Amount != expectedAmountInSubunits || payment.Amount != serverOrderAmount)
            {
                result.AddError(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.AmountMismatch"));
                return result;
            }

            var serverOrderCurrency = await _genericAttributeService.GetAttributeAsync<string>(customer, RazorpayDefaults.RazorpayOrderCurrencyAttribute, storeId);
            if (!payment.Currency.Equals(serverOrderCurrency, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.CurrencyMismatch"));
                return result;
            }

            if (payment.Status.Equals("captured", StringComparison.OrdinalIgnoreCase))
            {
                result.CaptureTransactionId = paymentId;
                result.NewPaymentStatus = PaymentStatus.Paid;

                await _genericAttributeService.SaveAttributeAsync<string>(customer, RazorpayDefaults.RazorpayOrderIdAttribute, string.Empty, storeId);
                await _genericAttributeService.SaveAttributeAsync<decimal?>(customer, RazorpayDefaults.RazorpayOrderAmountAttribute, null, storeId);
                await _genericAttributeService.SaveAttributeAsync<string>(customer, RazorpayDefaults.RazorpayOrderCurrencyAttribute, string.Empty, storeId);
            }
            else
            {
                result.AddError(string.Format(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.PaymentNotCaptured"), payment.Status));
                result.NewPaymentStatus = PaymentStatus.Pending; // leave as pending or fail
            }
        }
        catch (Exception)
        {
            result.AddError(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.PaymentFetchFailed"));
        }

        return result;
    }

    public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
    {
        return Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });
    }

    public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
    {
        return Task.FromResult(new RefundPaymentResult { Errors = new[] { "Refund method not supported" } });
    }

    public async Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
    {
        var warnings = new List<string>();

        if (string.IsNullOrEmpty(form["RazorpayOrderId"]))
            warnings.Add(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.Fields.OrderId.Required"));
        
        if (string.IsNullOrEmpty(form["RazorpayPaymentId"]))
            warnings.Add(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.Fields.PaymentId.Required"));

        if (string.IsNullOrEmpty(form["RazorpaySignature"]))
            warnings.Add(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.Fields.Signature.Required"));

        return warnings;
    }

    public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
    {
        return Task.FromResult(new VoidPaymentResult { Errors = new[] { "Void method not supported" } });
    }

    public Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
    {
        return Task.CompletedTask;
    }

    public override string GetConfigurationPageUrl()
    {
        return $"{_httpContextAccessor.HttpContext?.Request.PathBase}/Admin/RazorpayPayment/Configure";
    }

    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new RazorpayPaymentSettings
        {
            PaymentCapture = true
        });

        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentMethodDescription", "Pay securely with Razorpay.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeyId", "Key ID");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeyId.Hint", "Enter your Razorpay Key ID.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeySecret", "Key Secret");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeySecret.Hint", "Enter your Razorpay Key Secret. Leave blank to keep the existing secret.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.PaymentCapture", "Auto Capture");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.PaymentCapture.Hint", "Automatically capture payments.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.AdditionalFee", "Additional fee");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.AdditionalFeePercentage", "Additional fee. Use percentage");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Instructions", "Configure your Razorpay settings here. You can find these in your Razorpay Dashboard.");

        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentDetailsMissing", "Missing Razorpay payment details.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.VerificationFailed", "Razorpay signature verification failed.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.OrderMismatch", "Razorpay order ID mismatch.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.AmountMismatch", "Razorpay payment amount mismatch.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentNotCaptured", "Payment not captured. Status: {0}");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentFetchFailed", "Failed to fetch payment status.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.NotConfigured", "Razorpay plugin is not configured.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.EmptyCart", "Cart total is empty.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.UnsupportedCurrency", "Only INR currency is supported.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.OrderCreationFailed", "Failed to create Razorpay order.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeyId.Required", "Key ID is required.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeySecret.Required", "Key Secret is required.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.OrderId.Required", "Razorpay Order ID is missing.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.PaymentId.Required", "Razorpay Payment ID is missing.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.Signature.Required", "Razorpay Signature is missing.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.CurrencyMismatch", "Razorpay currency mismatch.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentInfo.Instructions", "Please click the button below to complete your payment securely via Razorpay.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentInfo.PayButton", "Pay with Razorpay");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentInfo.Processing", "Processing...");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentInfo.Success", "Payment successful! You can now continue the checkout.");

        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        await _settingService.DeleteSettingAsync<RazorpayPaymentSettings>();

        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentMethodDescription");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeyId");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeyId.Hint");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeySecret");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeySecret.Hint");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.PaymentCapture");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.PaymentCapture.Hint");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.AdditionalFee");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.AdditionalFee.Hint");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.AdditionalFeePercentage");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.AdditionalFeePercentage.Hint");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Instructions");

        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentDetailsMissing");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.VerificationFailed");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.OrderMismatch");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.AmountMismatch");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentNotCaptured");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentFetchFailed");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.NotConfigured");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.EmptyCart");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.UnsupportedCurrency");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.OrderCreationFailed");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeyId.Required");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.KeySecret.Required");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.OrderId.Required");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.PaymentId.Required");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.Fields.Signature.Required");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.CurrencyMismatch");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentInfo.Instructions");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentInfo.PayButton");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentInfo.Processing");
        await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Razorpay.PaymentInfo.Success");

        await base.UninstallAsync();
    }
}
