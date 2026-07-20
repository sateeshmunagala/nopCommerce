using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Plugin.Misc.AppointmentBooking.Domains;
using Nop.Plugin.Misc.AppointmentBooking.Models.Account;
using Nop.Plugin.Misc.AppointmentBooking.Services;
using Nop.Services.Catalog;
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
    private readonly INotificationService _notificationService;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IProductService _productService;
    private readonly IWorkContext _workContext;

    public VendorAppointmentBookingController(AppointmentBookingSettings appointmentBookingSettings,
        IAppointmentBookingService appointmentBookingService,
        INotificationService notificationService,
        IPriceFormatter priceFormatter,
        IProductService productService,
        IWorkContext workContext)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _appointmentBookingService = appointmentBookingService;
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

    protected virtual async Task<IList<SelectListItem>> PrepareVendorProductsAsync(int vendorId, int selectedProductId = 0)
    {
        var products = await _productService.SearchProductsAsync(vendorId: vendorId, showHidden: true);
        var items = products
            .Select(product => new SelectListItem
            {
                Text = product.Name,
                Value = product.Id.ToString(),
                Selected = product.Id == selectedProductId
            })
            .ToList();

        items.Insert(0, new SelectListItem
        {
            Text = "Select product",
            Value = "0",
            Selected = selectedProductId == 0
        });

        return items;
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
            ServiceDescription = service.Description,
            MappedProductId = mapping?.ProductId ?? 0,
            IsPublic = service.Id == 0 || service.IsActive,
            AvailableProducts = await PrepareVendorProductsAsync(vendorId, mapping?.ProductId ?? 0)
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
                IsPublic = service.IsActive,
                MappedProductId = mapping?.ProductId ?? 0
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

        var mappedProduct = model.MappedProductId > 0 ? await _productService.GetProductByIdAsync(model.MappedProductId) : null;
        if (model.MappedProductId > 0 && mappedProduct?.VendorId != vendorId)
            ModelState.AddModelError(nameof(model.MappedProductId), "Select one of your products.");

        if (!ModelState.IsValid)
        {
            var invalidModel = await PrepareEditServiceModelAsync(service, vendorId);
            invalidModel.Title = model.Title;
            invalidModel.ShortDescription = model.ShortDescription;
            invalidModel.Price = model.Price;
            invalidModel.DurationMinutes = model.DurationMinutes;
            invalidModel.ServiceDescription = model.ServiceDescription;
            invalidModel.MappedProductId = model.MappedProductId;
            invalidModel.IsPublic = model.IsPublic;
            invalidModel.AvailableProducts = await PrepareVendorProductsAsync(vendorId, model.MappedProductId);
            return View("~/Plugins/Misc.AppointmentBooking/Views/Account/EditService.cshtml", invalidModel);
        }

        service = await _appointmentBookingService.SaveServiceAsync(ApplyServiceModel(model, service, vendorId));

        if (mappedProduct != null)
        {
            mappedProduct.ShortDescription = model.ShortDescription?.Trim();
            mappedProduct.Price = model.Price;
            await _productService.UpdateProductAsync(mappedProduct);
            await _appointmentBookingService.MapServiceToProductAsync(service.Id, mappedProduct.Id, vendorId);
        }
        else
            await _appointmentBookingService.ClearServiceProductMappingsAsync(service.Id, vendorId);

        _notificationService.SuccessNotification("Service saved.");
        return RedirectToRoute(AppointmentBookingDefaults.AccountEditServiceRouteName, new { id = service.Id });
    }

    public async Task<IActionResult> Availability(int id)
    {
        var vendorId = await GetCurrentVendorIdAsync();
        if (vendorId <= 0)
            return Challenge();

        var service = await GetVendorServiceAsync(id, vendorId);
        if (service == null)
            return InvokeHttp404();

        var model = new AvailabilityModel
        {
            ServiceId = service.Id,
            ServiceName = service.Name,
            Rules = await _appointmentBookingService.GetAvailabilityRulesAsync(service.Id),
            Exceptions = await _appointmentBookingService.GetAvailabilityExceptionsAsync(service.Id),
            StartTime = "09:00",
            EndTime = "17:00",
            IsActive = true
        };

        return View("~/Plugins/Misc.AppointmentBooking/Views/Account/Availability.cshtml", model);
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
        var model = bookings.Select(booking => new VendorBookingModel
        {
            Id = booking.Id,
            ServiceName = services.TryGetValue(booking.ServiceId, out var serviceName) ? serviceName : $"Service #{booking.ServiceId}",
            ProductId = booking.ProductId,
            CustomerId = booking.CustomerId,
            OrderId = booking.OrderId,
            StartUtc = booking.StartUtc,
            EndUtc = booking.EndUtc,
            Status = booking.Status,
            AttendeeName = booking.AttendeeName,
            AttendeeEmail = booking.AttendeeEmail
        }).ToList();

        return View("~/Plugins/Misc.AppointmentBooking/Views/Account/Bookings.cshtml", model);
    }
}
