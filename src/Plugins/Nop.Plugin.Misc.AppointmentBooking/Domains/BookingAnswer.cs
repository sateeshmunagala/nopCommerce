using Nop.Core;

namespace Nop.Plugin.Misc.AppointmentBooking.Domains;

/// <summary>
/// Represents an answer captured for a booking
/// </summary>
public class BookingAnswer : BaseEntity
{
    public int BookingId { get; set; }

    public int ServiceQuestionId { get; set; }

    public string AnswerText { get; set; }

    public DateTime CreatedOnUtc { get; set; }
}
