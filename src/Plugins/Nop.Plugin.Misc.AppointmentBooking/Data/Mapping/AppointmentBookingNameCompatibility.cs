using Nop.Data.Mapping;
using Nop.Plugin.Misc.AppointmentBooking.Domains;

namespace Nop.Plugin.Misc.AppointmentBooking.Data.Mapping;

/// <summary>
/// Plugin table naming compatibility
/// </summary>
public class AppointmentBookingNameCompatibility : INameCompatibility
{
    public Dictionary<Type, string> TableNames => new()
    {
        [typeof(BookableService)] = "AppointmentBooking_Service",
        [typeof(ServiceProductMapping)] = "AppointmentBooking_ServiceProductMapping",
        [typeof(AvailabilityRule)] = "AppointmentBooking_AvailabilityRule",
        [typeof(AvailabilityException)] = "AppointmentBooking_AvailabilityException",
        [typeof(Booking)] = "AppointmentBooking_Booking",
        [typeof(BookingParticipant)] = "AppointmentBooking_BookingParticipant",
        [typeof(ServiceQuestion)] = "AppointmentBooking_ServiceQuestion",
        [typeof(BookingAnswer)] = "AppointmentBooking_BookingAnswer",
        [typeof(NotificationLog)] = "AppointmentBooking_NotificationLog",
        [typeof(TimeSlotHold)] = "AppointmentBooking_TimeSlotHold"
    };

    public Dictionary<(Type, string), string> ColumnName => new();
}
