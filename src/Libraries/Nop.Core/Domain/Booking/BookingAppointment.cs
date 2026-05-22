using System;
using Nop.Core;

namespace Nop.Core.Domain.Booking;

public class BookingAppointment : BaseEntity
{
    public int VendorId { get; set; }
    public int ProductId { get; set; }
    public int CustomerId { get; set; }
    public int OrderId { get; set; }
    public string GoogleEventId { get; set; }
    public string JoinLink { get; set; }
    public string BookingStatus { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
