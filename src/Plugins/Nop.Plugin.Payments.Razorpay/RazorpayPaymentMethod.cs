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
            result.AddError("Missing Razorpay payment details.");
            return result;
        }

        var isSignatureValid = _razorpayHttpClient.VerifySignature(orderId, paymentId, signature, _razorpayPaymentSettings.KeySecret);

        if (!isSignatureValid)
        {
            result.AddError("Razorpay signature verification failed.");
            return result;
        }

        try
        {
            var paymentStatus = await _razorpayHttpClient.GetPaymentStatusAsync(_razorpayPaymentSettings.KeyId, _razorpayPaymentSettings.KeySecret, paymentId);

            if (paymentStatus.Equals("captured", StringComparison.OrdinalIgnoreCase) || paymentStatus.Equals("authorized", StringComparison.OrdinalIgnoreCase))
            {
                result.CaptureTransactionId = paymentId;
                result.NewPaymentStatus = PaymentStatus.Paid;
            }
            else
            {
                result.AddError($"Payment not captured. Status: {paymentStatus}");
            }
        }
        catch (Exception ex)
        {
            result.AddError($"Failed to fetch payment status: {ex.Message}");
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

        await base.UninstallAsync();
    }
}
