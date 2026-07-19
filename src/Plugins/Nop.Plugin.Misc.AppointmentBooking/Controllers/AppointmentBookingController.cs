using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Http;
using Nop.Plugin.Misc.AppointmentBooking.Domains;
using Nop.Plugin.Misc.AppointmentBooking.Models;
using Nop.Plugin.Misc.AppointmentBooking.Models.Admin;
using Nop.Plugin.Misc.AppointmentBooking.Services;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Controllers;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.AppointmentBooking.Controllers;

/// <summary>
/// Represents appointment booking controller
/// </summary>
public class AppointmentBookingController : BasePublicController
{
    #region Fields

    private readonly AppointmentBookingSettings _appointmentBookingSettings;
    private readonly IAppointmentBookingService _appointmentBookingService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IProductService _productService;
    private readonly ISettingService _settingService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public AppointmentBookingController(AppointmentBookingSettings appointmentBookingSettings,
        IAppointmentBookingService appointmentBookingService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IProductService productService,
        ISettingService settingService,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        IWorkContext workContext)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _appointmentBookingService = appointmentBookingService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _productService = productService;
        _settingService = settingService;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _workContext = workContext;
    }

    #endregion

    #region Utilities

    protected virtual DateTime ParseTimeForDate(DateTime dateUtc, string time)
    {
        return TimeSpan.TryParse(time, out var parsedTime)
            ? dateUtc.Date.Add(parsedTime)
            : dateUtc.Date;
    }

    protected virtual ServiceAdminModel ToServiceModel(BookableService service, int mappedProductId = 0)
    {
        return new ServiceAdminModel
        {
            Id = service.Id,
            Name = service.Name,
            Description = service.Description,
            VendorId = service.VendorId,
            DurationMinutes = service.DurationMinutes,
            BufferBeforeMinutes = service.BufferBeforeMinutes,
            BufferAfterMinutes = service.BufferAfterMinutes,
            MinAdvanceBookingHours = service.MinAdvanceBookingHours,
            MaxAdvanceBookingDays = service.MaxAdvanceBookingDays,
            IsActive = service.IsActive,
            DisplayOrder = service.DisplayOrder,
            MappedProductId = mappedProductId
        };
    }

    protected virtual BookableService ToServiceEntity(ServiceAdminModel model, BookableService service = null)
    {
        service ??= new BookableService();
        service.Name = model.Name;
        service.Description = model.Description;
        service.VendorId = model.VendorId;
        service.DurationMinutes = model.DurationMinutes;
        service.BufferBeforeMinutes = model.BufferBeforeMinutes;
        service.BufferAfterMinutes = model.BufferAfterMinutes;
        service.MinAdvanceBookingHours = model.MinAdvanceBookingHours;
        service.MaxAdvanceBookingDays = model.MaxAdvanceBookingDays;
        service.IsActive = model.IsActive;
        service.DisplayOrder = model.DisplayOrder;

        return service;
    }

    #endregion

    #region Methods

    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public IActionResult Configure()
    {
        var model = new ConfigurationModel
        {
            Enabled = _appointmentBookingSettings.Enabled,
            DefaultBookingUrl = _appointmentBookingSettings.DefaultBookingUrl,
            DefaultDurationMinutes = _appointmentBookingSettings.DefaultDurationMinutes,
            AllowCalendarIframe = _appointmentBookingSettings.AllowCalendarIframe,
            CalendarProvider = _appointmentBookingSettings.CalendarProvider
        };

        return View("~/Plugins/Misc.AppointmentBooking/Views/Configure.cshtml", model);
    }

    [HttpPost, ActionName("Configure")]
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [FormValueRequired("save")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return Configure();

        _appointmentBookingSettings.Enabled = model.Enabled;
        _appointmentBookingSettings.DefaultBookingUrl = model.DefaultBookingUrl?.Trim() ?? string.Empty;
        _appointmentBookingSettings.DefaultDurationMinutes = model.DefaultDurationMinutes;
        _appointmentBookingSettings.AllowCalendarIframe = model.AllowCalendarIframe;
        _appointmentBookingSettings.CalendarProvider = model.CalendarProvider?.Trim() ?? string.Empty;

        await _settingService.SaveSettingAsync(_appointmentBookingSettings);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AppointmentBooking.Configuration.Saved"));

        return Configure();
    }

    public async Task<IActionResult> ProductBooking(int productId)
    {
        if (!await _appointmentBookingService.IsAppointmentProductAsync(productId))
            return InvokeHttp404();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var model = await _appointmentBookingService.PrepareProductBookingModelAsync(productId, customer?.Id ?? 0);
        if (model == null)
            return InvokeHttp404();

        return View("~/Plugins/Misc.AppointmentBooking/Views/AppointmentBooking/ProductBooking.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> HoldSlot(int productId, int serviceId, DateTime startUtc)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return RedirectToRoute(NopRouteNames.General.HOMEPAGE);

        var hold = await _appointmentBookingService.CreateTimeSlotHoldAsync(serviceId, productId, customer.Id, startUtc);
        if (hold == null)
        {
            _notificationService.ErrorNotification("The selected appointment slot is no longer available.");
            return RedirectToAction("ProductBooking", new { productId });
        }

        var store = await _storeContext.GetCurrentStoreAsync();
        var warnings = await _shoppingCartService.AddToCartAsync(customer, product, ShoppingCartType.ShoppingCart, store.Id, quantity: 1);
        if (warnings.Any())
        {
            _notificationService.ErrorNotification(string.Join(" ", warnings));
            return RedirectToAction("ProductBooking", new { productId });
        }

        _notificationService.SuccessNotification("Appointment slot held for checkout.");
        return RedirectToRoute(NopRouteNames.General.CART);
    }

    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Services()
    {
        var services = await _appointmentBookingService.GetAllServicesAsync();
        return View("~/Plugins/Misc.AppointmentBooking/Views/Admin/Services.cshtml", services.Select(service => ToServiceModel(service)).ToList());
    }

    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> EditService(int id = 0)
    {
        var service = id > 0 ? await _appointmentBookingService.GetServiceByIdAsync(id) : new BookableService
        {
            DurationMinutes = _appointmentBookingSettings.DefaultDurationMinutes > 0 ? _appointmentBookingSettings.DefaultDurationMinutes : 30,
            MaxAdvanceBookingDays = 14,
            IsActive = true
        };

        if (service == null)
            return RedirectToAction("Services");

        return View("~/Plugins/Misc.AppointmentBooking/Views/Admin/EditService.cshtml", ToServiceModel(service));
    }

    [HttpPost, ActionName("EditService")]
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [FormValueRequired("save")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> EditService(ServiceAdminModel model)
    {
        var service = model.Id > 0 ? await _appointmentBookingService.GetServiceByIdAsync(model.Id) : new BookableService();
        if (service == null)
            return RedirectToAction("Services");

        service = await _appointmentBookingService.SaveServiceAsync(ToServiceEntity(model, service));
        if (model.MappedProductId > 0)
            await _appointmentBookingService.MapServiceToProductAsync(service.Id, model.MappedProductId, service.VendorId);

        _notificationService.SuccessNotification("Appointment service saved.");
        return RedirectToAction("EditService", new { id = service.Id });
    }

    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Availability(int serviceId)
    {
        var service = await _appointmentBookingService.GetServiceByIdAsync(serviceId);
        if (service == null)
            return RedirectToAction("Services");

        ViewBag.Service = service;
        ViewBag.Rules = await _appointmentBookingService.GetAvailabilityRulesAsync(serviceId);
        ViewBag.Exceptions = await _appointmentBookingService.GetAvailabilityExceptionsAsync(serviceId);
        ViewBag.Questions = await _appointmentBookingService.GetServiceQuestionsAsync(serviceId);

        return View("~/Plugins/Misc.AppointmentBooking/Views/Admin/Availability.cshtml");
    }

    [HttpPost]
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> AddAvailabilityRule(AvailabilityRuleAdminModel model)
    {
        await _appointmentBookingService.SaveAvailabilityRuleAsync(new AvailabilityRule
        {
            ServiceId = model.ServiceId,
            VendorId = model.VendorId,
            DayOfWeek = model.DayOfWeek,
            StartTimeUtc = ParseTimeForDate(DateTime.UtcNow, model.StartTime),
            EndTimeUtc = ParseTimeForDate(DateTime.UtcNow, model.EndTime),
            TimeZoneId = model.TimeZoneId ?? "UTC",
            IsActive = model.IsActive
        });

        return RedirectToAction("Availability", new { serviceId = model.ServiceId });
    }

    [HttpPost]
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> AddAvailabilityException(AvailabilityExceptionAdminModel model)
    {
        await _appointmentBookingService.SaveAvailabilityExceptionAsync(new AvailabilityException
        {
            ServiceId = model.ServiceId,
            VendorId = model.VendorId,
            ExceptionDateUtc = model.ExceptionDateUtc.Date,
            StartTimeUtc = ParseTimeForDate(model.ExceptionDateUtc, model.StartTime),
            EndTimeUtc = ParseTimeForDate(model.ExceptionDateUtc, model.EndTime),
            IsAvailable = model.IsAvailable,
            Reason = model.Reason
        });

        return RedirectToAction("Availability", new { serviceId = model.ServiceId });
    }

    [HttpPost]
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> AddServiceQuestion(ServiceQuestionAdminModel model)
    {
        await _appointmentBookingService.SaveServiceQuestionAsync(new ServiceQuestion
        {
            ServiceId = model.ServiceId,
            QuestionText = model.QuestionText,
            QuestionType = model.QuestionType ?? "Text",
            IsRequired = model.IsRequired,
            DisplayOrder = model.DisplayOrder,
            OptionsJson = model.OptionsJson
        });

        return RedirectToAction("Availability", new { serviceId = model.ServiceId });
    }

    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Bookings()
    {
        var bookings = await _appointmentBookingService.GetAllBookingsAsync();
        var model = bookings.Select(booking => new BookingAdminModel
        {
            Id = booking.Id,
            ServiceId = booking.ServiceId,
            ProductId = booking.ProductId,
            VendorId = booking.VendorId,
            CustomerId = booking.CustomerId,
            OrderId = booking.OrderId,
            OrderItemId = booking.OrderItemId,
            StartUtc = booking.StartUtc,
            EndUtc = booking.EndUtc,
            Status = booking.Status,
            AttendeeName = booking.AttendeeName,
            AttendeeEmail = booking.AttendeeEmail,
            AttendeePhone = booking.AttendeePhone,
            AttendeeNotes = booking.AttendeeNotes,
            CancellationReason = booking.CancellationReason
        }).ToList();

        return View("~/Plugins/Misc.AppointmentBooking/Views/Admin/Bookings.cshtml", model);
    }

    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> BookingDetails(int id)
    {
        var booking = await _appointmentBookingService.GetBookingByIdAsync(id);
        if (booking == null)
            return RedirectToAction("Bookings");

        return View("~/Plugins/Misc.AppointmentBooking/Views/Admin/BookingDetails.cshtml", new BookingAdminModel
        {
            Id = booking.Id,
            ServiceId = booking.ServiceId,
            ProductId = booking.ProductId,
            VendorId = booking.VendorId,
            CustomerId = booking.CustomerId,
            OrderId = booking.OrderId,
            OrderItemId = booking.OrderItemId,
            StartUtc = booking.StartUtc,
            EndUtc = booking.EndUtc,
            Status = booking.Status,
            AttendeeName = booking.AttendeeName,
            AttendeeEmail = booking.AttendeeEmail,
            AttendeePhone = booking.AttendeePhone,
            AttendeeNotes = booking.AttendeeNotes,
            CancellationReason = booking.CancellationReason
        });
    }

    #endregion
}
