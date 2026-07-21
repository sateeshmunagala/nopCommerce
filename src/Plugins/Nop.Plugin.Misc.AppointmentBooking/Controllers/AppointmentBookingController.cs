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
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Services.Vendors;
using Nop.Web.Controllers;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc;
using Nop.Web.Framework.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Nop.Plugin.Misc.AppointmentBooking.Controllers;

/// <summary>
/// Represents appointment booking controller
/// </summary>
public class AppointmentBookingController : BasePublicController
{
    #region Fields

    private readonly AppointmentBookingSettings _appointmentBookingSettings;
    private readonly IAppointmentBookingService _appointmentBookingService;
    private readonly ICustomerService _customerService;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IProductService _productService;
    private readonly ISettingService _settingService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IStoreContext _storeContext;
    private readonly IVendorService _vendorService;
    private readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public AppointmentBookingController(AppointmentBookingSettings appointmentBookingSettings,
        IAppointmentBookingService appointmentBookingService,
        ICustomerService customerService,
        IDateTimeHelper dateTimeHelper,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IProductService productService,
        ISettingService settingService,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        IVendorService vendorService,
        IWorkContext workContext)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _appointmentBookingService = appointmentBookingService;
        _customerService = customerService;
        _dateTimeHelper = dateTimeHelper;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _productService = productService;
        _settingService = settingService;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _vendorService = vendorService;
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

    protected virtual IList<SelectListItem> PrepareDurationOptions(int selectedDuration)
    {
        int[] durations = [15, 30, 45, 60, 90, 120];

        return durations.Select(duration => new SelectListItem
        {
            Text = $"{duration} minutes",
            Value = duration.ToString(),
            Selected = duration == selectedDuration
        }).ToList();
    }

    protected virtual async Task<IList<SelectListItem>> PrepareVendorOptionsAsync(int selectedVendorId)
    {
        var vendors = await _vendorService.GetAllVendorsAsync(showHidden: true);
        var items = vendors.Select(vendor => new SelectListItem
        {
            Text = vendor.Name,
            Value = vendor.Id.ToString(),
            Selected = vendor.Id == selectedVendorId
        }).ToList();

        items.Insert(0, new SelectListItem
        {
            Text = "Select vendor",
            Value = "0",
            Selected = selectedVendorId == 0
        });

        return items;
    }

    protected virtual async Task<string> GetVendorNameAsync(int vendorId)
    {
        var vendor = vendorId > 0 ? await _vendorService.GetVendorByIdAsync(vendorId) : null;
        return vendor == null ? $"Vendor #{vendorId}" : vendor.Name;
    }

    protected virtual async Task<string> GetCustomerDisplayNameAsync(int customerId)
    {
        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        if (customer == null)
            return $"Customer #{customerId}";

        var fullName = await _customerService.GetCustomerFullNameAsync(customer);
        if (!string.IsNullOrWhiteSpace(fullName) && !string.IsNullOrWhiteSpace(customer.Email))
            return $"{fullName} ({customer.Email})";

        return !string.IsNullOrWhiteSpace(customer.Email) ? customer.Email : $"Customer #{customer.Id}";
    }

    protected virtual async Task<string> FormatDateTimeAsync(DateTime dateTimeUtc)
    {
        var userTime = await _dateTimeHelper.ConvertToUserTimeAsync(dateTimeUtc, DateTimeKind.Utc);
        return userTime.ToString("yyyy-MM-dd HH:mm");
    }

