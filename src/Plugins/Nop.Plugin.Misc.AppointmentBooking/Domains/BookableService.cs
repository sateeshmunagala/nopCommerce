using Nop.Core;

namespace Nop.Plugin.Misc.AppointmentBooking.Domains;

/// <summary>
/// Represents a generic bookable service
/// </summary>
public class BookableService : BaseEntity
{
    public string Name { get; set; }

    public string Description { get; set; }

    public int VendorId { get; set; }

    public int DurationMinutes { get; set; }

    public int BufferBeforeMinutes { get; set; }

    public int BufferAfterMinutes { get; set; }

    public int MinAdvanceBookingHours { get; set; }

    public int MaxAdvanceBookingDays { get; set; }

    public bool IsActive { get; set; }

    public int DisplayOrder { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}
