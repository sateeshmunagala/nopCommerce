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
    /// Gets or sets the default booking duration in minutes
    /// </summary>
    public int DefaultDurationMinutes { get; set; }

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
    /// Gets or sets the vendor name
    /// </summary>
    public string VendorName { get; set; }

    /// <summary>
    /// Gets or sets the vendor image URL
    /// </summary>
    public string VendorImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the vendor image alternate text
    /// </summary>
    public string VendorImageAlt { get; set; }

    /// <summary>
    /// Gets or sets available slots
    /// </summary>
    public IList<AvailableSlotModel> AvailableSlots { get; set; } = new List<AvailableSlotModel>();

    /// <summary>
    /// Gets or sets intake questions
    /// </summary>
    public IList<ServiceQuestionModel> Questions { get; set; } = new List<ServiceQuestionModel>();

}
