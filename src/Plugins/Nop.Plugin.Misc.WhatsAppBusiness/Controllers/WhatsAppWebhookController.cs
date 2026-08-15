using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Customers;
using Nop.Services.Logging;
using Nop.Plugin.Misc.WhatsAppBusiness.Services;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Controllers;

[Route("api/whatsapp")]
[ApiController]
public class WhatsAppWebhookController : ControllerBase
{
	private readonly WhatsAppBusinessSettings _settings;

	private readonly IWhatsAppBusinessService _whatsAppService;

	private readonly ILogger _logger;

	public WhatsAppWebhookController(WhatsAppBusinessSettings settings, IWhatsAppBusinessService whatsAppService, ILogger logger)
	{
		_settings = settings;
		_whatsAppService = whatsAppService;
		_logger = logger;
	}

	[HttpGet("webhook")]
	public async Task<IActionResult> Verify([FromQuery(Name = "hub.mode")] string mode, [FromQuery(Name = "hub.verify_token")] string verifyToken, [FromQuery(Name = "hub.challenge")] string challenge)
	{
		if (string.IsNullOrWhiteSpace(_settings.WebhookVerifyToken))
		{
			await _logger.WarningAsync("WhatsApp webhook verification rejected: verify token is not configured.");
			return StatusCode(403);
		}
		if (mode == "subscribe" && verifyToken == _settings.WebhookVerifyToken)
		{
			await _logger.InformationAsync("WhatsApp webhook verified successfully.");
			return Ok(challenge);
		}
		await _logger.WarningAsync("WhatsApp webhook verification failed: invalid mode or verify token.");
		return StatusCode(403);
	}

	[HttpPost("webhook")]
	public async Task<IActionResult> Receive()
	{
		string correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
		string text;
		using (StreamReader reader = new StreamReader(base.Request.Body, Encoding.UTF8))
		{
			text = await reader.ReadToEndAsync();
		}
		if (string.IsNullOrWhiteSpace(_settings.AppSecret))
		{
			await _logger.WarningAsync("[" + correlationId + "] WhatsApp webhook rejected: app secret not configured, cannot verify signature.");
			return Unauthorized();
		}
		string signatureHeader = base.Request.Headers["X-Hub-Signature-256"].ToString();
		if (!VerifySignature(text, signatureHeader, _settings.AppSecret))
		{
			await _logger.WarningAsync("[" + correlationId + "] WhatsApp webhook rejected: missing or invalid X-Hub-Signature-256 signature.");
			return Unauthorized();
		}
		int updated;
		try
		{
			updated = await ProcessWebhookPayloadAsync(text);
		}
		catch (JsonException)
		{
			await _logger.WarningAsync("[" + correlationId + "] WhatsApp webhook payload was malformed JSON and was ignored.");
			return Ok();
		}
		catch (Exception ex2)
		{
			await _logger.ErrorAsync("[" + correlationId + "] Error processing WhatsApp webhook payload.", ex2);
			return Ok();
		}
		await _logger.InformationAsync($"[{correlationId}] WhatsApp webhook processed: {updated} message status update(s) applied.");
		return Ok();
	}

	private async Task<int> ProcessWebhookPayloadAsync(string body)
	{
		int updated = 0;
		using JsonDocument doc = JsonDocument.Parse(body);
		JsonElement rootElement = doc.RootElement;
		if (rootElement.ValueKind != JsonValueKind.Object || !rootElement.TryGetProperty("entry", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return updated;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (!item.TryGetProperty("changes", out var value2) || value2.ValueKind != JsonValueKind.Array)
			{
				continue;
			}
			foreach (JsonElement item2 in value2.EnumerateArray())
			{
				if (!item2.TryGetProperty("value", out var value3) || !value3.TryGetProperty("statuses", out var value4) || value4.ValueKind != JsonValueKind.Array)
				{
					continue;
				}
				foreach (JsonElement item3 in value4.EnumerateArray())
				{
					string? text = (item3.TryGetProperty("id", out var value5) ? value5.GetString() : null);
					string? text2 = (item3.TryGetProperty("status", out var value6) ? value6.GetString() : null);
					if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2))
					{
						string? text3 = text2 switch
						{
							"sent" => "Sent", 
							"delivered" => "Delivered", 
							"read" => "Read", 
							"failed" => "Failed", 
							_ => null, 
						};
						if (text3 != null)
						{
							await _whatsAppService.UpdateMessageStatusAsync(text, text3);
							updated++;
						}
					}
				}
			}
		}
		return updated;
	}

	private static bool VerifySignature(string payload, string signatureHeader, string appSecret)
	{
		if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256="))
		{
			return false;
		}
		string text = signatureHeader.Substring("sha256=".Length);
		byte[] bytes = Encoding.UTF8.GetBytes(appSecret);
		byte[] bytes2 = Encoding.UTF8.GetBytes(payload);
		using HMACSHA256 hMACSHA = new HMACSHA256(bytes);
		string s = Convert.ToHexStringLower(hMACSHA.ComputeHash(bytes2));
		return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s), Encoding.ASCII.GetBytes(text.ToLowerInvariant()));
	}
}
