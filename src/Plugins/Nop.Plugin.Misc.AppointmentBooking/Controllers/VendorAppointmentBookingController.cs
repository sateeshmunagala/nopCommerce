using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Plugin.Misc.AppointmentBooking.Domains;
using Nop.Plugin.Misc.AppointmentBooking.Models.Account;
using Nop.Plugin.Misc.AppointmentBooking.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Messages;
using Nop.Web.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.AppointmentBooking.Controllers;

/// <summary>
/// Represents vendor self-service appointment booking controller
/// </summary>
[AutoValidateAntiforgeryToken]
public class VendorAppointmentBookingController : BasePublicController
{
    private readonly AppointmentBookingSettings _appointmentBookingSettings;
    private readonly IAppointmentBookingService _appointmentBookingService;
    private readonly ICustomerService _customerService;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly INotificationService _notificationService;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IProductService _productService;
    private readonly IWorkContext _workContext;

    public VendorAppointmentBookingController(AppointmentBookingSettings appointmentBookingSettings,
        IAppointmentBookingService appointmentBookingService,
        ICustomerService customerService,
        IDateTimeHelper dateTimeHelper,
        INotificationService notificationService,
        IPriceFormatter priceFormatter,
        IProductService productService,
        IWorkContext workContext)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _appointmentBookingService = appointmentBookingService;
        _customerService = customerService;
        _dateTimeHelper = dateTimeHelper;
        _notificationService = notificationService;
        _priceFormatter = priceFormatter;
        _productService = productService;
        _workContext = workContext;
    }

    protected virtual DateTime ParseTimeForDate(DateTime dateUtc, string time)
    {
        return TimeSpan.TryParse(time, out var parsedTime)
            ? dateUtc.Date.Add(parsedTime)
            : dateUtc.Date;
    }

    protected virtual async Task<int> GetCurrentVendorIdAsync()
    {
        var vendor = await _workContext.GetCurrentVendorAsync();
        return vendor?.Id ?? 0;
    }

    protected virtual async Task<BookableService> GetVendorServiceAsync(int serviceId, int vendorId)
    {
        var service = await _appointmentBookingService.GetServiceByIdAsync(serviceId);
        return service?.VendorId == vendorId ? service : null;
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

    protected virtual IList<SelectListItem> PrepareTimeOptions()
    {
        var options = new List<SelectListItem>();
        for (var time = TimeSpan.Zero; time < TimeSpan.FromDays(1); time = time.Add(TimeSpan.FromMinutes(15)))
        {
            var value = time.ToString(@"hh\:mm");
            options.Add(new SelectListItem { Text = value, Value = value });
        }

        return options;
    }

    protected virtual async Task<string> FormatDateTimeAsync(DateTime dateTimeUtc)
    {
        var userTime = await _dateTimeHelper.ConvertToUserTimeAsync(dateTimeUtc, DateTimeKind.Utc);
        return userTime.ToString("yyyy-MM-dd HH:mm");
    }

    protected virtual async Task<string> PrepareCustomerDisplayNameAsync(int customerId)
    {
        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        if (customer == null)
            return $"Customer #{customerId}";

        var fullName = await _customerService.GetCustomerFullNameAsync(customer);
        if (!string.IsNullOrWhiteSpace(fullName) && !string.IsNullOrWhiteSpace(customer.Email))
            return $"{fullName} ({customer.Email})";

        return !string.IsNullOrWhiteSpace(customer.Email) ? customer.Email : $"Customer #{customer.Id}";
    }

    protected virtual async Task<AvailabilityModel> PrepareAvailabilityModelAsync(int vendorId)
    {
        var services = (await _appointmentBookingService.GetServicesByVendorAsync(vendorId))
            .Where(service => service.IsActive)
            .ToList();
        var firstService = services.FirstOrDefault();
        var rules = firstService == null
            ? new List<AvailabilityRule>()
            : (await _appointmentBookingService.GetAvailabilityRulesAsync(firstService.Id)).ToList();
        var exceptions = new List<AvailabilityException>();

        foreach (var service in services)
            exceptions.AddRange((await _appointmentBookingService.GetAvailabilityExceptionsAsync(service.Id))
                .Where(exception => !exception.IsAvailable));

        var schedule = new List<ScheduleDayModel>();
        for (var day = 0; day < 7; day++)
        {
            var rule = rules.FirstOrDefault(item => item.DayOfWeek == day && item.IsActive);
            schedule.Add(new ScheduleDayModel
            {
                DayOfWeek = day,
                Enabled = rule != null,
                StartTime = rule?.StartTimeUtc.ToString("HH:mm") ?? "09:00",
                EndTime = rule?.EndTimeUtc.ToString("HH:mm") ?? "17:00"
            });
        }

        var blockedDates = exceptions
            .GroupBy(exception => exception.ExceptionDateUtc.Date)
            .OrderBy(group => group.Key)
            .Select(group => new BlockedDateModel
            {
                Date = group.Key,
                DateText = group.Key.ToString("yyyy-MM-dd"),
                Reason = group.Select(exception => exception.Reason).FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason))
            })
            .ToList();

        return new AvailabilityModel
        {
            VendorId = vendorId,
            ServiceId = firstService?.Id ?? 0,
            ServiceName = firstService?.Name,
            ActiveServiceCount = services.Count,
            Rules = rules,
            Exceptions = exceptions,
            Schedule = schedule,
            TimeOptions = PrepareTimeOptions(),
            BlockedDates = blockedDates,
            StartTime = "09:00",
            EndTime = "17:00",
            IsActive = true
        };
    }

    protected virtual async Task<EditServiceModel> PrepareEditServiceModelAsync(BookableService service, int vendorId)
    {
        var mapping = service.Id > 0 ? await _appointmentBookingService.GetActiveProductMappingByServiceAsync(service.Id) : null;
        var mappedProduct = mapping?.ProductId > 0 ? await _productService.GetProductByIdAsync(mapping.ProductId) : null;

        return new EditServiceModel
        {
            Id = service.Id,
            Title = service.Name,
            ShortDescription = mappedProduct?.ShortDescription,
            Price = mappedProduct?.Price ?? decimal.Zero,
            DurationMinutes = service.DurationMinutes > 0 ? service.DurationMinutes : (_appointmentBookingSettings.DefaultDurationMinutes > 0 ? _appointmentBookingSettings.DefaultDurationMinutes : 30),
            AvailableDurations = PrepareDurationOptions(service.DurationMinutes > 0 ? service.DurationMinutes : (_appointmentBookingSettings.DefaultDurationMinutes > 0 ? _appointmentBookingSettings.DefaultDurationMinutes : 30)),
            ServiceDescription = service.Description,
            IsPublic = service.Id == 0 || service.IsActive
        };
    }

    protected virtual BookableService ApplyServiceModel(EditServiceModel model, BookableService service, int vendorId)
    {
        service.Name = model.Title?.Trim();
        service.Description = model.ServiceDescription?.Trim();
        service.VendorId = vendorId;
        service.DurationMinutes = model.DurationMinutes > 0 ? model.DurationMinutes : (_appointmentBookingSettings.DefaultDurationMinutes > 0 ? _appointmentBookingSettings.DefaultDurationMinutes : 30);
        service.MinAdvanceBookingHours = service.MinAdvanceBookingHours > 0 ? service.MinAdvanceBookingHours : _appointmentBookingSettings.DefaultMinAdvanceBookingHours;
        service.MaxAdvanceBookingDays = service.MaxAdvanceBookingDays > 0 ? service.MaxAdvanceBookingDays : (_appointmentBookingSettings.DefaultMaxAdvanceBookingDays > 0 ? _appointmentBookingSettings.DefaultMaxAdvanceBookingDays : 14);
        service.IsActive = model.IsPublic;

        return service;
    }

    public async Task<IActionResult> Services()
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var services = await _appointmentBookingService.GetServicesByVendorAsync(vendorId);
        var model = new List<ServiceListItemModel>();

        foreach (var service in services)
        {
            var mapping = await _appointmentBookingService.GetActiveProductMappingByServiceAsync(service.Id);
            var product = mapping?.ProductId > 0 ? await _productService.GetProductByIdAsync(mapping.ProductId) : null;

            model.Add(new ServiceListItemModel
            {
                Id = service.Id,
                Title = service.Name,
                Description = service.Description,
                DurationMinutes = service.DurationMinutes,
                Price = product != null && product.Price > decimal.Zero ? await _priceFormatter.FormatPriceAsync(product.Price) : string.Empty,
                IsPublic = service.IsActive
            });
        }

        return View("~/Plugins/Misc.AppointmentBooking/Views/Account/Services.cshtml", model);
    }

    public async Task<IActionResult> CreateService()
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var service = new BookableService
        {
            DurationMinutes = _appointmentBookingSettings.DefaultDurationMinutes > 0 ? _appointmentBookingSettings.DefaultDurationMinutes : 30,
            MinAdvanceBookingHours = _appointmentBookingSettings.DefaultMinAdvanceBookingHours,
            MaxAdvanceBookingDays = _appointmentBookingSettings.DefaultMaxAdvanceBookingDays > 0 ? _appointmentBookingSettings.DefaultMaxAdvanceBookingDays : 14,
            IsActive = true
        };

        return View("~/Plugins/Misc.AppointmentBooking/Views/Account/EditService.cshtml", await PrepareEditServiceModelAsync(service, vendorId));
    }

    [HttpPost]
    public async Task<IActionResult> CreateService(EditServiceModel model)
    {
        return await SaveServiceAsync(model, 0);
    }

    public async Task<IActionResult> EditService(int id)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var service = await GetVendorServiceAsync(id, vendorId);
        if (service == null)
            return InvokeHttp404();

        return View("~/Plugins/Misc.AppointmentBooking/Views/Account/EditService.cshtml", await PrepareEditServiceModelAsync(service, vendorId));
    }

    [HttpPost]
    public async Task<IActionResult> EditService(EditServiceModel model)
    {
        return await SaveServiceAsync(model, model.Id);
    }

    protected virtual async Task<IActionResult> SaveServiceAsync(EditServiceModel model, int serviceId)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        if (string.IsNullOrWhiteSpace(model.Title))
            ModelState.AddModelError(nameof(model.Title), "Title is required.");

        if (model.DurationMinutes <= 0)
            ModelState.AddModelError(nameof(model.DurationMinutes), "Duration must be greater than zero.");

        var service = serviceId > 0 ? await GetVendorServiceAsync(serviceId, vendorId) : new BookableService();
        if (service == null)
            return InvokeHttp404();

        if (!ModelState.IsValid)
        {
            var invalidModel = await PrepareEditServiceModelAsync(service, vendorId);
            invalidModel.Title = model.Title;
            invalidModel.ShortDescription = model.ShortDescription;
            invalidModel.Price = model.Price;
            invalidModel.DurationMinutes = model.DurationMinutes;
            invalidModel.AvailableDurations = PrepareDurationOptions(model.DurationMinutes);
            invalidModel.ServiceDescription = model.ServiceDescription;
            invalidModel.IsPublic = model.IsPublic;
            return View("~/Plugins/Misc.AppointmentBooking/Views/Account/EditService.cshtml", invalidModel);
        }

        service = await _appointmentBookingService.SaveServiceAsync(ApplyServiceModel(model, service, vendorId));

        var mapping = await _appointmentBookingService.GetActiveProductMappingByServiceAsync(service.Id);
        var mappedProduct = mapping?.ProductId > 0 ? await _productService.GetProductByIdAsync(mapping.ProductId) : null;
        if (mappedProduct != null)
        {
            mappedProduct.ShortDescription = model.ShortDescription?.Trim();
            mappedProduct.Price = model.Price;
            await _productService.UpdateProductAsync(mappedProduct);
        }

        _notificationService.SuccessNotification("Service saved.");
        return RedirectToRoute(AppointmentBookingDefaults.AccountEditServiceRouteName, new { id = service.Id });
    }

    public async Task<IActionResult> Availability(int id)
    {
        return await AccountAvailability();
    }

    public async Task<IActionResult> AccountAvailability(int serviceId = 0)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        return View("~/Plugins/Misc.AppointmentBooking/Views/Account/Availability.cshtml", await PrepareAvailabilityModelAsync(vendorId));
    }

    [HttpPost]
    public async Task<IActionResult> AddAvailabilityRule(AvailabilityModel model)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var service = await GetVendorServiceAsync(model.ServiceId, vendorId);
        if (service == null)
            return InvokeHttp404();

        await _appointmentBookingService.SaveAvailabilityRuleAsync(new AvailabilityRule
        {
            ServiceId = service.Id,
            VendorId = vendorId,
            DayOfWeek = model.DayOfWeek,
            StartTimeUtc = ParseTimeForDate(DateTime.UtcNow, model.StartTime),
            EndTimeUtc = ParseTimeForDate(DateTime.UtcNow, model.EndTime),
            TimeZoneId = string.IsNullOrWhiteSpace(model.TimeZoneId) ? "UTC" : model.TimeZoneId.Trim(),
            IsActive = model.IsActive
        });

        _notificationService.SuccessNotification("Availability rule saved.");
        return RedirectToRoute(AppointmentBookingDefaults.AccountServiceAvailabilityRouteName, new { id = service.Id });
    }

    [HttpPost]
    public async Task<IActionResult> SaveAvailabilitySchedule(AvailabilityModel model)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var services = (await _appointmentBookingService.GetServicesByVendorAsync(vendorId))
            .Where(service => service.IsActive)
            .ToList();

        if (!services.Any())
        {
            _notificationService.ErrorNotification("Create an active service before saving a schedule.");
            return RedirectToRoute(AppointmentBookingDefaults.AccountAvailabilityRouteName);
        }

        foreach (var service in services)
        {
            await _appointmentBookingService.DeleteAvailabilityRulesAsync(service.Id);

            foreach (var day in model.Schedule.Where(day => day.Enabled))
            {
                await _appointmentBookingService.SaveAvailabilityRuleAsync(new AvailabilityRule
                {
                    ServiceId = service.Id,
                    VendorId = vendorId,
                    DayOfWeek = day.DayOfWeek,
                    StartTimeUtc = ParseTimeForDate(DateTime.UtcNow, day.StartTime),
                    EndTimeUtc = ParseTimeForDate(DateTime.UtcNow, day.EndTime),
                    TimeZoneId = string.IsNullOrWhiteSpace(model.TimeZoneId) ? "UTC" : model.TimeZoneId.Trim(),
                    IsActive = true
                });
            }
        }

        _notificationService.SuccessNotification("Availability schedule saved.");
        return RedirectToRoute(AppointmentBookingDefaults.AccountAvailabilityRouteName);
    }

    [HttpPost]
    public async Task<IActionResult> AddAvailabilityException(AvailabilityModel model)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var service = await GetVendorServiceAsync(model.ServiceId, vendorId);
        if (service == null)
            return InvokeHttp404();

        await _appointmentBookingService.SaveAvailabilityExceptionAsync(new AvailabilityException
        {
            ServiceId = service.Id,
            VendorId = vendorId,
            ExceptionDateUtc = model.ExceptionDateUtc.Date,
            StartTimeUtc = ParseTimeForDate(model.ExceptionDateUtc, model.StartTime),
            EndTimeUtc = ParseTimeForDate(model.ExceptionDateUtc, model.EndTime),
            IsAvailable = model.IsAvailable,
            Reason = model.Reason?.Trim()
        });

        _notificationService.SuccessNotification("Availability exception saved.");
        return RedirectToRoute(AppointmentBookingDefaults.AccountServiceAvailabilityRouteName, new { id = service.Id });
    }

    [HttpPost]
    public async Task<IActionResult> BlockUnavailableDates(AvailabilityModel model)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var services = (await _appointmentBookingService.GetServicesByVendorAsync(vendorId))
            .Where(service => service.IsActive)
            .ToList();
        var dateValues = (model.UnavailableDateValues ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => DateTime.TryParse(value, out var parsedDate) ? parsedDate.Date : (DateTime?)null)
            .Where(date => date.HasValue)
            .Select(date => date.Value)
            .Distinct()
            .ToList();

        if (!services.Any() || !dateValues.Any())
        {
            _notificationService.ErrorNotification("Select at least one date and make sure you have an active service.");
            return RedirectToRoute(AppointmentBookingDefaults.AccountAvailabilityRouteName);
        }

        foreach (var service in services)
        {
            foreach (var date in dateValues)
            {
                await _appointmentBookingService.SaveAvailabilityExceptionAsync(new AvailabilityException
                {
                    ServiceId = service.Id,
                    VendorId = vendorId,
                    ExceptionDateUtc = date,
                    StartTimeUtc = date,
                    EndTimeUtc = date.AddDays(1),
                    IsAvailable = false,
                    Reason = model.Reason?.Trim()
                });
            }
        }

        _notificationService.SuccessNotification("Unavailable date saved.");
        return RedirectToRoute(AppointmentBookingDefaults.AccountAvailabilityRouteName);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteBlockedDate(DateTime blockedDate)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var services = await _appointmentBookingService.GetServicesByVendorAsync(vendorId);
        foreach (var service in services)
        {
            var exceptions = await _appointmentBookingService.GetAvailabilityExceptionsAsync(service.Id);
            foreach (var exception in exceptions.Where(exception => !exception.IsAvailable && exception.ExceptionDateUtc.Date == blockedDate.Date))
                await _appointmentBookingService.DeleteAvailabilityExceptionAsync(exception.Id);
        }

        _notificationService.SuccessNotification("Unavailable date deleted.");
        return RedirectToRoute(AppointmentBookingDefaults.AccountAvailabilityRouteName);
    }

    public async Task<IActionResult> Questions(int id)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var service = await GetVendorServiceAsync(id, vendorId);
        if (service == null)
            return InvokeHttp404();

        var model = new ServiceQuestionModel
        {
            ServiceId = service.Id,
            ServiceName = service.Name,
            Questions = await _appointmentBookingService.GetServiceQuestionsAsync(service.Id)
        };

        return View("~/Plugins/Misc.AppointmentBooking/Views/Account/Questions.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> AddQuestion(ServiceQuestionModel model)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var service = await GetVendorServiceAsync(model.ServiceId, vendorId);
        if (service == null)
            return InvokeHttp404();

        if (string.IsNullOrWhiteSpace(model.QuestionText))
        {
            _notificationService.ErrorNotification("Question text is required.");
            return RedirectToRoute(AppointmentBookingDefaults.AccountServiceQuestionsRouteName, new { id = service.Id });
        }

        await _appointmentBookingService.SaveServiceQuestionAsync(new ServiceQuestion
        {
            ServiceId = service.Id,
            QuestionText = model.QuestionText.Trim(),
            QuestionType = string.IsNullOrWhiteSpace(model.QuestionType) ? "Text" : model.QuestionType.Trim(),
            IsRequired = model.IsRequired,
            DisplayOrder = model.DisplayOrder,
            OptionsJson = model.OptionsJson?.Trim()
        });

        _notificationService.SuccessNotification("Question saved.");
        return RedirectToRoute(AppointmentBookingDefaults.AccountServiceQuestionsRouteName, new { id = service.Id });
    }

    public async Task<IActionResult> Bookings()
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var services = (await _appointmentBookingService.GetServicesByVendorAsync(vendorId)).ToDictionary(service => service.Id, service => service.Name);
        var bookings = await _appointmentBookingService.GetBookingsByVendorAsync(vendorId);
        var model = new List<VendorBookingModel>();
        foreach (var booking in bookings)
        {
            model.Add(new VendorBookingModel
            {
                Id = booking.Id,
                ServiceName = services.TryGetValue(booking.ServiceId, out var serviceName) ? serviceName : $"Service #{booking.ServiceId}",
                CustomerDisplayName = await PrepareCustomerDisplayNameAsync(booking.CustomerId),
                OrderId = booking.OrderId,
                OrderDisplayText = booking.OrderId.HasValue ? $"#{booking.OrderId.Value}" : "Not checked out",
                StartUtc = booking.StartUtc,
                StartText = await FormatDateTimeAsync(booking.StartUtc),
                EndUtc = booking.EndUtc,
                EndText = await FormatDateTimeAsync(booking.EndUtc),
                Status = booking.Status,
                AttendeeName = booking.AttendeeName,
                AttendeeEmail = booking.AttendeeEmail
            });
        }

        return View("~/Plugins/Misc.AppointmentBooking/Views/Account/Bookings.cshtml", model);
    }
}
