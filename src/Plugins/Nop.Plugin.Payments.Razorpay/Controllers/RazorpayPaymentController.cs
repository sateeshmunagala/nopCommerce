using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Http;
using Nop.Plugin.Payments.Razorpay.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.Razorpay.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class RazorpayPaymentController : BasePaymentController
{
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IPermissionService _permissionService;
    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;

    public RazorpayPaymentController(
        ILocalizationService localizationService,
        INotificationService notificationService,
        IPermissionService permissionService,
        ISettingService settingService,
        IStoreContext storeContext)
    {
        _localizationService = localizationService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _settingService = settingService;
        _storeContext = storeContext;
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public async Task<IActionResult> Configure()
    {
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var razorpayPaymentSettings = await _settingService.LoadSettingAsync<RazorpayPaymentSettings>(storeScope);

        var model = new ConfigurationModel
        {
            KeyId = razorpayPaymentSettings.KeyId,
            PaymentCapture = razorpayPaymentSettings.PaymentCapture,
            AdditionalFee = razorpayPaymentSettings.AdditionalFee,
            AdditionalFeePercentage = razorpayPaymentSettings.AdditionalFeePercentage,
            ActiveStoreScopeConfiguration = storeScope
        };

        if (storeScope > 0)
        {
            model.KeyId_OverrideForStore = await _settingService.SettingExistsAsync(razorpayPaymentSettings, x => x.KeyId, storeScope);
            model.KeySecret_OverrideForStore = await _settingService.SettingExistsAsync(razorpayPaymentSettings, x => x.KeySecret, storeScope);
            model.PaymentCapture_OverrideForStore = await _settingService.SettingExistsAsync(razorpayPaymentSettings, x => x.PaymentCapture, storeScope);
            model.AdditionalFee_OverrideForStore = await _settingService.SettingExistsAsync(razorpayPaymentSettings, x => x.AdditionalFee, storeScope);
            model.AdditionalFeePercentage_OverrideForStore = await _settingService.SettingExistsAsync(razorpayPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
        }

        return View("~/Plugins/Payments.Razorpay/Views/Configure.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var razorpayPaymentSettings = await _settingService.LoadSettingAsync<RazorpayPaymentSettings>(storeScope);

        razorpayPaymentSettings.KeyId = model.KeyId;
        
        if (!string.IsNullOrWhiteSpace(model.KeySecret))
        {
            razorpayPaymentSettings.KeySecret = model.KeySecret;
        }

        razorpayPaymentSettings.PaymentCapture = model.PaymentCapture;
        razorpayPaymentSettings.AdditionalFee = model.AdditionalFee;
        razorpayPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

        await _settingService.SaveSettingOverridablePerStoreAsync(razorpayPaymentSettings, x => x.KeyId, model.KeyId_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(razorpayPaymentSettings, x => x.KeySecret, model.KeySecret_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(razorpayPaymentSettings, x => x.PaymentCapture, model.PaymentCapture_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(razorpayPaymentSettings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(razorpayPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);

        await _settingService.ClearCacheAsync();

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }
}
