namespace Nop.Core.Domain.Booking;

using Nop.Core.Configuration;

public class BookingSettings : ISettings
{
    public string GoogleClientId { get; set; }
    public string GoogleClientSecret { get; set; }
}
