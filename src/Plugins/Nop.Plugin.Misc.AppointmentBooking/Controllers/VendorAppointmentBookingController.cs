using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using System.Globalization;
using Nop.Plugin.Misc.AppointmentBooking.Domains;
using Nop.Plugin.Misc.AppointmentBooking.Models.Account;
using Nop.Plugin.Misc.AppointmentBooking.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Messages;
using Nop.Web.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Framework.Mvc.Routing;

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
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IProductService _productService;
    private readonly IWorkContext _workContext;

    public VendorAppointmentBookingController(AppointmentBookingSettings appointmentBookingSettings,
        IAppointmentBookingService appointmentBookingService,
        ICustomerService customerService,
        IDateTimeHelper dateTimeHelper,
        INotificationService notificationService,
        INopUrlHelper nopUrlHelper,
        IPriceFormatter priceFormatter,
        IProductService productService,
        IWorkContext workContext)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _appointmentBookingService = appointmentBookingService;
        _customerService = customerService;
        _dateTimeHelper = dateTimeHelper;
        _notificationService = notificationService;
        _nopUrlHelper = nopUrlHelper;
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
            var text = DateTime.Today.Add(time).ToString("h:mm tt", CultureInfo.CurrentCulture);
            options.Add(new SelectListItem { Text = text, Value = value });
        }

        return options;
    }

    protected virtual IList<int> PrepareWeekDays()
    {
        return [(int)DayOfWeek.Monday, (int)DayOfWeek.Tuesday, (int)DayOfWeek.Wednesday, (int)DayOfWeek.Thursday, (int)DayOfWeek.Friday, (int)DayOfWeek.Saturday, (int)DayOfWeek.Sunday];
    }

    protected virtual IList<SelectListItem> PrepareTimeZoneOptions(string selectedTimeZoneId)
    {
        return _dateTimeHelper.GetSystemTimeZones()
            .Select(timeZone => new SelectListItem
            {
                Text = timeZone.DisplayName,
                Value = timeZone.Id,
                Selected = timeZone.Id == selectedTimeZoneId
            })
            .ToList();
    }

    protected virtual IList<ScheduleDayModel> PrepareScheduleFromRules(IList<AvailabilityRule> rules)
    {
        var activeRulesByDay = rules
            .Where(rule => rule.IsActive)
            .GroupBy(rule => rule.DayOfWeek)
            .ToDictionary(group => group.Key, group => group.OrderBy(rule => rule.StartTimeUtc).ToList());

        return PrepareWeekDays().Select(day =>
        {
            var dayRules = activeRulesByDay.TryGetValue(day, out var activeRules)
                ? activeRules
                : new List<AvailabilityRule>();

            return new ScheduleDayModel
            {
                DayOfWeek = day,
                Enabled = dayRules.Any(),
                Intervals = dayRules.Any()
                    ? dayRules.Select(rule => new ScheduleIntervalModel
                    {
                        StartTime = rule.StartTimeUtc.ToString("HH:mm"),
                        EndTime = rule.EndTimeUtc.ToString("HH:mm")
                    }).ToList()
                    : new List<ScheduleIntervalModel>
                    {
                        new()
                        {
                            StartTime = "09:00",
                            EndTime = "17:00"
                        }
                    }
            };
        }).ToList();
    }

    protected virtual IList<ScheduleDayModel> NormalizePostedSchedule(IList<ScheduleDayModel> schedule)
    {
        var postedByDay = (schedule ?? new List<ScheduleDayModel>())
            .GroupBy(day => day.DayOfWeek)
            .ToDictionary(group => group.Key, group => group.First());

        return PrepareWeekDays().Select(day =>
        {
            var postedDay = postedByDay.TryGetValue(day, out var value) ? value : new ScheduleDayModel { DayOfWeek = day };
            postedDay.DayOfWeek = day;
            postedDay.Intervals ??= new List<ScheduleIntervalModel>();
            if (!postedDay.Intervals.Any())
            {
                postedDay.Intervals.Add(new ScheduleIntervalModel
                {
                    StartTime = "09:00",
                    EndTime = "17:00"
                });
            }

            return postedDay;
        }).ToList();
    }

    protected virtual async Task<string> FormatDateTimeAsync(DateTime dateTimeUtc)
    {
        var userTime = await _dateTimeHelper.ConvertToUserTimeAsync(dateTimeUtc, DateTimeKind.Utc);
        return userTime.ToString("yyyy-MM-dd HH:mm");
    }

    protected virtual string FormatTimeZoneOffset(DateTime dateTimeUtc)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(dateTimeUtc);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        offset = offset.Duration();

        return $"GMT{sign}{offset.Hours}:{offset.Minutes:D2}";
    }

    protected virtual async Task<string> FormatBookingDateHeaderAsync(DateTime startUtc)
    {
        var userStart = await _dateTimeHelper.ConvertToUserTimeAsync(startUtc, DateTimeKind.Utc);
        return userStart.ToString("ddd, dd MMM yyyy");
    }

    protected virtual async Task<string> FormatBookingTimeRangeAsync(DateTime startUtc, DateTime endUtc)
    {
        var userStart = await _dateTimeHelper.ConvertToUserTimeAsync(startUtc, DateTimeKind.Utc);
        var userEnd = await _dateTimeHelper.ConvertToUserTimeAsync(endUtc, DateTimeKind.Utc);
        return $"{userStart:hh:mm} - {userEnd:hh:mm tt} ({FormatTimeZoneOffset(startUtc)})";
    }

    protected virtual bool IsCompletedBooking(Booking booking)
    {
        return string.Equals(booking.Status, BookingStatus.Completed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(booking.Status, BookingStatus.Cancelled, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(booking.Status, BookingStatus.NoShow, StringComparison.OrdinalIgnoreCase) ||
            booking.EndUtc < DateTime.UtcNow;
    }

    protected virtual string GetBookingStatusCssClass(string status)
    {
        return status?.ToLowerInvariant() switch
        {
            "cancelled" => "cancelled",
            "completed" => "completed",
            "confirmed" => "confirmed",
            "noshow" => "cancelled",
            _ => "pending"
        };
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

    protected virtual async Task<AvailabilityModel> PrepareAvailabilityModelAsync(int vendorId, AvailabilityModel postedModel = null)
    {
        var services = await _appointmentBookingService.GetServicesByVendorAsync(vendorId);
        var firstService = services.FirstOrDefault();
        var rules = firstService == null
            ? new List<AvailabilityRule>()
            : (await _appointmentBookingService.GetAvailabilityRulesAsync(firstService.Id)).ToList();
        var exceptions = new List<AvailabilityException>();
        var selectedTimeZoneId = rules
            .Where(rule => rule.IsActive)
            .Select(rule => rule.TimeZoneId)
            .FirstOrDefault(timeZoneId => !string.IsNullOrWhiteSpace(timeZoneId));
        selectedTimeZoneId = string.IsNullOrWhiteSpace(selectedTimeZoneId)
            ? (await _dateTimeHelper.GetCurrentTimeZoneAsync()).Id
            : selectedTimeZoneId.Trim();
        selectedTimeZoneId = string.IsNullOrWhiteSpace(postedModel?.TimeZoneId)
            ? selectedTimeZoneId
            : postedModel.TimeZoneId.Trim();

        foreach (var service in services)
            exceptions.AddRange((await _appointmentBookingService.GetAvailabilityExceptionsAsync(service.Id))
                .Where(exception => !exception.IsAvailable));

        var schedule = postedModel?.Schedule?.Any() == true
            ? NormalizePostedSchedule(postedModel.Schedule)
            : PrepareScheduleFromRules(rules);

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
            AvailableTimeZones = PrepareTimeZoneOptions(selectedTimeZoneId),
            BlockedDates = blockedDates,
            StartTime = "09:00",
            EndTime = "17:00",
            TimeZoneId = selectedTimeZoneId,
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

        var services = (await _appointmentBookingService.GetServicesByVendorAsync(vendorId))
            .Where(service => service.IsActive)
            .ToList();
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
                ProductUrl = product != null ? await _nopUrlHelper.RouteGenericUrlAsync(product) : string.Empty,
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

    [HttpPost]
    public async Task<IActionResult> DeleteService(int id)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var service = await GetVendorServiceAsync(id, vendorId);
        if (service == null)
            return InvokeHttp404();

        service.IsActive = false;
        await _appointmentBookingService.SaveServiceAsync(service);

        var mapping = await _appointmentBookingService.GetActiveProductMappingByServiceAsync(service.Id);
        var mappedProduct = mapping?.ProductId > 0 ? await _productService.GetProductByIdAsync(mapping.ProductId) : null;
        if (mappedProduct != null)
        {
            mappedProduct.Published = false;
            mappedProduct.VisibleIndividually = false;
            mappedProduct.DisableBuyButton = true;
            await _productService.UpdateProductAsync(mappedProduct);
        }

        _notificationService.SuccessNotification("Service deleted.");
        return RedirectToRoute(AppointmentBookingDefaults.AccountServicesRouteName);
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

        try
        {
            service = await _appointmentBookingService.SaveServiceAsync(ApplyServiceModel(model, service, vendorId));
        }
        catch (InvalidOperationException exception)
        {
            _notificationService.ErrorNotification(exception.Message);
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

    protected virtual bool TryParseScheduleTime(string value, out TimeSpan time)
    {
        return TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out time) ||
            TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time);
    }

    protected virtual IList<string> ValidateSchedule(AvailabilityModel model)
    {
        var errors = new List<string>();
        var enabledDays = (model.Schedule ?? new List<ScheduleDayModel>()).Where(day => day.Enabled).ToList();
        if (!enabledDays.Any())
        {
            errors.Add("Select at least one available day.");
            return errors;
        }

        foreach (var day in enabledDays)
        {
            var dayName = Enum.GetName(typeof(DayOfWeek), day.DayOfWeek) ?? "day";
            var intervals = day.Intervals ?? new List<ScheduleIntervalModel>();
            if (!intervals.Any())
            {
                errors.Add($"Add at least one time slot for {dayName}.");
                continue;
            }

            var parsedIntervals = new List<(TimeSpan Start, TimeSpan End)>();
            foreach (var interval in intervals)
            {
                var hasStartTime = !string.IsNullOrWhiteSpace(interval.StartTime);
                var hasEndTime = !string.IsNullOrWhiteSpace(interval.EndTime);

                if (!hasStartTime)
                    errors.Add($"Select a start time for {dayName}.");

                if (!hasEndTime)
                    errors.Add($"Select an end time for {dayName}.");

                if (!hasStartTime || !hasEndTime)
                    continue;

                if (!TryParseScheduleTime(interval.StartTime, out var startTime))
                {
                    errors.Add($"Select a start time for {dayName}.");
                    continue;
                }

                if (!TryParseScheduleTime(interval.EndTime, out var endTime))
                {
                    errors.Add($"Select an end time for {dayName}.");
                    continue;
                }

                if (startTime >= endTime)
                {
                    errors.Add($"Start time must be earlier than end time for {dayName}.");
                    continue;
                }

                parsedIntervals.Add((startTime, endTime));
            }

            var orderedIntervals = parsedIntervals.OrderBy(interval => interval.Start).ToList();
            for (var i = 1; i < orderedIntervals.Count; i++)
            {
                if (orderedIntervals[i].Start < orderedIntervals[i - 1].End)
                {
                    errors.Add($"Time slots cannot overlap for {dayName}.");
                    break;
                }
            }
        }

        return errors.Distinct().ToList();
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
            ModelState.AddModelError(string.Empty, "Create an active service before saving a schedule.");
        }

        var selectedTimeZoneId = model.TimeZoneId?.Trim();
        selectedTimeZoneId = _dateTimeHelper.GetSystemTimeZones().Any(timeZone => timeZone.Id == selectedTimeZoneId)
            ? selectedTimeZoneId
            : "UTC";
        model.TimeZoneId = selectedTimeZoneId;

        foreach (var error in ValidateSchedule(model))
            ModelState.AddModelError(string.Empty, error);

        if (!ModelState.IsValid)
            return View("~/Plugins/Misc.AppointmentBooking/Views/Account/Availability.cshtml", await PrepareAvailabilityModelAsync(vendorId, model));

        foreach (var service in services)
        {
            await _appointmentBookingService.DeleteAvailabilityRulesAsync(service.Id);

            foreach (var day in model.Schedule.Where(day => day.Enabled))
            {
                var intervals = (day.Intervals ?? new List<ScheduleIntervalModel>())
                    .Select(interval => new
                    {
                        StartTime = interval.StartTime?.Trim(),
                        EndTime = interval.EndTime?.Trim()
                    })
                    .Distinct()
                    .ToList();

                foreach (var interval in intervals)
                {
                    await _appointmentBookingService.SaveAvailabilityRuleAsync(new AvailabilityRule
                    {
                        ServiceId = service.Id,
                        VendorId = vendorId,
                        DayOfWeek = day.DayOfWeek,
                        StartTimeUtc = ParseTimeForDate(DateTime.UtcNow, interval.StartTime),
                        EndTimeUtc = ParseTimeForDate(DateTime.UtcNow, interval.EndTime),
                        TimeZoneId = selectedTimeZoneId,
                        IsActive = true
                    });
                }
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
            .Select(value => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate) ? parsedDate.Date : (DateTime?)null)
            .Where(date => date.HasValue)
            .Select(date => date.Value)
            .Distinct()
            .ToList();

        if (!dateValues.Any())
        {
            _notificationService.ErrorNotification("Select at least one unavailable date.");
            return RedirectToRoute(AppointmentBookingDefaults.AccountAvailabilityRouteName);
        }

        if (!services.Any())
        {
            _notificationService.ErrorNotification("Create an active service before blocking unavailable dates.");
            return RedirectToRoute(AppointmentBookingDefaults.AccountAvailabilityRouteName);
        }

        foreach (var service in services)
        {
            var existingUnavailableExceptions = (await _appointmentBookingService.GetAvailabilityExceptionsAsync(service.Id))
                .Where(exception => !exception.IsAvailable)
                .GroupBy(exception => exception.ExceptionDateUtc.Date)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(exception => exception.UpdatedOnUtc).First());

            foreach (var date in dateValues)
            {
                var availabilityException = existingUnavailableExceptions.TryGetValue(date, out var existingException)
                    ? existingException
                    : new AvailabilityException
                    {
                        ServiceId = service.Id,
                        VendorId = vendorId,
                        ExceptionDateUtc = date,
                        IsAvailable = false
                    };

                availabilityException.VendorId = vendorId;
                availabilityException.StartTimeUtc = date;
                availabilityException.EndTimeUtc = date.AddDays(1).AddSeconds(-1);
                availabilityException.IsAvailable = false;
                availabilityException.Reason = model.Reason?.Trim();

                await _appointmentBookingService.SaveAvailabilityExceptionAsync(availabilityException);

                foreach (var duplicateException in (await _appointmentBookingService.GetAvailabilityExceptionsAsync(service.Id))
                    .Where(exception => !exception.IsAvailable && exception.ExceptionDateUtc.Date == date && exception.Id != availabilityException.Id))
                {
                    await _appointmentBookingService.DeleteAvailabilityExceptionAsync(duplicateException.Id);
                }
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

        return View("~/Plugins/Misc.AppointmentBooking/Views/Account/Bookings.cshtml", new VendorBookingTabModel
        {
            ActiveTab = "upcoming"
        });
    }

    public async Task<IActionResult> BookingsTab(string tab = "upcoming")
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var activeTab = string.Equals(tab, "completed", StringComparison.OrdinalIgnoreCase) ? "completed" : "upcoming";
        var model = new VendorBookingTabModel
        {
            ActiveTab = activeTab,
            Bookings = await PrepareVendorBookingModelsAsync(vendorId, activeTab)
        };

        var viewPath = activeTab == "completed"
            ? "~/Plugins/Misc.AppointmentBooking/Views/Account/_BookingsCompleted.cshtml"
            : "~/Plugins/Misc.AppointmentBooking/Views/Account/_BookingsUpcoming.cshtml";

        return PartialView(viewPath, model);
    }

    protected virtual async Task<IList<VendorBookingModel>> PrepareVendorBookingModelsAsync(int vendorId, string activeTab)
    {
        var services = (await _appointmentBookingService.GetServicesByVendorAsync(vendorId)).ToDictionary(service => service.Id, service => service.Name);
        var bookings = await _appointmentBookingService.GetBookingsByVendorAsync(vendorId);
        var isCompletedTab = string.Equals(activeTab, "completed", StringComparison.OrdinalIgnoreCase);
        var filteredBookings = bookings
            .Where(booking => isCompletedTab ? IsCompletedBooking(booking) : !IsCompletedBooking(booking));
        filteredBookings = isCompletedTab
            ? filteredBookings.OrderByDescending(booking => booking.StartUtc)
            : filteredBookings.OrderBy(booking => booking.StartUtc);
        var model = new List<VendorBookingModel>();

        foreach (var booking in filteredBookings)
        {
            var product = booking.ProductId > 0 ? await _productService.GetProductByIdAsync(booking.ProductId) : null;
            var displayCustomerName = !string.IsNullOrWhiteSpace(booking.AttendeeName)
                ? booking.AttendeeName
                : await PrepareCustomerDisplayNameAsync(booking.CustomerId);

            model.Add(new VendorBookingModel
            {
                Id = booking.Id,
                ServiceName = services.TryGetValue(booking.ServiceId, out var serviceName) ? serviceName : $"Service #{booking.ServiceId}",
                CustomerDisplayName = await PrepareCustomerDisplayNameAsync(booking.CustomerId),
                DisplayCustomerName = displayCustomerName,
                OrderId = booking.OrderId,
                OrderDisplayText = booking.OrderId.HasValue ? $"#{booking.OrderId.Value}" : "Not checked out",
                ProductId = booking.ProductId,
                StartUtc = booking.StartUtc,
                StartText = await FormatDateTimeAsync(booking.StartUtc),
                EndUtc = booking.EndUtc,
                EndText = await FormatDateTimeAsync(booking.EndUtc),
                DateHeaderText = await FormatBookingDateHeaderAsync(booking.StartUtc),
                TimeRangeText = await FormatBookingTimeRangeAsync(booking.StartUtc, booking.EndUtc),
                DurationMinutes = Math.Max(1, (int)Math.Round((booking.EndUtc - booking.StartUtc).TotalMinutes)),
                PriceText = product != null ? await _priceFormatter.FormatPriceAsync(product.Price) : await _priceFormatter.FormatPriceAsync(decimal.Zero),
                Status = booking.Status,
                StatusText = string.IsNullOrWhiteSpace(booking.Status) ? "Pending" : booking.Status,
                StatusCssClass = GetBookingStatusCssClass(booking.Status),
                AttendeeName = booking.AttendeeName,
                AttendeeEmail = booking.AttendeeEmail
            });
        }

        return model;
    }
}
