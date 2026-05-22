using System;
using System.Threading.Tasks;
using Nop.Core.Domain.Booking;
using Nop.Data;
using System.Linq;

namespace Nop.Services.Booking;

public class BookingService : IBookingService
{
    private readonly IRepository<BookingIntegrationToken> _tokenRepository;

    public BookingService(IRepository<BookingIntegrationToken> tokenRepository)
    {
        _tokenRepository = tokenRepository;
    }

    public async Task<BookingIntegrationToken> GetTokenByVendorIdAsync(int vendorId)
    {
        if (vendorId <= 0) return null;
        var list = await _tokenRepository.GetAllAsync(q => q.Where(t => t.VendorId == vendorId && t.IsActive));
        return list.FirstOrDefault();
    }

    public async Task SaveTokenAsync(BookingIntegrationToken token)
    {
        if (token == null) throw new ArgumentNullException(nameof(token));

        if (token.Id == 0)
        {
            token.CreatedOnUtc = DateTime.UtcNow;
            token.UpdatedOnUtc = DateTime.UtcNow;
            await _tokenRepository.InsertAsync(token);
        }
        else
        {
            token.UpdatedOnUtc = DateTime.UtcNow;
            await _tokenRepository.UpdateAsync(token);
        }
    }

    public async Task DeleteTokenAsync(int id)
    {
        var token = await _tokenRepository.GetByIdAsync(id);
        if (token != null)
            await _tokenRepository.DeleteAsync(token);
    }
}
