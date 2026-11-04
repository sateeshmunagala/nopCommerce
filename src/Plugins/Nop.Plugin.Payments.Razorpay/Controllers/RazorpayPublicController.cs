using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Payments.Razorpay.Services;
using Nop.Services.Common;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Web.Controllers;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.Razorpay.Controllers;

[AutoValidateAntiforgeryToken]
public class RazorpayPublicController : BasePublicController
{
    private const int MaxPaymentFailureDescriptionLength = 400;
    private const string PopupSuccessHandlerEntryEvent = "RazorpayPopupSuccessHandlerEntry";
    private const string VerifyPaymentResponseSuccessEvent = "RazorpayVerifyPaymentResponseSuccess";
    private const string PaymentInfoSaveInvokedEvent = "RazorpayPaymentInfoSaveInvoked";
    private const string PaymentInfoAjaxJsonErrorEvent = "RazorpayPaymentInfoAjaxJsonError";
    private const string PaymentInfoAjaxConfirmSectionSuccessEvent = "RazorpayPaymentInfoAjaxConfirmSectionSuccess";
    private const string PaymentInfoAjaxUnexpectedResponseEvent = "RazorpayPaymentInfoAjaxUnexpectedResponse";
    private const string PaymentInfoAjaxTransportFailureEvent = "RazorpayPaymentInfoAjaxTransportFailure";
    private static readonly Regex PaymentFailureSensitiveFieldRegex = new(
        @"(?i)\b(?<key>authorization|api[_ -]?key|token|secret|password|signature|credential|email|e-mail|phone|mobile|contact|name|address|card(?:[_ -]?(?:number|no))?|cvv|cvc|otp|upi|vpa|account(?:[_ -]?(?:number|no))?|customer(?:[_ -]?(?:id|identifier))?|payment(?:[_ -]?(?:id|identifier|reference))?|transaction(?:[_ -]?(?:id|reference))?|order(?:[_ -]?id)?)\b[""']?\s*[:=]\s*[""']?[^,;|}\]]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PaymentFailureEmailOrVpaRegex = new(
        @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+(?:\.[A-Z]{2,})?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PaymentFailureNumberRegex = new(
        @"(?<!\w)\+?\d(?:[\s().-]*\d){6,}(?!\w)",
        RegexOptions.Compiled);
    private static readonly Regex PaymentFailureCredentialRegex = new(
        @"(?i)\bBearer\s+[A-Z0-9._~+/-]+=*|\b(?:rzp_(?:live|test)_|sk_(?:live|test)_)[A-Z0-9]+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PaymentFailureUrlRegex = new(
        @"(?i)https?://\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PaymentFailureWhitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled);

    private readonly RazorpayHttpClient _razorpayHttpClient;
    private readonly RazorpayPaymentSettings _razorpayPaymentSettings;
    private readonly IWorkContext _workContext;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IOrderTotalCalculationService _orderTotalCalculationService;
    private readonly IStoreContext _storeContext;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger _logger;
    private readonly ICurrencyService _currencyService;

    private static bool IsRazorpayCheckoutDiagnosticEvent(string error)
    {
        return error is PopupSuccessHandlerEntryEvent or
            VerifyPaymentResponseSuccessEvent or
            PaymentInfoSaveInvokedEvent or
            PaymentInfoAjaxJsonErrorEvent or
            PaymentInfoAjaxConfirmSectionSuccessEvent or
            PaymentInfoAjaxUnexpectedResponseEvent or
            PaymentInfoAjaxTransportFailureEvent;
    }

    private static bool IsPaymentInfoDiagnosticError(string error)
    {
        return error is PaymentInfoAjaxJsonErrorEvent or
            PaymentInfoAjaxUnexpectedResponseEvent or
            PaymentInfoAjaxTransportFailureEvent;
    }

    private static string GetSelectedPaymentMethodState(string selectedPaymentMethod)
    {
        if (string.IsNullOrWhiteSpace(selectedPaymentMethod))
            return "empty";

        return string.Equals(selectedPaymentMethod, RazorpayDefaults.SystemName, StringComparison.OrdinalIgnoreCase)
            ? "razorpay"
            : "another";
    }

    private static string MaskRazorpayOrderId(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return "none";

        var trimmedOrderId = orderId.Trim();
        return trimmedOrderId.Length <= 4
            ? "***"
            : $"***{trimmedOrderId[^4..]}";
    }

    private async Task LogPaymentMethodSnapshotAsync(Customer customer, int storeId, string stage, string razorpayOrderId)
    {
        string selectedPaymentMethod;
        try
        {
            selectedPaymentMethod = await _genericAttributeService.GetAttributeAsync<string>(
                customer,
                NopCustomerDefaults.SelectedPaymentMethodAttribute,
                storeId);
        }
        catch (Exception exception)
        {
            await LogPaymentMethodSnapshotFailureAsync(customer, storeId, stage, "read", exception);
            return;
        }

        try
        {
            var selectedMethodState = GetSelectedPaymentMethodState(selectedPaymentMethod);
            var logMessage = $"Stage={stage}; StoreId={storeId}; CustomerId={customer?.Id ?? 0}; SelectedMethodState={selectedMethodState}; RazorpayOrderId={MaskRazorpayOrderId(razorpayOrderId)};";

            if (selectedMethodState == "razorpay")
                await _logger.InformationAsync($"Razorpay checkout payment method snapshot. {logMessage}");
            else
                await _logger.WarningAsync($"Razorpay checkout payment method snapshot. {logMessage}");
        }
        catch (Exception exception)
        {
            await LogPaymentMethodSnapshotFailureAsync(customer, storeId, stage, "write", exception);
        }
    }

    private async Task LogPaymentMethodSnapshotFailureAsync(Customer customer, int storeId, string stage, string operation, Exception exception)
    {
        try
        {
            if (_logger == null)
                return;

            await _logger.WarningAsync(
                $"Razorpay checkout payment method snapshot failure. Stage={stage}; StoreId={storeId}; CustomerId={customer?.Id ?? 0}; Operation={operation}; FailureType={exception?.GetType().Name ?? "Unknown"};");
        }
        catch
        {
        }
    }

    private static string NormalizeExistingClientErrorCategory(string error)
    {
        return error switch
        {
            "JQueryMissing" => "JQueryMissing",
            "PayButtonMissing" => "PayButtonMissing",
            "CreateOrderMissingData" => "CreateOrderMissingData",
            "ModalDismissed" => "ModalDismissed",
            "PaymentFailed" => "PaymentFailed",
            "RazorpayOpenException" => "RazorpayOpenException",
            "RazorpaySDKLoadFailed" => "RazorpaySDKLoadFailed",
            "CreateOrderException" => "CreateOrderException",
            "VerifyPaymentFailed" => "VerifyPaymentFailed",
            "VerifyPaymentException" => "VerifyPaymentException",
            _ => "OtherClientDiagnostic"
        };
    }

    private static string SanitizePaymentFailureDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "DescriptionMissing";

        var boundedInput = description.Length > 2000 ? description[..2000] : description;
        var cleaned = new StringBuilder(boundedInput.Length);
        foreach (var character in boundedInput)
            cleaned.Append(char.IsControl(character) ? ' ' : character);

        var sanitized = PaymentFailureWhitespaceRegex.Replace(cleaned.ToString(), " ").Trim();
        sanitized = PaymentFailureSensitiveFieldRegex.Replace(
            sanitized,
            match => $"{match.Groups["key"].Value}=[redacted]");
        sanitized = PaymentFailureEmailOrVpaRegex.Replace(sanitized, "[redacted-identifier]");
        sanitized = PaymentFailureNumberRegex.Replace(sanitized, "[redacted-number]");
        sanitized = PaymentFailureCredentialRegex.Replace(sanitized, "[redacted-credential]");
        sanitized = PaymentFailureUrlRegex.Replace(sanitized, "[redacted-url]");
        sanitized = PaymentFailureWhitespaceRegex.Replace(sanitized, " ").Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
            return "DescriptionMissing";

        return sanitized.Length > MaxPaymentFailureDescriptionLength
            ? sanitized[..MaxPaymentFailureDescriptionLength]
            : sanitized;
    }

    public RazorpayPublicController(
        RazorpayHttpClient razorpayHttpClient,
        RazorpayPaymentSettings razorpayPaymentSettings,
        IWorkContext workContext,
        IShoppingCartService shoppingCartService,
        IOrderTotalCalculationService orderTotalCalculationService,
        IStoreContext storeContext,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        ILogger logger,
        ICurrencyService currencyService)
    {
        _razorpayHttpClient = razorpayHttpClient;
        _razorpayPaymentSettings = razorpayPaymentSettings;
        _workContext = workContext;
        _shoppingCartService = shoppingCartService;
        _orderTotalCalculationService = orderTotalCalculationService;
        _storeContext = storeContext;
        _genericAttributeService = genericAttributeService;
        _localizationService = localizationService;
        _logger = logger;
        _currencyService = currencyService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        try
        {
            if (string.IsNullOrEmpty(_razorpayPaymentSettings.KeyId) || string.IsNullOrEmpty(_razorpayPaymentSettings.KeySecret))
            {
                await _logger.WarningAsync($"Razorpay CreateOrder: Plugin is not configured. CustomerId: {customer.Id}, StoreId: {store.Id}");
                return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.NotConfigured") });
            }

            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, Nop.Core.Domain.Orders.ShoppingCartType.ShoppingCart, store.Id);
            
            var (shoppingCartTotal, _, _, _, _, _) = await _orderTotalCalculationService.GetShoppingCartTotalAsync(cart);
            if (!shoppingCartTotal.HasValue || shoppingCartTotal.Value <= 0)
            {
                await _logger.ErrorAsync($"Razorpay CreateOrder: Empty cart for CustomerId: {customer.Id}, StoreId: {store.Id}");
                return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.EmptyCart") });
            }

            var currency = await _workContext.GetWorkingCurrencyAsync();
            var currencyCode = currency.CurrencyCode;

            // 1. ALLOW BOTH INR AND USD
            var allowedCurrencies = new[] { "INR", "USD" };
            if (!allowedCurrencies.Contains(currencyCode, StringComparer.OrdinalIgnoreCase))
            {
                await _logger.ErrorAsync($"Razorpay CreateOrder: Unsupported currency '{currencyCode}' for CustomerId: {customer.Id}, StoreId: {store.Id}");
                return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.UnsupportedCurrency") });
            }

            var workingCurrency = await _workContext.GetWorkingCurrencyAsync();

            // Convert the cart total from primary currency (INR) to user's working currency (USD or INR)
            decimal amountInWorkingCurrency = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(
                shoppingCartTotal.Value,
                workingCurrency
            );

            // Convert to whole-number subunits for Razorpay (cents for USD, paise for INR)
            var amountInSubunits = (long)Math.Round(amountInWorkingCurrency * 100, 0);

            var receiptId = Guid.NewGuid().ToString("N");

            var razorpayOrderId = await _razorpayHttpClient.CreateOrderAsync(
                _razorpayPaymentSettings.KeyId, 
                _razorpayPaymentSettings.KeySecret, 
                amountInSubunits, 
                currencyCode, 
                receiptId,
                _razorpayPaymentSettings.PaymentCapture);

            await _genericAttributeService.SaveAttributeAsync(customer, RazorpayDefaults.RazorpayOrderIdAttribute, razorpayOrderId, store.Id);
            await _genericAttributeService.SaveAttributeAsync(customer, RazorpayDefaults.RazorpayOrderAmountAttribute, amountInSubunits, store.Id);
            await _genericAttributeService.SaveAttributeAsync(customer, RazorpayDefaults.RazorpayOrderCurrencyAttribute, currencyCode, store.Id);

            await LogPaymentMethodSnapshotAsync(customer, store.Id, "CreateOrderResponse", razorpayOrderId);

            return Json(new 
            { 
                keyId = _razorpayPaymentSettings.KeyId,
                orderId = razorpayOrderId,
                amount = amountInSubunits,
                currency = currencyCode,
                name = store.Name,
                description = "Order Payment",
                prefill = new
                {
                    name = $"{customer.FirstName} {customer.LastName}".Trim(),
                    email = customer.Email,
                    contact = customer.Phone // Try to prefill if available
                }
            });
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Razorpay CreateOrder failed. CustomerId: {customer.Id}, StoreId: {store.Id}", ex);
            return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.OrderCreationFailed") });
        }
    }

