using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Http;
using Nop.Plugin.Misc.SinglePageCheckout.Factories;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using System.Linq;
using Nop.Web.Framework;

namespace Nop.Plugin.Misc.SinglePageCheckout.Controllers;

[AutoValidateAntiforgeryToken]
public class SinglePageCheckoutController : BasePluginController
{
    private readonly OrderSettings _orderSettings;
    private readonly IWorkContext _workContext;
    private readonly IStoreContext _storeContext;
    private readonly ISettingService _settingService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly ISinglePageCheckoutPageModelFactory _pageModelFactory;
    private readonly IProductService _productService;
    private readonly INotificationService _notificationService;
    private readonly IProductAttributeParser _productAttributeParser;
    private readonly ILocalizationService _localizationService;
    private readonly ICustomerService _customerService;

    public SinglePageCheckoutController(
        OrderSettings orderSettings,
        IWorkContext workContext,
        IStoreContext storeContext,
        ISettingService settingService,
        IShoppingCartService shoppingCartService,
        ISinglePageCheckoutPageModelFactory pageModelFactory,
        IProductService productService,
        INotificationService notificationService,
        IProductAttributeParser productAttributeParser,
        ILocalizationService localizationService,
        ICustomerService customerService)
    {
        _orderSettings = orderSettings;
        _workContext = workContext;
        _storeContext = storeContext;
        _settingService = settingService;
        _shoppingCartService = shoppingCartService;
        _pageModelFactory = pageModelFactory;
        _productService = productService;
        _notificationService = notificationService;
        _productAttributeParser = productAttributeParser;
        _localizationService = localizationService;
        _customerService = customerService;
    }

    public async Task<IActionResult> Index()
    {
        var settings = await _settingService.LoadSettingAsync<SinglePageCheckoutSettings>();
        if (!settings.Enabled || !_orderSettings.OnePageCheckoutEnabled)
            return RedirectToRoute("Checkout");

        var customer = await _workContext.GetCurrentCustomerAsync();

        // Guest user fallback (same as standard checkout)
        if (!await _customerService.IsRegisteredAsync(customer))
            return Challenge();

        var store = await _storeContext.GetCurrentStoreAsync();
        var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        if (!cart.Any())
            return RedirectToRoute(NopRouteNames.General.CART);

        var model = await _pageModelFactory.PrepareAsync(cart);

        return View("~/Plugins/Misc.SinglePageCheckout/Views/Index.cshtml", model);
    }

    public async Task<IActionResult> Summary()
    {
        var settings = await _settingService.LoadSettingAsync<SinglePageCheckoutSettings>();
        if (!settings.Enabled || !_orderSettings.OnePageCheckoutEnabled)
            return Empty;

        var customer = await _workContext.GetCurrentCustomerAsync();

        if (customer == null || customer.Deleted || !await _customerService.IsRegisteredAsync(customer))
            return Content("");

        var store = await _storeContext.GetCurrentStoreAsync();
        var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        if (cart == null || !cart.Any())
            return Content("");

        var model = await _pageModelFactory.PrepareAsync(cart);

        return PartialView("~/Plugins/Misc.SinglePageCheckout/Views/_SummaryRefresh.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> BuyNow(int productId, IFormCollection form)
    {
        var settings = await _settingService.LoadSettingAsync<SinglePageCheckoutSettings>();
        if (!settings.Enabled || !settings.EnableBuyNow)
            return RedirectToRoute(SinglePageCheckoutDefaults.CheckoutRouteName);

        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null || product.Deleted || !product.Published)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        // Extract quantity
        var quantity = 1;
        if (form.ContainsKey($"addtocart_{productId}.EnteredQuantity"))
        {
            int.TryParse(form[$"addtocart_{productId}.EnteredQuantity"], out quantity);
        }
        quantity = Math.Max(1, quantity);

        var warnings = new List<string>();

        // Extract attributes
        var attributeXml = await _productAttributeParser.ParseProductAttributesAsync(product, form, warnings);

        // Add to cart
        var addWarnings = await _shoppingCartService.AddToCartAsync(
            customer, product, ShoppingCartType.ShoppingCart, store.Id,
            attributeXml, 0, null, null, quantity);

        warnings.AddRange(addWarnings);

        if (warnings.Any())
        {
            foreach (var warning in warnings)
            {
                _notificationService.ErrorNotification(warning);
            }
            return RedirectToRoute(NopRouteNames.General.CART);
        }

        return RedirectToRoute(SinglePageCheckoutDefaults.CheckoutRouteName);
    }
}
