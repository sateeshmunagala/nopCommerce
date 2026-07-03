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

namespace Nop.Plugin.Payments.Razorpay;

public class RazorpayPaymentMethod : BasePlugin, IPaymentMethod
{
    private readonly ISettingService _settingService;
    private readonly RazorpayPaymentSettings _razorpayPaymentSettings;
    private readonly ILocalizationService _localizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly RazorpayHttpClient _razorpayHttpClient;
    private readonly IOrderTotalCalculationService _orderTotalCalculationService;

    public RazorpayPaymentMethod(
        ISettingService settingService,
        RazorpayPaymentSettings razorpayPaymentSettings,
        ILocalizationService localizationService,
        IHttpContextAccessor httpContextAccessor,
        RazorpayHttpClient razorpayHttpClient,
        IOrderTotalCalculationService orderTotalCalculationService)
    {
        _settingService = settingService;
        _razorpayPaymentSettings = razorpayPaymentSettings;
        _localizationService = localizationService;
        _httpContextAccessor = httpContextAccessor;
        _razorpayHttpClient = razorpayHttpClient;
        _orderTotalCalculationService = orderTotalCalculationService;
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

            // Note: Currency validation is limited here because ProcessPaymentRequest doesn't provide
            // direct access to the currency code, but amount and order ownership are strictly verified.
            var expectedAmountInSubunits = Math.Round(processPaymentRequest.OrderTotal * 100, 0);
            if (payment.Amount != expectedAmountInSubunits)
            {
                result.AddError(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.AmountMismatch"));
                return result;
            }

            if (payment.Status.Equals("captured", StringComparison.OrdinalIgnoreCase))
            {
                result.CaptureTransactionId = paymentId;
                result.NewPaymentStatus = PaymentStatus.Paid;
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

    public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
    {
        var warnings = new List<string>();

        if (string.IsNullOrEmpty(form["RazorpayOrderId"]))
            warnings.Add("Razorpay Order ID is missing.");
        
        if (string.IsNullOrEmpty(form["RazorpayPaymentId"]))
            warnings.Add("Razorpay Payment ID is missing.");

        if (string.IsNullOrEmpty(form["RazorpaySignature"]))
            warnings.Add("Razorpay Signature is missing.");

        return Task.FromResult<IList<string>>(warnings);
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

        await base.UninstallAsync();
    }
}
