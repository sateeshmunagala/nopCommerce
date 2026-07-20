using Nop.Core;

namespace Nop.Plugin.Misc.AppointmentBooking.Domains;

/// <summary>
/// Represents an additional booking participant
/// </summary>
public class BookingParticipant : BaseEntity
{
    public int BookingId { get; set; }

    public string Name { get; set; }

    public string Email { get; set; }

    public string ParticipantType { get; set; }

    public DateTime CreatedOnUtc { get; set; }
}
