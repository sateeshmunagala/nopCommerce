using Nop.Plugin.Misc.AppointmentBooking.Domains;

namespace Nop.Plugin.Misc.AppointmentBooking.Models.Account;

/// <summary>
/// Represents vendor-managed availability for a service
/// </summary>
public record AvailabilityModel
{
    public int ServiceId { get; set; }

    public string ServiceName { get; set; }

    public IList<AvailabilityRule> Rules { get; set; } = new List<AvailabilityRule>();

    public IList<AvailabilityException> Exceptions { get; set; } = new List<AvailabilityException>();

    public int DayOfWeek { get; set; }

    public string StartTime { get; set; }

    public string EndTime { get; set; }

    public string TimeZoneId { get; set; } = "UTC";

    public bool IsActive { get; set; } = true;

    public DateTime ExceptionDateUtc { get; set; } = DateTime.UtcNow.Date;

    public bool IsAvailable { get; set; }

    public string Reason { get; set; }
}
