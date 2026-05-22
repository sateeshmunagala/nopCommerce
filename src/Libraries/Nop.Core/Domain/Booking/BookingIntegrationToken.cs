using System;
using Nop.Core;

namespace Nop.Core.Domain.Booking;

public class BookingIntegrationToken : BaseEntity
{
    public int VendorId { get; set; }
    public string GoogleAccountEmail { get; set; }
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public DateTime? TokenExpiryUtc { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
