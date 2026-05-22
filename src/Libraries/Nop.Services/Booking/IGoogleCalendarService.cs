using System.Threading.Tasks;

namespace Nop.Services.Booking;

public interface IGoogleCalendarService
{
    /// <summary>
    /// Exchanges an authorization code for Google tokens.
    /// </summary>
    Task<(string accessToken, string refreshToken, string email, System.DateTime? expiryUtc)> ExchangeCodeForTokensAsync(string code, string redirectUri, string clientId, string clientSecret);
}