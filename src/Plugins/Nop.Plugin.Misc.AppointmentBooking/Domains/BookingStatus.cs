namespace Nop.Plugin.Misc.AppointmentBooking.Domains;

/// <summary>
/// Appointment booking statuses
/// </summary>
public static class BookingStatus
{
    public const string PendingCheckout = "PendingCheckout";
    public const string PendingPayment = "PendingPayment";
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
    public const string Completed = "Completed";
    public const string NoShow = "NoShow";
}
