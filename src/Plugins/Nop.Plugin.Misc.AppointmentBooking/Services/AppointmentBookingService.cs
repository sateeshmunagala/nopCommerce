using System.Globalization;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Misc.AppointmentBooking.Domains;
using Nop.Plugin.Misc.AppointmentBooking.Models;
using Nop.Services.Catalog;
using Nop.Services.Helpers;
using Nop.Services.Seo;

namespace Nop.Plugin.Misc.AppointmentBooking.Services;

/// <summary>
/// Represents appointment booking service
/// </summary>
public class AppointmentBookingService : IAppointmentBookingService
{
    #region Fields

    private readonly AppointmentBookingSettings _appointmentBookingSettings;
    private readonly IRepository<AvailabilityException> _availabilityExceptionRepository;
    private readonly IRepository<AvailabilityRule> _availabilityRuleRepository;
    private readonly IRepository<BookingAnswer> _bookingAnswerRepository;
    private readonly IRepository<Booking> _bookingRepository;
    private readonly IRepository<BookableService> _bookableServiceRepository;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IProductService _productService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly IRepository<ServiceProductMapping> _serviceProductMappingRepository;
    private readonly IRepository<ServiceQuestion> _serviceQuestionRepository;
    private readonly IRepository<TimeSlotHold> _timeSlotHoldRepository;
    private readonly IUrlRecordService _urlRecordService;

    #endregion

    #region Ctor

