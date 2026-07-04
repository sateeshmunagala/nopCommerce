using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Configuration;
using Nop.Plugin.Misc.SinglePageCheckout.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.SinglePageCheckout.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class SinglePageCheckoutAdminController : BasePluginController
{
    private readonly INotificationService _notificationService;
    private readonly ILocalizationService _localizationService;
    private readonly IPermissionService _permissionService;
    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;

    public SinglePageCheckoutAdminController(
        INotificationService notificationService,
        ILocalizationService localizationService,
        IPermissionService permissionService,
        ISettingService settingService,
        IStoreContext storeContext)
    {
        _notificationService = notificationService;
        _localizationService = localizationService;
        _permissionService = permissionService;
        _settingService = settingService;
        _storeContext = storeContext;
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure()
    {
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<SinglePageCheckoutSettings>(storeScope);

        var model = new ConfigurationModel
        {
            ActiveStoreScopeConfiguration = storeScope,
            Enabled = settings.Enabled,
            BypassCart = settings.BypassCart,
            ShowCartOnCheckout = settings.ShowCartOnCheckout,
            AllowCartItemEditing = settings.AllowCartItemEditing,
            ShowDiscountBox = settings.ShowDiscountBox,
            ShowGiftCardBox = settings.ShowGiftCardBox,
            ShowCheckoutAttributes = settings.ShowCheckoutAttributes,
            ShowOrderReviewData = settings.ShowOrderReviewData,
            ShowEstimateShipping = settings.ShowEstimateShipping,
            EnableBuyNow = settings.EnableBuyNow,
            ShowBuyNowOnProductDetails = settings.ShowBuyNowOnProductDetails,
            ShowBuyNowOnProductBoxes = settings.ShowBuyNowOnProductBoxes,
            PreselectDefaultCustomerAddress = settings.PreselectDefaultCustomerAddress,
            PreselectLastCustomerBillingAddress = settings.PreselectLastCustomerBillingAddress,
            PreselectLastCustomerShippingAddress = settings.PreselectLastCustomerShippingAddress,
            EnableShipToSameAddressByDefault = settings.EnableShipToSameAddressByDefault,
            DefaultBillingCountryId = settings.DefaultBillingCountryId
        };

        return View("~/Plugins/Misc.SinglePageCheckout/Views/Configure.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<SinglePageCheckoutSettings>(storeScope);

        settings.Enabled = model.Enabled;
        settings.BypassCart = model.BypassCart;
        settings.ShowCartOnCheckout = model.ShowCartOnCheckout;
        settings.AllowCartItemEditing = model.AllowCartItemEditing;
        settings.ShowDiscountBox = model.ShowDiscountBox;
        settings.ShowGiftCardBox = model.ShowGiftCardBox;
        settings.ShowCheckoutAttributes = model.ShowCheckoutAttributes;
        settings.ShowOrderReviewData = model.ShowOrderReviewData;
        settings.ShowEstimateShipping = model.ShowEstimateShipping;
        settings.EnableBuyNow = model.EnableBuyNow;
        settings.ShowBuyNowOnProductDetails = model.ShowBuyNowOnProductDetails;
        settings.ShowBuyNowOnProductBoxes = model.ShowBuyNowOnProductBoxes;
        settings.PreselectDefaultCustomerAddress = model.PreselectDefaultCustomerAddress;
        settings.PreselectLastCustomerBillingAddress = model.PreselectLastCustomerBillingAddress;
        settings.PreselectLastCustomerShippingAddress = model.PreselectLastCustomerShippingAddress;
        settings.EnableShipToSameAddressByDefault = model.EnableShipToSameAddressByDefault;
        settings.DefaultBillingCountryId = model.DefaultBillingCountryId;

        await _settingService.SaveSettingAsync(settings, storeScope);

        await _settingService.ClearCacheAsync();

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }
}
