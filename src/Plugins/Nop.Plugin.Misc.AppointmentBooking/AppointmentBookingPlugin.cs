using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Cms;
using Nop.Plugin.Misc.AppointmentBooking.Components;
using Nop.Plugin.Misc.AppointmentBooking.Domains;
using Nop.Plugin.Misc.AppointmentBooking.Services;
using Nop.Services.Catalog;
using Nop.Services.Cms;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.Vendors;
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
    private readonly IAppointmentBookingService _appointmentBookingService;
    private readonly IProductService _productService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly ISettingService _settingService;
    private readonly IVendorService _vendorService;
    private readonly WidgetSettings _widgetSettings;

    #endregion

    #region Ctor

    public AppointmentBookingPlugin(ILocalizationService localizationService,
        INopUrlHelper nopUrlHelper,
        IAppointmentBookingService appointmentBookingService,
        IProductService productService,
        IProductTemplateService productTemplateService,
        ISettingService settingService,
        IVendorService vendorService,
        WidgetSettings widgetSettings)
    {
        _localizationService = localizationService;
        _nopUrlHelper = nopUrlHelper;
        _appointmentBookingService = appointmentBookingService;
        _productService = productService;
        _productTemplateService = productTemplateService;
        _settingService = settingService;
        _vendorService = vendorService;
        _widgetSettings = widgetSettings;
    }

    #endregion

    #region Methods

    protected virtual async Task EnsureServiceProductTemplateAsync()
    {
        var templates = await _productTemplateService.GetAllProductTemplatesAsync();
        var template = templates.FirstOrDefault(item =>
            string.Equals(item.ViewPath, AppointmentBookingDefaults.ServiceProductTemplateViewPath, StringComparison.OrdinalIgnoreCase)) ??
            templates.FirstOrDefault(item =>
                string.Equals(item.Name, AppointmentBookingDefaults.ServiceProductTemplateName, StringComparison.OrdinalIgnoreCase));

        if (template == null)
        {
            await _productTemplateService.InsertProductTemplateAsync(new ProductTemplate
            {
                Name = AppointmentBookingDefaults.ServiceProductTemplateName,
                ViewPath = AppointmentBookingDefaults.ServiceProductTemplateViewPath,
                DisplayOrder = 22,
                IgnoredProductTypes = ((int)ProductType.GroupedProduct).ToString()
            });
            return;
        }

        var changed = false;
        if (!string.Equals(template.Name, AppointmentBookingDefaults.ServiceProductTemplateName, StringComparison.Ordinal))
        {
            template.Name = AppointmentBookingDefaults.ServiceProductTemplateName;
            changed = true;
        }

        if (!string.Equals(template.ViewPath, AppointmentBookingDefaults.ServiceProductTemplateViewPath, StringComparison.Ordinal))
        {
            template.ViewPath = AppointmentBookingDefaults.ServiceProductTemplateViewPath;
            changed = true;
        }

        if (template.DisplayOrder != 22)
        {
            template.DisplayOrder = 22;
            changed = true;
        }

        var ignoredProductTypes = ((int)ProductType.GroupedProduct).ToString();
        if (!string.Equals(template.IgnoredProductTypes, ignoredProductTypes, StringComparison.Ordinal))
        {
            template.IgnoredProductTypes = ignoredProductTypes;
            changed = true;
        }

        if (changed)
            await _productTemplateService.UpdateProductTemplateAsync(template);
    }

    protected virtual async Task EnsureSampleServicesAsync()
    {
        var vendor = (await _vendorService.GetAllVendorsAsync(showHidden: true)).FirstOrDefault();
        if (vendor == null)
            return;

        var existingServices = await _appointmentBookingService.GetServicesByVendorAsync(vendor.Id);
        if (existingServices.Any(service =>
            string.Equals(service.Name, "Intro Consultation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(service.Name, "Strategy Session", StringComparison.OrdinalIgnoreCase)))
            return;

        var samples = new[]
        {
            new { Name = "Intro Consultation", Description = "A focused introductory appointment.", Duration = 30, Price = 49m },
            new { Name = "Strategy Session", Description = "A longer appointment for planning and advice.", Duration = 60, Price = 99m }
        };

        foreach (var sample in samples)
        {
            var service = await _appointmentBookingService.SaveServiceAsync(new BookableService
            {
                Name = sample.Name,
                Description = sample.Description,
                VendorId = vendor.Id,
                DurationMinutes = sample.Duration,
                MinAdvanceBookingHours = 1,
                MaxAdvanceBookingDays = 30,
                IsActive = true,
                DisplayOrder = 100
            });

            var mapping = await _appointmentBookingService.GetActiveProductMappingByServiceAsync(service.Id);
            var product = mapping?.ProductId > 0 ? await _productService.GetProductByIdAsync(mapping.ProductId) : null;
            if (product != null)
            {
                product.ShortDescription = sample.Description;
                product.Price = sample.Price;
                await _productService.UpdateProductAsync(product);
            }

            foreach (var day in new[] { 1, 2, 3, 4, 5 })
            {
                await _appointmentBookingService.SaveAvailabilityRuleAsync(new AvailabilityRule
                {
                    ServiceId = service.Id,
                    VendorId = vendor.Id,
                    DayOfWeek = day,
                    StartTimeUtc = DateTime.UtcNow.Date.AddHours(9),
                    EndTimeUtc = DateTime.UtcNow.Date.AddHours(17),
                    TimeZoneId = "UTC",
                    IsActive = true
                });
            }
        }
    }

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

        await EnsureServiceProductTemplateAsync();
        await EnsureSampleServicesAsync();

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
    /// Update plugin
    /// </summary>
    /// <param name="currentVersion">Current version of the plugin</param>
    /// <param name="targetVersion">Target version of the plugin</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UpdateAsync(string currentVersion, string targetVersion)
    {
        await EnsureServiceProductTemplateAsync();
        await EnsureSampleServicesAsync();

        await base.UpdateAsync(currentVersion, targetVersion);
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
