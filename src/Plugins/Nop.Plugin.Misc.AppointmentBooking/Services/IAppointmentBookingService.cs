using Nop.Plugin.Misc.AppointmentBooking.Models;
using Nop.Plugin.Misc.AppointmentBooking.Domains;
using Nop.Core.Domain.Orders;

namespace Nop.Plugin.Misc.AppointmentBooking.Services;

/// <summary>
/// Represents appointment booking service
/// </summary>
public interface IAppointmentBookingService
{
    /// <summary>
    /// Determines whether a product should expose appointment booking UI
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task<bool> IsAppointmentProductAsync(int productId);

    /// <summary>
    /// Prepares product booking model
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <param name="customerId">Customer identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task<ProductAppointmentBookingModel> PrepareProductBookingModelAsync(int productId, int customerId);

    Task<IList<BookableService>> GetAllServicesAsync();

    Task<IList<BookableService>> GetServicesByVendorAsync(int vendorId);

    Task<BookableService> GetServiceByIdAsync(int serviceId);

    Task<BookableService> SaveServiceAsync(BookableService service);

    Task<ServiceProductMapping> MapServiceToProductAsync(int serviceId, int productId, int vendorId);

    Task ClearServiceProductMappingsAsync(int serviceId, int vendorId);

    Task<BookableService> GetServiceByProductAsync(int productId);

    Task<ServiceProductMapping> GetActiveProductMappingAsync(int productId);

    Task<ServiceProductMapping> GetActiveProductMappingByServiceAsync(int serviceId);

    Task<IList<AvailabilityRule>> GetAvailabilityRulesAsync(int serviceId);

    Task<AvailabilityRule> SaveAvailabilityRuleAsync(AvailabilityRule rule);

    Task DeleteAvailabilityRulesAsync(int serviceId);

    Task<IList<AvailabilityException>> GetAvailabilityExceptionsAsync(int serviceId);

    Task<AvailabilityException> SaveAvailabilityExceptionAsync(AvailabilityException availabilityException);

    Task DeleteAvailabilityExceptionAsync(int exceptionId);

    Task<IList<ServiceQuestion>> GetServiceQuestionsAsync(int serviceId);

    Task<ServiceQuestion> SaveServiceQuestionAsync(ServiceQuestion question);

    Task<IList<AvailableSlotModel>> GenerateAvailableSlotsAsync(int serviceId, DateTime fromUtc, DateTime toUtc);

    Task<TimeSlotHold> CreateTimeSlotHoldAsync(int serviceId, int productId, int customerId, DateTime startUtc);

    Task<int> ReleaseExpiredHoldsAsync();

    Task<TimeSlotHold> GetActiveHoldForCustomerProductAsync(int customerId, int productId);

    Task<Booking> ConvertHoldToBookingAsync(TimeSlotHold hold, Order order, OrderItem orderItem, string status = BookingStatus.PendingPayment);

    Task ConfirmBookingsForOrderAsync(Order order);

    Task CancelBookingAsync(int bookingId, string reason);

    Task SaveBookingAnswersAsync(int bookingId, IDictionary<int, string> answers);

    Task<IList<Booking>> GetAllBookingsAsync();

    Task<IList<Booking>> GetBookingsByVendorAsync(int vendorId);

    Task<Booking> GetBookingByIdAsync(int bookingId);
}
