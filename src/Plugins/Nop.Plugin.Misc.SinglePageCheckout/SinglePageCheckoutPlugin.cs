using Nop.Core.Domain.Cms;
using Nop.Services.Cms;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Misc.SinglePageCheckout;

public class SinglePageCheckoutPlugin : BasePlugin, IMiscPlugin, IWidgetPlugin
{
    private readonly ILocalizationService _localizationService;
    private readonly ISettingService _settingService;
    private readonly WidgetSettings _widgetSettings;

    public SinglePageCheckoutPlugin(
        ILocalizationService localizationService,
        ISettingService settingService,
        WidgetSettings widgetSettings)
    {
        _localizationService = localizationService;
        _settingService = settingService;
        _widgetSettings = widgetSettings;
    }

    public override string GetConfigurationPageUrl()
    {
        return "/Admin/SinglePageCheckoutAdmin/Configure";
    }

    public bool HideInWidgetList => false;

    public Task<IList<string>> GetWidgetZonesAsync()
    {
        return Task.FromResult<IList<string>>(new List<string>
        {
            PublicWidgetZones.ProductDetailsAddInfo,
            PublicWidgetZones.ProductBoxAddinfoAfter
        });
    }

    public Type GetWidgetViewComponent(string widgetZone)
    {
        return Type.GetType("Nop.Plugin.Misc.SinglePageCheckout.Components.SinglePageCheckoutBuyNowViewComponent, Nop.Plugin.Misc.SinglePageCheckout");
    }

    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new SinglePageCheckoutSettings
        {
            Enabled = true,
            ShowCartOnCheckout = true,
            AllowCartItemEditing = true,
            ShowDiscountBox = true,
            ShowGiftCardBox = true,
            ShowCheckoutAttributes = true,
            ShowOrderReviewData = true,
            ShowEstimateShipping = true,
            EnableBuyNow = true,
            ShowBuyNowOnProductDetails = true,
            ShowBuyNowOnProductBoxes = true,
            EnableShipToSameAddressByDefault = true
        });

        if (!_widgetSettings.ActiveWidgetSystemNames.Contains(SinglePageCheckoutDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Add(SinglePageCheckoutDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Misc.SinglePageCheckout.BuyNow.Button", "Buy Now");

        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        await _settingService.DeleteSettingAsync<SinglePageCheckoutSettings>();

        if (_widgetSettings.ActiveWidgetSystemNames.Contains(SinglePageCheckoutDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Remove(SinglePageCheckoutDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.SinglePageCheckout");

        await base.UninstallAsync();
    }
}
