using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Payments.Razorpay.Services;
using Nop.Services.Common;
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
    private readonly RazorpayHttpClient _razorpayHttpClient;
    private readonly RazorpayPaymentSettings _razorpayPaymentSettings;
    private readonly IWorkContext _workContext;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IOrderTotalCalculationService _orderTotalCalculationService;
    private readonly IStoreContext _storeContext;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger _logger;

    public RazorpayPublicController(
        RazorpayHttpClient razorpayHttpClient,
        RazorpayPaymentSettings razorpayPaymentSettings,
        IWorkContext workContext,
        IShoppingCartService shoppingCartService,
        IOrderTotalCalculationService orderTotalCalculationService,
        IStoreContext storeContext,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        ILogger logger)
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
                await _logger.WarningAsync($"Razorpay CreateOrder: Empty cart for CustomerId: {customer.Id}, StoreId: {store.Id}");
                return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.EmptyCart") });
            }

            var currency = await _workContext.GetWorkingCurrencyAsync();
            var currencyCode = currency.CurrencyCode;
            
            if (!currencyCode.Equals("INR", StringComparison.OrdinalIgnoreCase))
            {
                await _logger.WarningAsync($"Razorpay CreateOrder: Unsupported currency '{currencyCode}' for CustomerId: {customer.Id}, StoreId: {store.Id}");
                return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.UnsupportedCurrency") });
            }

            // Razorpay uses subunit (e.g. paisa for INR). Multiplier is 100 for most currencies.
            var amountInSubunits = shoppingCartTotal.Value * 100;
            
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

            return Json(new 
            { 
                keyId = _razorpayPaymentSettings.KeyId,
                orderId = razorpayOrderId,
                amount = Math.Round(amountInSubunits, 0),
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

            var safeError = (error?.Length > 400) == true ? error.Substring(0, 400) : error;
            var safeContext = (context?.Length > 400) == true ? context.Substring(0, 400) : context;

            var message = $"Razorpay Client Error: {safeError}. Context: {safeContext}. CustomerId: {customer.Id}, StoreId: {store.Id}";
            await _logger.WarningAsync(message);

            return Json(new { success = true });
        }
        catch
        {
            // Do not fail if logging fails
            return Json(new { success = false });
        }
    }
}
