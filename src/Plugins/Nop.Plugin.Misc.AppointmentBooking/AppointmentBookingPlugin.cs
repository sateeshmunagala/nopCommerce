using Nop.Core.Domain.Cms;
using Nop.Plugin.Misc.AppointmentBooking.Components;
using Nop.Services.Cms;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Web.Framework.Infrastructure;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.AppointmentBooking;

/// <summary>
/// Represents appointment booking plugin
/// </summary>
public class AppointmentBookingPlugin : BasePlugin, IMiscPlugin, IWidgetPlugin
{
    #region Fields

    private readonly ILocalizationService _localizationService;
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly ISettingService _settingService;
    private readonly WidgetSettings _widgetSettings;

    #endregion

    #region Ctor

    public AppointmentBookingPlugin(ILocalizationService localizationService,
        INopUrlHelper nopUrlHelper,
        ISettingService settingService,
        WidgetSettings widgetSettings)
    {
        _localizationService = localizationService;
        _nopUrlHelper = nopUrlHelper;
        _settingService = settingService;
        _widgetSettings = widgetSettings;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return _nopUrlHelper.RouteUrl(AppointmentBookingDefaults.ConfigurationRouteName);
    }

    /// <summary>
    /// Gets widget zones where this widget should be rendered
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the widget zones
    /// </returns>
    public Task<IList<string>> GetWidgetZonesAsync()
    {
        return Task.FromResult<IList<string>>(new List<string>
        {
            PublicWidgetZones.ProductDetailsOverviewBottom,
            PublicWidgetZones.AccountNavigationAfter
        });
    }

    /// <summary>
    /// Gets a type of a view component for displaying widget
    /// </summary>
    /// <param name="widgetZone">Name of widget zone</param>
    /// <returns>View component type</returns>
    public Type GetWidgetViewComponent(string widgetZone)
    {
        ArgumentNullException.ThrowIfNull(widgetZone);

        if (widgetZone.Equals(PublicWidgetZones.ProductDetailsOverviewBottom))
            return typeof(ProductAppointmentBookingViewComponent);

        if (widgetZone.Equals(PublicWidgetZones.AccountNavigationAfter))
            return typeof(AccountServicesNavigationViewComponent);

        return null;
    }

    /// <summary>
    /// Install plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new AppointmentBookingSettings
        {
            Enabled = true,
            DefaultDurationMinutes = 30,
            DefaultMinAdvanceBookingHours = 1,
            DefaultMaxAdvanceBookingDays = 14
        });

        if (!_widgetSettings.ActiveWidgetSystemNames.Contains(AppointmentBookingDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Add(AppointmentBookingDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Misc.AppointmentBooking.Enabled"] = "Enabled",
            ["Plugins.Misc.AppointmentBooking.Enabled.Hint"] = "Check to enable appointment booking surfaces.",
            ["Plugins.Misc.AppointmentBooking.DefaultDurationMinutes"] = "Default duration",
            ["Plugins.Misc.AppointmentBooking.DefaultDurationMinutes.Hint"] = "Enter the default appointment duration in minutes.",
            ["Plugins.Misc.AppointmentBooking.DefaultMinAdvanceBookingHours"] = "Minimum advance booking hours",
            ["Plugins.Misc.AppointmentBooking.DefaultMinAdvanceBookingHours.Hint"] = "Enter the default minimum time before a customer can book a slot.",
            ["Plugins.Misc.AppointmentBooking.DefaultMaxAdvanceBookingDays"] = "Maximum advance booking days",
            ["Plugins.Misc.AppointmentBooking.DefaultMaxAdvanceBookingDays.Hint"] = "Enter the default number of days ahead that customers can book.",
            ["Plugins.Misc.AppointmentBooking.Configuration.Saved"] = "Appointment booking settings have been saved.",
            ["Plugins.Misc.AppointmentBooking.Appointments"] = "Appointments",
            ["Plugins.Misc.AppointmentBooking.Services"] = "Appointment services",
            ["Plugins.Misc.AppointmentBooking.Bookings"] = "Appointment bookings",
            ["Plugins.Misc.AppointmentBooking.Account.Service.Title"] = "Title",
            ["Plugins.Misc.AppointmentBooking.Account.Service.ShortDescription"] = "Short description",
            ["Plugins.Misc.AppointmentBooking.Account.Service.Price"] = "Price",
            ["Plugins.Misc.AppointmentBooking.Account.Service.DurationMinutes"] = "Duration",
            ["Plugins.Misc.AppointmentBooking.Account.Service.Description"] = "Service description",
            ["Plugins.Misc.AppointmentBooking.Account.Service.MappedProduct"] = "Mapped product",
            ["Plugins.Misc.AppointmentBooking.Account.Service.IsPublic"] = "Visible"
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        if (_widgetSettings.ActiveWidgetSystemNames.Contains(AppointmentBookingDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Remove(AppointmentBookingDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        await _settingService.DeleteSettingAsync<AppointmentBookingSettings>();
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.AppointmentBooking");

        await base.UninstallAsync();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets a value indicating whether to hide this plugin on the widget list page in the admin area
    /// </summary>
    public bool HideInWidgetList => true;

    #endregion
}
