using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Models;

/// <summary>
/// Represents the public product appointment booking model
/// </summary>
public record ProductAppointmentBookingModel : BaseNopEntityModel
{
    /// <summary>
    /// Gets or sets the product name
    /// </summary>
    public string ProductName { get; set; }

    /// <summary>
    /// Gets or sets the product short description
    /// </summary>
    public string ShortDescription { get; set; }

    /// <summary>
    /// Gets or sets the formatted product price
    /// </summary>
    public string Price { get; set; }

    /// <summary>
    /// Gets or sets the booking URL
    /// </summary>
    public string BookingUrl { get; set; }

    /// <summary>
    /// Gets or sets the default booking duration in minutes
    /// </summary>
    public int DefaultDurationMinutes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the booking URL can be rendered in an iframe
    /// </summary>
    public bool AllowCalendarIframe { get; set; }

    /// <summary>
    /// Gets or sets the calendar provider label
    /// </summary>
    public string CalendarProvider { get; set; }

    /// <summary>
    /// Gets or sets the mapped service identifier
    /// </summary>
    public int ServiceId { get; set; }

    /// <summary>
    /// Gets or sets the service name
    /// </summary>
    public string ServiceName { get; set; }

    /// <summary>
    /// Gets or sets the service description
    /// </summary>
    public string ServiceDescription { get; set; }

    /// <summary>
    /// Gets or sets available slots
    /// </summary>
    public IList<AvailableSlotModel> AvailableSlots { get; set; } = new List<AvailableSlotModel>();

    /// <summary>
    /// Gets or sets intake questions
    /// </summary>
    public IList<ServiceQuestionModel> Questions { get; set; } = new List<ServiceQuestionModel>();

    /// <summary>
    /// Gets a value indicating whether a booking URL exists
    /// </summary>
    public bool HasBookingUrl => !string.IsNullOrWhiteSpace(BookingUrl);
}
