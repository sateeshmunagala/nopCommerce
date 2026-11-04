using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Net.Http.Headers;
using Nop.Core;

namespace Nop.Plugin.Payments.Razorpay.Services;

public class RazorpayHttpClient
{
    private readonly HttpClient _httpClient;

    public RazorpayHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.razorpay.com/v1/");
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.UserAgent, $"nopCommerce-{NopVersion.CURRENT_VERSION}");
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept, "application/json");
    }

    public async Task<string> CreateOrderAsync(string keyId, string keySecret, decimal amountInSubunits, string currency, string receiptId, bool paymentCapture)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "orders");
        
        var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{keyId}:{keySecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authString);

        var payload = new
        {
            amount = Math.Round(amountInSubunits, 0),
            currency = currency,
            receipt = receiptId,
            payment_capture = paymentCapture ? 1 : 0
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        using var jsonDocument = JsonDocument.Parse(responseContent);
        
        if (jsonDocument.RootElement.TryGetProperty("id", out var idElement))
        {
            return idElement.GetString() ?? string.Empty;
        }

        throw new Exception("Razorpay order creation failed: 'id' not found in response.");
    }

    public async Task<Nop.Plugin.Payments.Razorpay.Models.RazorpayPaymentDto> GetPaymentAsync(string keyId, string keySecret, string paymentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"payments/{paymentId}");

        var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{keyId}:{keySecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authString);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        using var jsonDocument = JsonDocument.Parse(responseContent);

        var dto = new Nop.Plugin.Payments.Razorpay.Models.RazorpayPaymentDto();

        var root = jsonDocument.RootElement;

        if (root.TryGetProperty("status", out var statusElement))
        {
            dto.Status = statusElement.GetString() ?? string.Empty;
        }
        if (root.TryGetProperty("order_id", out var orderIdElement))
        {
            dto.OrderId = orderIdElement.GetString() ?? string.Empty;
        }
        if (root.TryGetProperty("amount", out var amountElement) && amountElement.TryGetDecimal(out var amount))
        {
            dto.Amount = amount;
        }
        if (root.TryGetProperty("currency", out var currencyElement))
        {
            dto.Currency = currencyElement.GetString() ?? string.Empty;
        }

        return dto;
    }

    public bool VerifySignature(string orderId, string paymentId, string signature, string keySecret)
    {
        var payload = $"{orderId}|{paymentId}";
        var secretBytes = Encoding.UTF8.GetBytes(keySecret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(secretBytes);
        var hashBytes = hmac.ComputeHash(payloadBytes);
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hashString),
            Encoding.UTF8.GetBytes(signature));
    }
}