    [HttpPost]
    public async Task<IActionResult> VerifyPayment(string razorpay_payment_id, string razorpay_order_id, string razorpay_signature)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        try
        {
            await LogPaymentMethodSnapshotAsync(customer, store.Id, "VerifyPaymentEntry", razorpay_order_id);

            if (string.IsNullOrEmpty(razorpay_payment_id) || string.IsNullOrEmpty(razorpay_order_id) || string.IsNullOrEmpty(razorpay_signature))
            {
                await _logger.WarningAsync($"Razorpay VerifyPayment: Missing payment details. CustomerId: {customer.Id}, StoreId: {store.Id}");
                return Json(new { success = false, error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.PaymentDetailsMissing") });
            }

            var serverOrderId = await _genericAttributeService.GetAttributeAsync<string>(customer, RazorpayDefaults.RazorpayOrderIdAttribute, store.Id);

            if (string.IsNullOrEmpty(serverOrderId) || !serverOrderId.Equals(razorpay_order_id, StringComparison.OrdinalIgnoreCase))
            {
                await _logger.WarningAsync($"Razorpay VerifyPayment: Order mismatch. ServerOrderId: {serverOrderId}, ClientOrderId: {razorpay_order_id}, CustomerId: {customer.Id}, StoreId: {store.Id}");
                return Json(new { success = false, error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.OrderMismatch") });
            }

            var isSignatureValid = _razorpayHttpClient.VerifySignature(razorpay_order_id, razorpay_payment_id, razorpay_signature, _razorpayPaymentSettings.KeySecret);

            if (!isSignatureValid)
            {
                await _logger.WarningAsync($"Razorpay VerifyPayment: Signature verification failed. RazorpayOrderId: {razorpay_order_id}, PaymentId: {razorpay_payment_id}, CustomerId: {customer.Id}, StoreId: {store.Id}");
                return Json(new { success = false, error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.VerificationFailed") });
            }

            var payment = await _razorpayHttpClient.GetPaymentAsync(
                _razorpayPaymentSettings.KeyId, _razorpayPaymentSettings.KeySecret, razorpay_payment_id);

            if (!payment.OrderId.Equals(razorpay_order_id, StringComparison.OrdinalIgnoreCase))
            {
                await _logger.WarningAsync($"Razorpay VerifyPayment: Payment order mismatch. PaymentOrderId: {payment.OrderId}, ClientOrderId: {razorpay_order_id}, CustomerId: {customer.Id}, StoreId: {store.Id}");
                return Json(new { success = false, error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.OrderMismatch") });
            }

            var serverOrderAmount = await _genericAttributeService.GetAttributeAsync<decimal>(customer, RazorpayDefaults.RazorpayOrderAmountAttribute, store.Id);
            if (payment.Amount != serverOrderAmount)
            {
                await _logger.WarningAsync($"Razorpay VerifyPayment: Amount mismatch. PaymentAmount: {payment.Amount}, ServerAmount: {serverOrderAmount}, CustomerId: {customer.Id}, StoreId: {store.Id}");
                return Json(new { success = false, error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.AmountMismatch") });
            }

            var serverOrderCurrency = await _genericAttributeService.GetAttributeAsync<string>(customer, RazorpayDefaults.RazorpayOrderCurrencyAttribute, store.Id);
            if (!payment.Currency.Equals(serverOrderCurrency, StringComparison.OrdinalIgnoreCase))
            {
                await _logger.WarningAsync($"Razorpay VerifyPayment: Currency mismatch. PaymentCurrency: {payment.Currency}, ServerCurrency: {serverOrderCurrency}, CustomerId: {customer.Id}, StoreId: {store.Id}");
                return Json(new { success = false, error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.CurrencyMismatch") });
            }

            if (!payment.Status.Equals("captured", StringComparison.OrdinalIgnoreCase))
            {
                await _logger.WarningAsync($"Razorpay VerifyPayment: Payment not captured. Status: {payment.Status}, PaymentId: {razorpay_payment_id}, CustomerId: {customer.Id}, StoreId: {store.Id}");
                var errorMsg = string.Format(await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.PaymentNotCaptured"), payment.Status);
                return Json(new { success = false, error = errorMsg });
            }

            await LogPaymentMethodSnapshotAsync(customer, store.Id, "VerifyPaymentSuccessResponse", razorpay_order_id);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Razorpay VerifyPayment failed. RazorpayOrderId: {razorpay_order_id}, PaymentId: {razorpay_payment_id}, CustomerId: {customer.Id}, StoreId: {store.Id}", ex);
            return Json(new { success = false, error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.PaymentFetchFailed") });
        }
    }

    [HttpPost]
    public async Task<IActionResult> LogClientError(string error, string context)
    {
        try
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            if (IsRazorpayCheckoutDiagnosticEvent(error))
            {
                var selectedPaymentMethod = await _genericAttributeService.GetAttributeAsync<string>(
                    customer,
                    NopCustomerDefaults.SelectedPaymentMethodAttribute,
                    store.Id);
                var selectedMethodState = GetSelectedPaymentMethodState(selectedPaymentMethod);
                var razorpayOrderId = await _genericAttributeService.GetAttributeAsync<string>(
                    customer,
                    RazorpayDefaults.RazorpayOrderIdAttribute,
                    store.Id);
                var loadWaitingState = error == PaymentInfoSaveInvokedEvent
                    ? context switch
                    {
                        "CheckoutLoadWaitingState=active" => "active",
                        "CheckoutLoadWaitingState=false" => "false",
                        _ => "unknown"
                    }
                    : "not-reported";
                var diagnosticMessage = $"Stage={error}; StoreId={store.Id}; CustomerId={customer.Id}; SelectedMethodState={selectedMethodState}; RazorpayOrderId={MaskRazorpayOrderId(razorpayOrderId)}; ClientReportedCheckoutLoadWaitingActive={loadWaitingState};";

                if (IsPaymentInfoDiagnosticError(error) || selectedMethodState != "razorpay")
                    await _logger.WarningAsync($"Razorpay checkout payment selection diagnostic. {diagnosticMessage}");
                else
                    await _logger.InformationAsync($"Razorpay checkout payment selection diagnostic. {diagnosticMessage}");

                return Json(new { success = true });
            }

            var safeError = NormalizeExistingClientErrorCategory(error);
            var safeContext = safeError == "PaymentFailed"
                ? SanitizePaymentFailureDescription(context)
                : "DetailsOmitted";
            var clientErrorMessage = $"Razorpay Client Error: {safeError}. Context={safeContext}; CustomerId: {customer.Id}, StoreId: {store.Id}";
            await _logger.WarningAsync(clientErrorMessage);

            return Json(new { success = true });
        }
        catch
        {
            // Do not fail if logging fails
            return Json(new { success = false });
        }
    }
}
