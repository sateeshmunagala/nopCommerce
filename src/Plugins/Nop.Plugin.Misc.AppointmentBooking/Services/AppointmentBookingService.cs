using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Misc.AppointmentBooking.Domains;
using Nop.Plugin.Misc.AppointmentBooking.Models;
using Nop.Services.Catalog;

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
    private readonly IPriceFormatter _priceFormatter;
    private readonly IProductService _productService;
    private readonly IRepository<ServiceProductMapping> _serviceProductMappingRepository;
    private readonly IRepository<ServiceQuestion> _serviceQuestionRepository;
    private readonly IRepository<TimeSlotHold> _timeSlotHoldRepository;

    #endregion

    #region Ctor

    public AppointmentBookingService(AppointmentBookingSettings appointmentBookingSettings,
        IRepository<AvailabilityException> availabilityExceptionRepository,
        IRepository<AvailabilityRule> availabilityRuleRepository,
        IRepository<BookingAnswer> bookingAnswerRepository,
        IRepository<Booking> bookingRepository,
        IRepository<BookableService> bookableServiceRepository,
        IPriceFormatter priceFormatter,
        IProductService productService,
        IRepository<ServiceProductMapping> serviceProductMappingRepository,
        IRepository<ServiceQuestion> serviceQuestionRepository,
        IRepository<TimeSlotHold> timeSlotHoldRepository)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _availabilityExceptionRepository = availabilityExceptionRepository;
        _availabilityRuleRepository = availabilityRuleRepository;
        _bookingAnswerRepository = bookingAnswerRepository;
        _bookingRepository = bookingRepository;
        _bookableServiceRepository = bookableServiceRepository;
        _priceFormatter = priceFormatter;
        _productService = productService;
        _serviceProductMappingRepository = serviceProductMappingRepository;
        _serviceQuestionRepository = serviceQuestionRepository;
        _timeSlotHoldRepository = timeSlotHoldRepository;
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
        var toUtc = DateTime.UtcNow.AddDays(service.MaxAdvanceBookingDays > 0 ? service.MaxAdvanceBookingDays : 14);
        var slots = await GenerateAvailableSlotsAsync(service.Id, fromUtc, toUtc);
        var questions = await GetServiceQuestionsAsync(service.Id);

        return new ProductAppointmentBookingModel
        {
            Id = product.Id,
            ProductName = product.Name,
            ShortDescription = product.ShortDescription,
            Price = product.Price > decimal.Zero ? await _priceFormatter.FormatPriceAsync(product.Price) : string.Empty,
            BookingUrl = string.Empty,
            DefaultDurationMinutes = service.DurationMinutes,
            AllowCalendarIframe = false,
            CalendarProvider = string.Empty,
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

        return service;
    }

    public async Task<ServiceProductMapping> MapServiceToProductAsync(int serviceId, int productId, int vendorId)
    {
        if (serviceId <= 0 || productId <= 0)
            return null;

        var now = DateTime.UtcNow;
        var activeMappings = await _serviceProductMappingRepository.Table
            .Where(mapping => mapping.ProductId == productId && mapping.IsActive)
            .ToListAsync();

        foreach (var mapping in activeMappings)
        {
            if (mapping.ServiceId == serviceId)
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

                    slots.Add(new AvailableSlotModel
                    {
                        StartUtc = slotStartUtc,
                        EndUtc = slotEndUtc,
                        DisplayText = $"{slotStartUtc:yyyy-MM-dd HH:mm} UTC"
                    });
                }
            }
        }

        return slots.OrderBy(slot => slot.StartUtc).Take(50).ToList();
    }

    public async Task<TimeSlotHold> CreateTimeSlotHoldAsync(int serviceId, int productId, int customerId, DateTime startUtc)
    {
        var service = await GetServiceByIdAsync(serviceId);
        if (service == null || !service.IsActive)
            return null;

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
        return hold;
    }

    public async Task<int> ReleaseExpiredHoldsAsync()
    {
        var expiredHolds = await _timeSlotHoldRepository.Table
            .Where(hold => hold.ExpiresOnUtc <= DateTime.UtcNow)
            .ToListAsync();

        foreach (var hold in expiredHolds)
            await _timeSlotHoldRepository.DeleteAsync(hold);

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

        var booking = new Booking
        {
            ServiceId = hold.ServiceId,
            ProductId = hold.ProductId,
            VendorId = hold.VendorId,
            CustomerId = hold.CustomerId,
            OrderId = order.Id,
            OrderItemId = orderItem.Id,
            StartUtc = hold.StartUtc,
            EndUtc = hold.EndUtc,
            CustomerTimeZoneId = "UTC",
            Status = status,
            CreatedOnUtc = DateTime.UtcNow,
            UpdatedOnUtc = DateTime.UtcNow
        };

        await _bookingRepository.InsertAsync(booking);
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

    public async Task<Booking> GetBookingByIdAsync(int bookingId)
    {
        return bookingId <= 0 ? null : await _bookingRepository.GetByIdAsync(bookingId);
    }

    #endregion
}
