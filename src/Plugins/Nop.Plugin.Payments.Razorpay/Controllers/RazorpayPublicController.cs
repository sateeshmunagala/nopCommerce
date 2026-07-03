using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Http;
using Nop.Plugin.Payments.Razorpay.Services;
using Nop.Services.Directory;
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
    private readonly ICurrencyService _currencyService;
    private readonly CurrencySettings _currencySettings;

    private readonly Nop.Services.Localization.ILocalizationService _localizationService;

    public RazorpayPublicController(
        RazorpayHttpClient razorpayHttpClient,
        RazorpayPaymentSettings razorpayPaymentSettings,
        IWorkContext workContext,
        IShoppingCartService shoppingCartService,
        IOrderTotalCalculationService orderTotalCalculationService,
        IStoreContext storeContext,
        ICurrencyService currencyService,
        CurrencySettings currencySettings,
        Nop.Services.Localization.ILocalizationService localizationService)
    {
        _razorpayHttpClient = razorpayHttpClient;
        _razorpayPaymentSettings = razorpayPaymentSettings;
        _workContext = workContext;
        _shoppingCartService = shoppingCartService;
        _orderTotalCalculationService = orderTotalCalculationService;
        _storeContext = storeContext;
        _currencyService = currencyService;
        _currencySettings = currencySettings;
        _localizationService = localizationService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder()
    {
        try
        {
            if (string.IsNullOrEmpty(_razorpayPaymentSettings.KeyId) || string.IsNullOrEmpty(_razorpayPaymentSettings.KeySecret))
            {
                return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.NotConfigured") });
            }

            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, Nop.Core.Domain.Orders.ShoppingCartType.ShoppingCart, store.Id);
            
            var (shoppingCartTotal, _, _, _, _, _) = await _orderTotalCalculationService.GetShoppingCartTotalAsync(cart);
            if (!shoppingCartTotal.HasValue || shoppingCartTotal.Value <= 0)
            {
                return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.EmptyCart") });
            }

            var currency = await _workContext.GetWorkingCurrencyAsync();
            var currencyCode = currency.CurrencyCode;
            
            if (!currencyCode.Equals("INR", StringComparison.OrdinalIgnoreCase))
            {
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
        catch (Exception)
        {
            return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.OrderCreationFailed") });
        }
    }

    [HttpPost]
    public async Task<IActionResult> VerifyPayment(string razorpay_payment_id, string razorpay_order_id, string razorpay_signature)
    {
        try
        {
            if (string.IsNullOrEmpty(razorpay_payment_id) || string.IsNullOrEmpty(razorpay_order_id) || string.IsNullOrEmpty(razorpay_signature))
            {
                return Json(new { success = false, error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.PaymentDetailsMissing") });
            }

            var isSignatureValid = _razorpayHttpClient.VerifySignature(razorpay_order_id, razorpay_payment_id, razorpay_signature, _razorpayPaymentSettings.KeySecret);

            if (isSignatureValid)
            {
                return Json(new { success = true });
            }
            else
            {
                return Json(new { success = false, error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.VerificationFailed") });
            }
        }
        catch (Exception)
        {
            return Json(new { success = false, error = await _localizationService.GetResourceAsync("Plugins.Payments.Razorpay.PaymentFetchFailed") });
        }
    }
}
