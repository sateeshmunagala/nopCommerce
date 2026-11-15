using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.SinglePageCheckout.Filters;

public class PaymentSelectionDiagnosticFilter : IAsyncActionFilter
{
    private const string RazorpaySystemName = "Payments.Razorpay";
    private const string AttemptCookieName = "spc_checkout_attempt";
    private static readonly HashSet<string> CheckoutActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Index", "OnePageCheckout", "BillingAddress", "SelectBillingAddress", "NewBillingAddress", "ShippingAddress",
        "SelectShippingAddress", "NewShippingAddress", "ShippingMethod", "SelectShippingMethod", "PaymentMethod",
        "SelectPaymentMethod", "PaymentInfo", "EnterPaymentInfo", "Confirm", "ConfirmOrder", "OpcIndex",
        "OpcSaveBilling", "OpcSaveShipping", "OpcSaveShippingMethod", "OpcSavePaymentMethod", "OpcSavePaymentInfo",
        "OpcConfirmOrder", "OpcCompleteRedirectionPayment", "SaveEditBillingAddress", "DeleteEditBillingAddress",
        "SaveEditShippingAddress", "DeleteEditShippingAddress"
    };
    private static readonly HashSet<string> CartActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddProductToCart_Catalog", "AddProductToCart_Details", "UpdateCart", "ChangeVendorCart",
        "ApplyDiscountCoupon", "ApplyGiftCard", "RemoveDiscountCoupon", "RemoveGiftCardCode",
        "ProductDetails_AttributeChange", "CheckoutAttributeChange", "UploadFileCheckoutAttribute", "SelectShippingOption",
        "AddItemsToCartFromWishlist", "MoveToCustomWishlist"
    };

    private readonly ISettingService _settingService;
    private readonly IWorkContext _workContext;
    private readonly IStoreContext _storeContext;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILogger _logger;

    public PaymentSelectionDiagnosticFilter(
        ISettingService settingService,
        IWorkContext workContext,
        IStoreContext storeContext,
        IGenericAttributeService genericAttributeService,
        ILogger logger)
    {
        _settingService = settingService;
        _workContext = workContext;
        _storeContext = storeContext;
        _genericAttributeService = genericAttributeService;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var controller = context.ActionDescriptor.RouteValues.TryGetValue("controller", out var controllerName)
            ? controllerName
            : string.Empty;
        var action = context.ActionDescriptor.RouteValues.TryGetValue("action", out var actionName)
            ? actionName
            : string.Empty;
        var isCheckout = string.Equals(controller, "Checkout", StringComparison.OrdinalIgnoreCase) && CheckoutActions.Contains(action);
        var isCartMutation = string.Equals(controller, "ShoppingCart", StringComparison.OrdinalIgnoreCase) && CartActions.Contains(action);
        var isPluginBoundary = string.Equals(controller, "SinglePageCheckout", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(action, "Index", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(action, "BuyNow", StringComparison.OrdinalIgnoreCase));

        if (!isCheckout && !isCartMutation && !isPluginBoundary)
        {
            await next();
            return;
        }

        var controllerCategory = isCheckout ? "Checkout" : isCartMutation ? "ShoppingCart" : "SinglePageCheckout";
        var actionCategory = isCheckout
            ? CheckoutActions.First(candidate => string.Equals(candidate, action, StringComparison.OrdinalIgnoreCase))
            : isCartMutation
                ? CartActions.First(candidate => string.Equals(candidate, action, StringComparison.OrdinalIgnoreCase))
                : string.Equals(action, "Index", StringComparison.OrdinalIgnoreCase) ? "Index" : "BuyNow";
        var attemptId = GetAttemptId(context.HttpContext.Request.Cookies[AttemptCookieName]);

        SinglePageCheckoutSettings settings;
        try
        {
            settings = await _settingService.LoadSettingAsync<SinglePageCheckoutSettings>();
        }
        catch
        {
            await LogPreparationWarningAsync(controllerCategory, actionCategory, "SettingsReadFailure", 0, 0, attemptId);
            await next();
            return;
        }
        if (!settings.Enabled)
        {
            await next();
            return;
        }

        Customer customer = null;
        int storeId = 0;
        string selectedBefore;
        string attemptedCategory;
        try
        {
            customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            storeId = store?.Id ?? 0;
            selectedBefore = await ReadSelectionStateAsync(customer, storeId);
            attemptedCategory = IsPaymentMethodSave(action, context)
                ? GetSelectionState(context.ActionArguments.TryGetValue("paymentmethod", out var method) ? method as string : null)
                : "not-applicable";
        }
        catch
        {
            await LogPreparationWarningAsync(controllerCategory, actionCategory, "ContextPreparationFailure", customer?.Id ?? 0, storeId, attemptId);
            await next();
            return;
        }
        var customerId = customer?.Id ?? 0;

        var executedContext = await next();
        try
        {
            var selectedAfter = await ReadSelectionStateAsync(customer, storeId);
            var response = GetResponseFlags(executedContext);
            var message = $"ControllerCategory={controllerCategory}; ActionCategory={actionCategory}; StoreId={storeId}; CustomerId={customerId}; AttemptId={attemptId}; Before={selectedBefore}; After={selectedAfter}; AttemptedMethod={attemptedCategory}; Response={response.Outcome}; ErrorFlag={response.Error}; ErrorsFlag={response.Errors}; SuccessFlag={response.Success};";

            if (selectedBefore == "unavailable" || selectedAfter == "unavailable")
            {
                await _logger.WarningAsync($"Single Page Checkout payment selection diagnostic. Category=AttributeReadFailure; {message}");
            }
            else if (IsPaymentMethodSave(action, context))
            {
                if (selectedAfter is "empty" or "unavailable" || response.Error || response.Errors || executedContext.Exception != null)
                    await _logger.WarningAsync($"Single Page Checkout payment method save diagnostic. {message}");
                else
                    await _logger.InformationAsync($"Single Page Checkout payment method save diagnostic. {message}");
            }
            else if (selectedBefore == "razorpay" && selectedAfter == "empty")
            {
                await _logger.WarningAsync($"Single Page Checkout Razorpay selection reset diagnostic. {message}");
            }
            else if (IsPaymentSelectionRequiredBoundary(action) && selectedAfter == "empty")
            {
                await _logger.WarningAsync($"Single Page Checkout missing payment selection diagnostic. {message}");
            }
            else
            {
                await _logger.InformationAsync($"Single Page Checkout payment selection boundary diagnostic. {message}");
            }
        }
        catch
        {
            await LogPreparationWarningAsync(controllerCategory, actionCategory, "ResponseCaptureFailure", customerId, storeId, attemptId);
        }
    }

    private async Task<string> ReadSelectionStateAsync(Customer customer, int storeId)
    {
        try
        {
            if (customer == null)
                return "empty";

            var selection = await _genericAttributeService.GetAttributeAsync<string>(
                customer,
                NopCustomerDefaults.SelectedPaymentMethodAttribute,
                storeId);
            return GetSelectionState(selection);
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string GetSelectionState(string selection)
    {
        if (string.IsNullOrWhiteSpace(selection))
            return "empty";

        return string.Equals(selection, RazorpaySystemName, StringComparison.OrdinalIgnoreCase)
            ? "razorpay"
            : "another";
    }

    private static bool IsPaymentMethodSave(string action, ActionExecutingContext context)
    {
        return action.Equals("OpcSavePaymentMethod", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("SelectPaymentMethod", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("PaymentMethod", StringComparison.OrdinalIgnoreCase) && context.ActionArguments.ContainsKey("paymentmethod");
    }

    private static bool IsPaymentSelectionRequiredBoundary(string action)
    {
        return action.Equals("PaymentInfo", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("EnterPaymentInfo", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("OpcSavePaymentInfo", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("ConfirmOrder", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("OpcConfirmOrder", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAttemptId(string attemptId)
    {
        return !string.IsNullOrEmpty(attemptId) && attemptId.Length <= 16 && attemptId.All(char.IsLetterOrDigit)
            ? attemptId
            : "none";
    }

    private async Task LogPreparationWarningAsync(string controllerCategory, string actionCategory, string category, int customerId, int storeId, string attemptId)
    {
        try
        {
            await _logger.WarningAsync(
                $"Single Page Checkout payment selection diagnostic. Category={category}; ControllerCategory={controllerCategory}; ActionCategory={actionCategory}; StoreId={storeId}; CustomerId={customerId}; AttemptId={attemptId};");
        }
        catch
        {
        }
    }

    private static (string Outcome, bool Error, bool Errors, bool? Success) GetResponseFlags(ActionExecutedContext context)
    {
        var result = context.Result;
        var responseValue = result switch
        {
            JsonResult jsonResult => jsonResult.Value,
            ObjectResult objectResult => objectResult.Value,
            _ => null
        };

        var error = ReadFlag(responseValue, "error", out var errorValue) && HasValue(errorValue);
        var errors = ReadFlag(responseValue, "errors", out var errorsValue) && HasValue(errorsValue);
        bool? success = ReadFlag(responseValue, "success", out var successValue) && successValue is bool successFlag
            ? successFlag
            : null;
        var outcome = context.Exception != null
            ? "exception"
            : result is RedirectResult or RedirectToActionResult or RedirectToRouteResult
                ? "redirect"
                : result?.GetType().Name ?? "none";

        return (outcome, error, errors, success);
    }

    private static bool ReadFlag(object value, string name, out object flagValue)
    {
        flagValue = null;
        if (value == null)
            return false;

        var property = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property == null)
            return false;

        try
        {
            flagValue = property.GetValue(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasValue(object value)
    {
        return value switch
        {
            null => false,
            bool boolean => boolean,
            string text => !string.IsNullOrWhiteSpace(text),
            System.Collections.ICollection collection => collection.Count > 0,
            _ => true
        };
    }
}