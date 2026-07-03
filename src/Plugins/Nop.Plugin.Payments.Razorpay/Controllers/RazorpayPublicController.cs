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

    public RazorpayPublicController(
        RazorpayHttpClient razorpayHttpClient,
        RazorpayPaymentSettings razorpayPaymentSettings,
        IWorkContext workContext,
        IShoppingCartService shoppingCartService,
        IOrderTotalCalculationService orderTotalCalculationService,
        IStoreContext storeContext,
        ICurrencyService currencyService,
        CurrencySettings currencySettings)
    {
        _razorpayHttpClient = razorpayHttpClient;
        _razorpayPaymentSettings = razorpayPaymentSettings;
        _workContext = workContext;
        _shoppingCartService = shoppingCartService;
        _orderTotalCalculationService = orderTotalCalculationService;
        _storeContext = storeContext;
        _currencyService = currencyService;
        _currencySettings = currencySettings;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder()
    {
        try
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, Nop.Core.Domain.Orders.ShoppingCartType.ShoppingCart, store.Id);
            
            var (shoppingCartTotal, _, _, _, _, _) = await _orderTotalCalculationService.GetShoppingCartTotalAsync(cart);
            if (!shoppingCartTotal.HasValue)
            {
                return Json(new { error = "Cart total is empty" });
            }

            var currency = await _workContext.GetWorkingCurrencyAsync();
            var currencyCode = currency.CurrencyCode;
            
            // Razorpay uses subunit (e.g. paisa for INR). Multiplier is 100 for most currencies.
            var amountInSubunits = shoppingCartTotal.Value * 100;
            
            var receiptId = Guid.NewGuid().ToString("N");

            var razorpayOrderId = await _razorpayHttpClient.CreateOrderAsync(
                _razorpayPaymentSettings.KeyId, 
                _razorpayPaymentSettings.KeySecret, 
                amountInSubunits, 
                currencyCode, 
                receiptId);

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
            return Json(new { error = ex.Message });
        }
    }

    [HttpPost]
    public IActionResult VerifyPayment(string razorpay_payment_id, string razorpay_order_id, string razorpay_signature)
    {
        try
        {
            if (string.IsNullOrEmpty(razorpay_payment_id) || string.IsNullOrEmpty(razorpay_order_id) || string.IsNullOrEmpty(razorpay_signature))
            {
                return Json(new { success = false, error = "Missing payment verification parameters." });
            }

            var isSignatureValid = _razorpayHttpClient.VerifySignature(razorpay_order_id, razorpay_payment_id, razorpay_signature, _razorpayPaymentSettings.KeySecret);

            if (isSignatureValid)
            {
                return Json(new { success = true });
            }
            else
            {
                return Json(new { success = false, error = "Invalid signature." });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }
}
