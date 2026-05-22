using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Nop.Services.Booking;

public class GoogleCalendarService : IGoogleCalendarService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public GoogleCalendarService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<(string accessToken, string refreshToken, string email, DateTime? expiryUtc)> ExchangeCodeForTokensAsync(string code, string redirectUri, string clientId, string clientSecret)
    {
        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("grant_type", "authorization_code")
        });
        request.Content = content;
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var accessToken = root.GetProperty("access_token").GetString();
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        var expiryUtc = DateTime.UtcNow.AddSeconds(expiresIn);

        // Get user email from id_token (JWT)
        string email = null;
        if (root.TryGetProperty("id_token", out var idTokenProp))
        {
            var idToken = idTokenProp.GetString();
            if (!string.IsNullOrEmpty(idToken))
            {
                var parts = idToken.Split('.');
                if (parts.Length == 3)
                {
                    var payload = parts[1];
                    var pad = 4 - (payload.Length % 4);
                    if (pad < 4) payload += new string('=', pad);
                    var bytes = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/'));
                    var payloadJson = System.Text.Encoding.UTF8.GetString(bytes);
                    using var payloadDoc = JsonDocument.Parse(payloadJson);
                    if (payloadDoc.RootElement.TryGetProperty("email", out var emailProp))
                        email = emailProp.GetString();
                }
            }
        }
        return (accessToken, refreshToken, email, expiryUtc);
    }
}