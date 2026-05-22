using System;
using Nop.Core;

namespace Nop.Core.Domain.Booking;

public class BookingProductMapping : BaseEntity
{
    public int ProductId { get; set; }
    public int VendorId { get; set; }
    public string GoogleBookingUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
