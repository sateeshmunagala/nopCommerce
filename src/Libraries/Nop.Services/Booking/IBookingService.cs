using System.Threading.Tasks;
using Nop.Core.Domain.Booking;

namespace Nop.Services.Booking;

public interface IBookingService
{
    Task<BookingIntegrationToken> GetTokenByVendorIdAsync(int vendorId);
    Task SaveTokenAsync(BookingIntegrationToken token);
    Task DeleteTokenAsync(int id);
}
