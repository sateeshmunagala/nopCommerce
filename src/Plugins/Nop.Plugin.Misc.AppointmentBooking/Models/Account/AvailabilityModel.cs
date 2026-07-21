using Nop.Plugin.Misc.AppointmentBooking.Domains;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Nop.Plugin.Misc.AppointmentBooking.Models.Account;

/// <summary>
/// Represents vendor-managed availability for a service
/// </summary>
public record AvailabilityModel
{
    public int VendorId { get; set; }

    public int ServiceId { get; set; }

    public string ServiceName { get; set; }

    public int ActiveServiceCount { get; set; }

    public IList<ScheduleDayModel> Schedule { get; set; } = new List<ScheduleDayModel>();

    public IList<SelectListItem> TimeOptions { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableTimeZones { get; set; } = new List<SelectListItem>();

    public IList<BlockedDateModel> BlockedDates { get; set; } = new List<BlockedDateModel>();

    public string UnavailableDateValues { get; set; }

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

public record ScheduleDayModel
{
    public int DayOfWeek { get; set; }

    public bool Enabled { get; set; }

    public string StartTime { get; set; }

    public string EndTime { get; set; }
}

public record BlockedDateModel
{
    public DateTime Date { get; set; }

    public string DateText { get; set; }

    public string Reason { get; set; }
}
