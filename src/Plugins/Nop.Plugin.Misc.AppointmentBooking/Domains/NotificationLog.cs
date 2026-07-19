using Nop.Core;

namespace Nop.Plugin.Misc.AppointmentBooking.Domains;

/// <summary>
/// Represents appointment notification tracking
/// </summary>
public class NotificationLog : BaseEntity
{
    public int BookingId { get; set; }

    public string NotificationType { get; set; }

    public string RecipientEmail { get; set; }

    public DateTime? SentOnUtc { get; set; }

    public string Status { get; set; }

    public string ErrorMessage { get; set; }

    public DateTime CreatedOnUtc { get; set; }
}