    protected virtual async Task<ServiceAdminModel> ToServiceModelAsync(BookableService service)
    {
        return new ServiceAdminModel
        {
            Id = service.Id,
            Name = service.Name,
            Description = service.Description,
            VendorId = service.VendorId,
            VendorName = await GetVendorNameAsync(service.VendorId),
            AvailableVendors = await PrepareVendorOptionsAsync(service.VendorId),
            DurationMinutes = service.DurationMinutes,
            AvailableDurations = PrepareDurationOptions(service.DurationMinutes),
            BufferBeforeMinutes = service.BufferBeforeMinutes,
            BufferAfterMinutes = service.BufferAfterMinutes,
            MinAdvanceBookingHours = service.MinAdvanceBookingHours,
            MaxAdvanceBookingDays = service.MaxAdvanceBookingDays,
            IsActive = service.IsActive,
            DisplayOrder = service.DisplayOrder
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
            DefaultDurationMinutes = _appointmentBookingSettings.DefaultDurationMinutes,
            DefaultMinAdvanceBookingHours = _appointmentBookingSettings.DefaultMinAdvanceBookingHours,
            DefaultMaxAdvanceBookingDays = _appointmentBookingSettings.DefaultMaxAdvanceBookingDays
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
        _appointmentBookingSettings.DefaultDurationMinutes = model.DefaultDurationMinutes;
        _appointmentBookingSettings.DefaultMinAdvanceBookingHours = model.DefaultMinAdvanceBookingHours;
        _appointmentBookingSettings.DefaultMaxAdvanceBookingDays = model.DefaultMaxAdvanceBookingDays;

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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HoldSlot(int productId, int serviceId, DateTime startUtc)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return RedirectToRoute(NopRouteNames.General.HOMEPAGE);

        if (startUtc == default)
        {
            _notificationService.ErrorNotification("Select an appointment time.");
            return RedirectToRoute(AppointmentBookingDefaults.ProductBookingRouteName, new { productId });
        }

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
        var model = new List<ServiceAdminModel>();
        foreach (var service in services)
            model.Add(await ToServiceModelAsync(service));

        return View("~/Plugins/Misc.AppointmentBooking/Views/Admin/Services.cshtml", model);
    }

    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> EditService(int id = 0)
    {
        var service = id > 0 ? await _appointmentBookingService.GetServiceByIdAsync(id) : new BookableService
        {
            DurationMinutes = _appointmentBookingSettings.DefaultDurationMinutes > 0 ? _appointmentBookingSettings.DefaultDurationMinutes : 30,
            MinAdvanceBookingHours = _appointmentBookingSettings.DefaultMinAdvanceBookingHours,
            MaxAdvanceBookingDays = _appointmentBookingSettings.DefaultMaxAdvanceBookingDays > 0 ? _appointmentBookingSettings.DefaultMaxAdvanceBookingDays : 14,
            IsActive = true
        };

        if (service == null)
            return RedirectToAction("Services");

        return View("~/Plugins/Misc.AppointmentBooking/Views/Admin/EditService.cshtml", await ToServiceModelAsync(service));
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

        try
        {
            service = await _appointmentBookingService.SaveServiceAsync(ToServiceEntity(model, service));
        }
        catch (InvalidOperationException exception)
        {
            _notificationService.ErrorNotification(exception.Message);
            return View("~/Plugins/Misc.AppointmentBooking/Views/Admin/EditService.cshtml", await ToServiceModelAsync(service));
        }

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
        var services = (await _appointmentBookingService.GetAllServicesAsync()).ToDictionary(service => service.Id, service => service.Name);
        var products = (await _productService.GetProductsByIdsAsync(bookings.Select(booking => booking.ProductId).Distinct().ToArray()))
            .ToDictionary(product => product.Id, product => product.Name);
        var model = new List<BookingAdminModel>();

        foreach (var booking in bookings)
        {
            model.Add(new BookingAdminModel
            {
                Id = booking.Id,
                ServiceId = booking.ServiceId,
                ServiceName = services.TryGetValue(booking.ServiceId, out var serviceName) ? serviceName : $"Service #{booking.ServiceId}",
                ProductId = booking.ProductId,
                ProductName = products.TryGetValue(booking.ProductId, out var productName) ? productName : $"Product #{booking.ProductId}",
                VendorId = booking.VendorId,
                VendorName = await GetVendorNameAsync(booking.VendorId),
                CustomerId = booking.CustomerId,
                CustomerDisplayName = await GetCustomerDisplayNameAsync(booking.CustomerId),
                OrderId = booking.OrderId,
                OrderDisplayText = booking.OrderId.HasValue ? $"#{booking.OrderId.Value}" : "Not checked out",
                OrderItemId = booking.OrderItemId,
                OrderItemDisplayText = booking.OrderItemId.HasValue ? $"#{booking.OrderItemId.Value}" : "Not checked out",
                StartUtc = booking.StartUtc,
                StartText = await FormatDateTimeAsync(booking.StartUtc),
                EndUtc = booking.EndUtc,
                EndText = await FormatDateTimeAsync(booking.EndUtc),
                Status = booking.Status,
                AttendeeName = booking.AttendeeName,
                AttendeeEmail = booking.AttendeeEmail,
                AttendeePhone = booking.AttendeePhone,
                AttendeeNotes = booking.AttendeeNotes,
                CancellationReason = booking.CancellationReason
            });
        }

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

        var service = await _appointmentBookingService.GetServiceByIdAsync(booking.ServiceId);
        var product = await _productService.GetProductByIdAsync(booking.ProductId);

        return View("~/Plugins/Misc.AppointmentBooking/Views/Admin/BookingDetails.cshtml", new BookingAdminModel
        {
            Id = booking.Id,
            ServiceId = booking.ServiceId,
            ServiceName = service?.Name ?? $"Service #{booking.ServiceId}",
            ProductId = booking.ProductId,
            ProductName = product?.Name ?? $"Product #{booking.ProductId}",
            VendorId = booking.VendorId,
            VendorName = await GetVendorNameAsync(booking.VendorId),
            CustomerId = booking.CustomerId,
            CustomerDisplayName = await GetCustomerDisplayNameAsync(booking.CustomerId),
            OrderId = booking.OrderId,
            OrderDisplayText = booking.OrderId.HasValue ? $"#{booking.OrderId.Value}" : "Not checked out",
            OrderItemId = booking.OrderItemId,
            OrderItemDisplayText = booking.OrderItemId.HasValue ? $"#{booking.OrderItemId.Value}" : "Not checked out",
            StartUtc = booking.StartUtc,
            StartText = await FormatDateTimeAsync(booking.StartUtc),
            EndUtc = booking.EndUtc,
            EndText = await FormatDateTimeAsync(booking.EndUtc),
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