    public AppointmentBookingService(AppointmentBookingSettings appointmentBookingSettings,
        IRepository<AvailabilityException> availabilityExceptionRepository,
        IRepository<AvailabilityRule> availabilityRuleRepository,
        IRepository<BookingAnswer> bookingAnswerRepository,
        IRepository<Booking> bookingRepository,
        IRepository<BookableService> bookableServiceRepository,
        IDateTimeHelper dateTimeHelper,
        IPriceFormatter priceFormatter,
        IProductService productService,
        IProductTemplateService productTemplateService,
        IRepository<ServiceProductMapping> serviceProductMappingRepository,
        IRepository<ServiceQuestion> serviceQuestionRepository,
        IRepository<TimeSlotHold> timeSlotHoldRepository,
        IUrlRecordService urlRecordService)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _availabilityExceptionRepository = availabilityExceptionRepository;
        _availabilityRuleRepository = availabilityRuleRepository;
        _bookingAnswerRepository = bookingAnswerRepository;
        _bookingRepository = bookingRepository;
        _bookableServiceRepository = bookableServiceRepository;
        _dateTimeHelper = dateTimeHelper;
        _priceFormatter = priceFormatter;
        _productService = productService;
        _productTemplateService = productTemplateService;
        _serviceProductMappingRepository = serviceProductMappingRepository;
        _serviceQuestionRepository = serviceQuestionRepository;
        _timeSlotHoldRepository = timeSlotHoldRepository;
        _urlRecordService = urlRecordService;
    }

    #endregion

    #region Utilities

    protected virtual bool Overlaps(DateTime startUtc, DateTime endUtc, DateTime otherStartUtc, DateTime otherEndUtc)
    {
        return startUtc < otherEndUtc && otherStartUtc < endUtc;
    }

    protected virtual DateTime ComposeTime(DateTime dateUtc, DateTime timeUtc)
    {
        return dateUtc.Date.Add(timeUtc.TimeOfDay);
    }

    protected virtual async Task<int> GetServiceProductTemplateIdAsync()
    {
        var templates = await _productTemplateService.GetAllProductTemplatesAsync();
        return templates.FirstOrDefault(template =>
            string.Equals(template.ViewPath, AppointmentBookingDefaults.ServiceProductTemplateViewPath, StringComparison.OrdinalIgnoreCase))?.Id ??
            templates.FirstOrDefault(template =>
                string.Equals(template.Name, AppointmentBookingDefaults.ServiceProductTemplateName, StringComparison.OrdinalIgnoreCase))?.Id ??
            0;
    }

    protected virtual async Task<Product> EnsureServiceProductAsync(BookableService service)
    {
        if (service == null || service.Id <= 0)
            return null;

        var mapping = await GetActiveProductMappingByServiceAsync(service.Id);
        var product = mapping?.ProductId > 0 ? await _productService.GetProductByIdAsync(mapping.ProductId) : null;
        var now = DateTime.UtcNow;
        var productTemplateId = await GetServiceProductTemplateIdAsync();

        if (product == null)
        {
            product = new Product
            {
                ProductType = ProductType.SimpleProduct,
                VisibleIndividually = true,
                Name = service.Name,
                ShortDescription = string.Empty,
                FullDescription = service.Description,
                ProductTemplateId = productTemplateId,
                VendorId = service.VendorId,
                Published = service.IsActive,
                DisableBuyButton = false,
                DisableWishlistButton = true,
                IsShipEnabled = false,
                ManageInventoryMethod = ManageInventoryMethod.DontManageStock,
                StockQuantity = 10000,
                OrderMinimumQuantity = 1,
                OrderMaximumQuantity = 1,
                CreatedOnUtc = now,
                UpdatedOnUtc = now
            };

            await _productService.InsertProductAsync(product);
        }
        else
        {
            product.Name = service.Name;
            product.FullDescription = service.Description;
            product.ProductTemplateId = productTemplateId > 0 ? productTemplateId : product.ProductTemplateId;
            product.VendorId = service.VendorId;
            product.Published = service.IsActive;
            product.VisibleIndividually = true;
            product.DisableBuyButton = false;
            product.DisableWishlistButton = true;
            product.IsShipEnabled = false;
            product.OrderMinimumQuantity = product.OrderMinimumQuantity <= 0 ? 1 : product.OrderMinimumQuantity;
            product.OrderMaximumQuantity = product.OrderMaximumQuantity <= 0 ? 1 : product.OrderMaximumQuantity;
            product.UpdatedOnUtc = now;

            await _productService.UpdateProductAsync(product);
        }

        var seName = await _urlRecordService.ValidateSeNameAsync(product, string.Empty, product.Name, true);
        await _urlRecordService.SaveSlugAsync(product, seName, 0);
        await MapServiceToProductAsync(service.Id, product.Id, service.VendorId);

        return product;
    }

    protected virtual async Task<bool> IsSlotAvailableAsync(BookableService service, DateTime startUtc, DateTime endUtc)
    {
        var checkStartUtc = startUtc.AddMinutes(-service.BufferBeforeMinutes);
        var checkEndUtc = endUtc.AddMinutes(service.BufferAfterMinutes);

        var dateStartUtc = startUtc.Date;
        var dateEndUtc = dateStartUtc.AddDays(1);
        var unavailableExceptions = await _availabilityExceptionRepository.Table
            .Where(exception => exception.ServiceId == service.Id &&
                !exception.IsAvailable &&
                exception.ExceptionDateUtc >= dateStartUtc &&
                exception.ExceptionDateUtc < dateEndUtc)
            .ToListAsync();

        if (unavailableExceptions.Any(exception => Overlaps(startUtc, endUtc,
            ComposeTime(startUtc, exception.StartTimeUtc),
            ComposeTime(startUtc, exception.EndTimeUtc))))
            return false;

        var existingBookings = await _bookingRepository.Table
            .Where(booking => booking.ServiceId == service.Id &&
                booking.Status != BookingStatus.Cancelled &&
                booking.StartUtc < checkEndUtc &&
                booking.EndUtc > checkStartUtc)
            .ToListAsync();

        if (existingBookings.Any(booking => Overlaps(checkStartUtc, checkEndUtc,
            booking.StartUtc.AddMinutes(-service.BufferBeforeMinutes),
            booking.EndUtc.AddMinutes(service.BufferAfterMinutes))))
            return false;

        var activeHold = await _timeSlotHoldRepository.Table
            .Where(hold => hold.ServiceId == service.Id &&
                hold.ExpiresOnUtc > DateTime.UtcNow &&
                hold.StartUtc < checkEndUtc &&
                hold.EndUtc > checkStartUtc)
            .FirstOrDefaultAsync();

        return activeHold == null;
    }

    #endregion

    #region Methods

    public async Task<bool> IsAppointmentProductAsync(int productId)
    {
        if (!_appointmentBookingSettings.Enabled || productId <= 0)
            return false;

        return await GetServiceByProductAsync(productId) != null;
    }

    public async Task<ProductAppointmentBookingModel> PrepareProductBookingModelAsync(int productId, int customerId)
    {
        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return null;

        var service = await GetServiceByProductAsync(productId);
        if (service == null)
            return null;

        var fromUtc = DateTime.UtcNow.AddHours(service.MinAdvanceBookingHours);
        var maxAdvanceBookingDays = Math.Max(service.MaxAdvanceBookingDays, 30);
        var toUtc = DateTime.UtcNow.AddDays(maxAdvanceBookingDays);
        var slots = await GenerateAvailableSlotsAsync(service.Id, fromUtc, toUtc);
        var questions = await GetServiceQuestionsAsync(service.Id);

        return new ProductAppointmentBookingModel
        {
            Id = product.Id,
            ProductName = product.Name,
            ShortDescription = product.ShortDescription,
            Price = product.Price > decimal.Zero ? await _priceFormatter.FormatPriceAsync(product.Price) : string.Empty,
            DefaultDurationMinutes = service.DurationMinutes,
            ServiceId = service.Id,
            ServiceName = service.Name,
            ServiceDescription = service.Description,
            AvailableSlots = slots,
            Questions = questions.Select(question => new ServiceQuestionModel
            {
                Id = question.Id,
                QuestionText = question.QuestionText,
                QuestionType = question.QuestionType,
                IsRequired = question.IsRequired,
                DisplayOrder = question.DisplayOrder,
                OptionsJson = question.OptionsJson
            }).ToList()
        };
    }

    public async Task<IList<BookableService>> GetAllServicesAsync()
    {
        return await _bookableServiceRepository.Table.OrderBy(service => service.DisplayOrder).ThenBy(service => service.Name).ToListAsync();
    }

    public async Task<IList<BookableService>> GetServicesByVendorAsync(int vendorId)
    {
        if (vendorId <= 0)
            return new List<BookableService>();

        return await _bookableServiceRepository.Table
            .Where(service => service.VendorId == vendorId)
            .OrderBy(service => service.DisplayOrder)
            .ThenBy(service => service.Name)
            .ToListAsync();
    }

    public async Task<BookableService> GetServiceByIdAsync(int serviceId)
    {
        return serviceId <= 0 ? null : await _bookableServiceRepository.GetByIdAsync(serviceId);
    }

    public async Task<BookableService> SaveServiceAsync(BookableService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        var now = DateTime.UtcNow;
        service.UpdatedOnUtc = now;

        if (service.Id == 0)
        {
            service.CreatedOnUtc = now;
            await _bookableServiceRepository.InsertAsync(service);
        }
        else
            await _bookableServiceRepository.UpdateAsync(service);

        await EnsureServiceProductAsync(service);

        return service;
    }

    public async Task<ServiceProductMapping> MapServiceToProductAsync(int serviceId, int productId, int vendorId)
    {
        if (serviceId <= 0 || productId <= 0)
            return null;

        var now = DateTime.UtcNow;
        var activeMappings = await _serviceProductMappingRepository.Table
            .Where(mapping => (mapping.ProductId == productId || mapping.ServiceId == serviceId) && mapping.IsActive)
            .ToListAsync();

        foreach (var mapping in activeMappings)
        {
            if (mapping.ServiceId == serviceId && mapping.ProductId == productId)
            {
                mapping.VendorId = vendorId;
                mapping.UpdatedOnUtc = now;
                await _serviceProductMappingRepository.UpdateAsync(mapping);
                return mapping;
            }

            mapping.IsActive = false;
            mapping.UpdatedOnUtc = now;
            await _serviceProductMappingRepository.UpdateAsync(mapping);
        }

        var serviceMapping = new ServiceProductMapping
        {
            ServiceId = serviceId,
            ProductId = productId,
            VendorId = vendorId,
            IsActive = true,
            CreatedOnUtc = now,
            UpdatedOnUtc = now
        };

        await _serviceProductMappingRepository.InsertAsync(serviceMapping);
        return serviceMapping;
    }

    public async Task ClearServiceProductMappingsAsync(int serviceId, int vendorId)
    {
        if (serviceId <= 0 || vendorId <= 0)
            return;

        var activeMappings = await _serviceProductMappingRepository.Table
            .Where(mapping => mapping.ServiceId == serviceId && mapping.VendorId == vendorId && mapping.IsActive)
            .ToListAsync();

        foreach (var mapping in activeMappings)
        {
            mapping.IsActive = false;
            mapping.UpdatedOnUtc = DateTime.UtcNow;
            await _serviceProductMappingRepository.UpdateAsync(mapping);
        }
    }

    public async Task<BookableService> GetServiceByProductAsync(int productId)
    {
        var mapping = await GetActiveProductMappingAsync(productId);
        return mapping == null ? null : await GetServiceByIdAsync(mapping.ServiceId);
    }

    public async Task<ServiceProductMapping> GetActiveProductMappingAsync(int productId)
    {
        if (productId <= 0)
            return null;

        return await _serviceProductMappingRepository.Table
            .FirstOrDefaultAsync(mapping => mapping.ProductId == productId && mapping.IsActive);
    }

    public async Task<ServiceProductMapping> GetActiveProductMappingByServiceAsync(int serviceId)
    {
        if (serviceId <= 0)
            return null;

        return await _serviceProductMappingRepository.Table
            .FirstOrDefaultAsync(mapping => mapping.ServiceId == serviceId && mapping.IsActive);
    }

    public async Task<IList<AvailabilityRule>> GetAvailabilityRulesAsync(int serviceId)
    {
        return await _availabilityRuleRepository.Table
            .Where(rule => rule.ServiceId == serviceId)
            .OrderBy(rule => rule.DayOfWeek)
            .ThenBy(rule => rule.StartTimeUtc)
            .ToListAsync();
    }

    public async Task<AvailabilityRule> SaveAvailabilityRuleAsync(AvailabilityRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var now = DateTime.UtcNow;
        rule.UpdatedOnUtc = now;

        if (rule.Id == 0)
        {
            rule.CreatedOnUtc = now;
            await _availabilityRuleRepository.InsertAsync(rule);
        }
        else
            await _availabilityRuleRepository.UpdateAsync(rule);

        return rule;
    }

    public async Task DeleteAvailabilityRulesAsync(int serviceId)
    {
        if (serviceId <= 0)
            return;

        var rules = await _availabilityRuleRepository.Table
            .Where(rule => rule.ServiceId == serviceId)
            .ToListAsync();

        foreach (var rule in rules)
            await _availabilityRuleRepository.DeleteAsync(rule);
    }

    public async Task<IList<AvailabilityException>> GetAvailabilityExceptionsAsync(int serviceId)
    {
        return await _availabilityExceptionRepository.Table
            .Where(exception => exception.ServiceId == serviceId)
            .OrderBy(exception => exception.ExceptionDateUtc)
            .ThenBy(exception => exception.StartTimeUtc)
            .ToListAsync();
    }

    public async Task<AvailabilityException> SaveAvailabilityExceptionAsync(AvailabilityException availabilityException)
    {
        ArgumentNullException.ThrowIfNull(availabilityException);

        var now = DateTime.UtcNow;
        availabilityException.UpdatedOnUtc = now;

        if (availabilityException.Id == 0)
        {
            availabilityException.CreatedOnUtc = now;
            await _availabilityExceptionRepository.InsertAsync(availabilityException);
        }
        else
            await _availabilityExceptionRepository.UpdateAsync(availabilityException);

        return availabilityException;
    }

    public async Task DeleteAvailabilityExceptionAsync(int exceptionId)
    {
        if (exceptionId <= 0)
            return;

        var availabilityException = await _availabilityExceptionRepository.GetByIdAsync(exceptionId);
        if (availabilityException != null)
            await _availabilityExceptionRepository.DeleteAsync(availabilityException);
    }

    public async Task<IList<ServiceQuestion>> GetServiceQuestionsAsync(int serviceId)
    {
        return await _serviceQuestionRepository.Table
            .Where(question => question.ServiceId == serviceId)
            .OrderBy(question => question.DisplayOrder)
            .ThenBy(question => question.Id)
            .ToListAsync();
    }

    public async Task<ServiceQuestion> SaveServiceQuestionAsync(ServiceQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var now = DateTime.UtcNow;
        question.UpdatedOnUtc = now;

        if (question.Id == 0)
        {
            question.CreatedOnUtc = now;
            await _serviceQuestionRepository.InsertAsync(question);
        }
        else
            await _serviceQuestionRepository.UpdateAsync(question);

        return question;
    }

    public async Task<IList<AvailableSlotModel>> GenerateAvailableSlotsAsync(int serviceId, DateTime fromUtc, DateTime toUtc)
    {
        var service = await GetServiceByIdAsync(serviceId);
        if (service == null || !service.IsActive || service.DurationMinutes <= 0)
            return new List<AvailableSlotModel>();

        await ReleaseExpiredHoldsAsync();

        var rules = await _availabilityRuleRepository.Table
            .Where(rule => rule.ServiceId == serviceId && rule.IsActive)
            .ToListAsync();

        var slots = new List<AvailableSlotModel>();
        for (var date = fromUtc.Date; date <= toUtc.Date; date = date.AddDays(1))
        {
            foreach (var rule in rules.Where(rule => rule.DayOfWeek == (int)date.DayOfWeek))
            {
                var windowStartUtc = ComposeTime(date, rule.StartTimeUtc);
                var windowEndUtc = ComposeTime(date, rule.EndTimeUtc);

                for (var slotStartUtc = windowStartUtc; slotStartUtc.AddMinutes(service.DurationMinutes) <= windowEndUtc; slotStartUtc = slotStartUtc.AddMinutes(service.DurationMinutes))
                {
                    var slotEndUtc = slotStartUtc.AddMinutes(service.DurationMinutes);
                    if (slotStartUtc < fromUtc || slotEndUtc > toUtc)
                        continue;

                    if (!await IsSlotAvailableAsync(service, slotStartUtc, slotEndUtc))
                        continue;

                    var userStart = await _dateTimeHelper.ConvertToUserTimeAsync(slotStartUtc, DateTimeKind.Utc);
                    slots.Add(new AvailableSlotModel
                    {
                        StartUtc = slotStartUtc,
                        EndUtc = slotEndUtc,
                        DisplayText = userStart.ToString("ddd, MMM d h:mm tt", CultureInfo.CurrentCulture),
                        DateKey = userStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        DateText = userStart.ToString("MMM d", CultureInfo.CurrentCulture),
                        DayText = userStart.ToString("ddd", CultureInfo.CurrentCulture),
                        TimeText = userStart.ToString("h:mm tt", CultureInfo.CurrentCulture)
                    });
                }
            }
        }

        return slots.OrderBy(slot => slot.StartUtc).Take(300).ToList();
    }

    public async Task<TimeSlotHold> CreateTimeSlotHoldAsync(int serviceId, int productId, int customerId, DateTime startUtc)
    {
        var service = await GetServiceByIdAsync(serviceId);
        if (service == null || !service.IsActive)
            return null;

        await ReleaseExpiredHoldsAsync();

        var endUtc = startUtc.AddMinutes(service.DurationMinutes);
        if (!await IsSlotAvailableAsync(service, startUtc, endUtc))
            return null;

        var hold = new TimeSlotHold
        {
            ServiceId = service.Id,
            ProductId = productId,
            VendorId = service.VendorId,
            CustomerId = customerId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            ExpiresOnUtc = DateTime.UtcNow.AddMinutes(15),
            HoldToken = Guid.NewGuid().ToString("N"),
            CreatedOnUtc = DateTime.UtcNow
        };

        await _timeSlotHoldRepository.InsertAsync(hold);

        await _bookingRepository.InsertAsync(new Booking
        {
            ServiceId = hold.ServiceId,
            ProductId = hold.ProductId,
            VendorId = hold.VendorId,
            CustomerId = hold.CustomerId,
            OrderId = null,
            OrderItemId = null,
            StartUtc = hold.StartUtc,
            EndUtc = hold.EndUtc,
            CustomerTimeZoneId = "UTC",
            Status = BookingStatus.PendingCheckout,
            CreatedOnUtc = DateTime.UtcNow,
            UpdatedOnUtc = DateTime.UtcNow
        });

        return hold;
    }

    public async Task<int> ReleaseExpiredHoldsAsync()
    {
        var expiredHolds = await _timeSlotHoldRepository.Table
            .Where(hold => hold.ExpiresOnUtc <= DateTime.UtcNow)
            .ToListAsync();

        foreach (var hold in expiredHolds)
        {
            var pendingBookings = await _bookingRepository.Table
                .Where(booking => booking.ServiceId == hold.ServiceId &&
                    booking.ProductId == hold.ProductId &&
                    booking.CustomerId == hold.CustomerId &&
                    booking.StartUtc == hold.StartUtc &&
                    booking.EndUtc == hold.EndUtc &&
                    booking.Status == BookingStatus.PendingCheckout)
                .ToListAsync();

            foreach (var booking in pendingBookings)
            {
                booking.Status = BookingStatus.Cancelled;
                booking.CancellationReason = "Checkout hold expired.";
                booking.UpdatedOnUtc = DateTime.UtcNow;
                await _bookingRepository.UpdateAsync(booking);
            }

            await _timeSlotHoldRepository.DeleteAsync(hold);
        }

        return expiredHolds.Count;
    }

    public async Task<TimeSlotHold> GetActiveHoldForCustomerProductAsync(int customerId, int productId)
    {
        return await _timeSlotHoldRepository.Table
            .Where(hold => hold.CustomerId == customerId &&
                hold.ProductId == productId &&
                hold.ExpiresOnUtc > DateTime.UtcNow)
            .OrderByDescending(hold => hold.CreatedOnUtc)
            .FirstOrDefaultAsync();
    }

    public async Task<Booking> ConvertHoldToBookingAsync(TimeSlotHold hold, Order order, OrderItem orderItem, string status = BookingStatus.PendingPayment)
    {
        if (hold == null || order == null || orderItem == null)
            return null;

        var existingBooking = await _bookingRepository.Table.FirstOrDefaultAsync(booking => booking.OrderItemId == orderItem.Id);
        if (existingBooking != null)
            return existingBooking;

        var booking = await _bookingRepository.Table
            .Where(item => item.ServiceId == hold.ServiceId &&
                item.ProductId == hold.ProductId &&
                item.CustomerId == hold.CustomerId &&
                item.StartUtc == hold.StartUtc &&
                item.EndUtc == hold.EndUtc &&
                item.Status == BookingStatus.PendingCheckout)
            .OrderByDescending(item => item.CreatedOnUtc)
            .FirstOrDefaultAsync();

        if (booking == null)
        {
            booking = new Booking
            {
                ServiceId = hold.ServiceId,
                ProductId = hold.ProductId,
                VendorId = hold.VendorId,
                CustomerId = hold.CustomerId,
                StartUtc = hold.StartUtc,
                EndUtc = hold.EndUtc,
                CustomerTimeZoneId = "UTC",
                CreatedOnUtc = DateTime.UtcNow
            };

            await _bookingRepository.InsertAsync(booking);
        }

        booking.OrderId = order.Id;
        booking.OrderItemId = orderItem.Id;
        booking.Status = status;
        booking.UpdatedOnUtc = DateTime.UtcNow;

        await _bookingRepository.UpdateAsync(booking);
        await _timeSlotHoldRepository.DeleteAsync(hold);

        return booking;
    }

    public async Task ConfirmBookingsForOrderAsync(Order order)
    {
        if (order == null)
            return;

        var bookings = await _bookingRepository.Table
            .Where(booking => booking.OrderId == order.Id && booking.Status == BookingStatus.PendingPayment)
            .ToListAsync();

        foreach (var booking in bookings)
        {
            booking.Status = BookingStatus.Confirmed;
            booking.UpdatedOnUtc = DateTime.UtcNow;
            await _bookingRepository.UpdateAsync(booking);
        }
    }

    public async Task CancelBookingAsync(int bookingId, string reason)
    {
        var booking = await GetBookingByIdAsync(bookingId);
        if (booking == null)
            return;

        booking.Status = BookingStatus.Cancelled;
        booking.CancellationReason = reason;
        booking.UpdatedOnUtc = DateTime.UtcNow;

        await _bookingRepository.UpdateAsync(booking);
    }

    public async Task SaveBookingAnswersAsync(int bookingId, IDictionary<int, string> answers)
    {
        if (bookingId <= 0 || answers == null)
            return;

        foreach (var answer in answers)
        {
            await _bookingAnswerRepository.InsertAsync(new BookingAnswer
            {
                BookingId = bookingId,
                ServiceQuestionId = answer.Key,
                AnswerText = answer.Value,
                CreatedOnUtc = DateTime.UtcNow
            });
        }
    }

    public async Task<IList<Booking>> GetAllBookingsAsync()
    {
        return await _bookingRepository.Table.OrderByDescending(booking => booking.StartUtc).ToListAsync();
    }

    public async Task<IList<Booking>> GetBookingsByVendorAsync(int vendorId)
    {
        if (vendorId <= 0)
            return new List<Booking>();

        return await _bookingRepository.Table
            .Where(booking => booking.VendorId == vendorId)
            .OrderByDescending(booking => booking.StartUtc)
            .ToListAsync();
    }

    public async Task<Booking> GetBookingByIdAsync(int bookingId)
    {
        return bookingId <= 0 ? null : await _bookingRepository.GetByIdAsync(bookingId);
    }

    #endregion
}
