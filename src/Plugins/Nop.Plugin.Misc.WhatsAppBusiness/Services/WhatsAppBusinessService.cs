using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Nop.Core.Caching;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Services.Logging;
using Nop.Plugin.Misc.WhatsAppBusiness.Domain;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Services;

public class WhatsAppBusinessService : IWhatsAppBusinessService
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly IHttpClientFactory _httpClientFactory;

	private readonly IRepository<WhatsAppBlacklist> _blacklistRepository;

	private readonly IRepository<WhatsAppMessageLog> _logRepository;

	private readonly WhatsAppBusinessSettings _settings;

	private readonly ILogger? _logger;

	public WhatsAppBusinessService(IHttpClientFactory httpClientFactory, IRepository<WhatsAppBlacklist> blacklistRepository, IRepository<WhatsAppMessageLog> logRepository, WhatsAppBusinessSettings settings, ILogger? logger = null)
	{
		_httpClientFactory = httpClientFactory;
		_blacklistRepository = blacklistRepository;
		_logRepository = logRepository;
		_settings = settings;
		_logger = logger;
	}

	public async Task<bool> SendMessageAsync(int orderId, int customerId, string phoneNumber, string messageType, string messageBody, string? trackingNumber = null)
	{
		var payload = new
		{
			messaging_product = "whatsapp",
			to = phoneNumber,
			type = "text",
			text = new
			{
				body = messageBody
			}
		};
		return await SendPayloadAsync(orderId, customerId, phoneNumber, messageType, payload, trackingNumber, null);
	}

	public async Task<bool> SendTemplateMessageAsync(int orderId, int customerId, string phoneNumber, string messageType, string templateName, string languageCode, IList<string> bodyParameters, string? trackingNumber = null)
	{
		var parameters = (bodyParameters ?? new List<string>()).Select((string p) => new
		{
			type = "text",
			text = p
		}).ToArray();
		var payload = new
		{
			messaging_product = "whatsapp",
			to = phoneNumber,
			type = "template",
			template = new
			{
				name = templateName,
				language = new
				{
					code = languageCode
				},
				components = new[]
				{
					new
					{
						type = "body",
						parameters = parameters
					}
				}
			}
		};
		return await SendPayloadAsync(orderId, customerId, phoneNumber, messageType, payload, trackingNumber, templateName);
	}

	public async Task<bool> IsBlacklistedAsync(string phoneNumber)
	{
		return await AsyncIQueryableExtensions.AnyAsync<WhatsAppBlacklist>(_blacklistRepository.Table, (Expression<Func<WhatsAppBlacklist, bool>>)((WhatsAppBlacklist b) => b.PhoneNumber == phoneNumber));
	}

	public async Task<IList<WhatsAppMessageLog>> GetRecentLogsAsync(int count = 50)
	{
		return await AsyncIQueryableExtensions.ToListAsync<WhatsAppMessageLog>(_logRepository.Table.OrderByDescending((WhatsAppMessageLog l) => l.SentAt).Take(count));
	}

	public async Task<IList<WhatsAppMessageLog>> GetOrderLogsAsync(int orderId)
	{
		return await AsyncIQueryableExtensions.ToListAsync<WhatsAppMessageLog>((IQueryable<WhatsAppMessageLog>)(from l in _logRepository.Table
			where l.OrderId == orderId
			orderby l.SentAt
			select l));
	}

	public async Task UpdateMessageStatusAsync(string whatsAppMessageId, string newStatus)
	{
		if (!string.IsNullOrWhiteSpace(whatsAppMessageId))
		{
			WhatsAppMessageLog whatsAppMessageLog = await AsyncIQueryableExtensions.FirstOrDefaultAsync<WhatsAppMessageLog>(_logRepository.Table, (Expression<Func<WhatsAppMessageLog, bool>>)((WhatsAppMessageLog l) => l.WhatsAppMessageId == whatsAppMessageId));
			if (whatsAppMessageLog != null)
			{
				whatsAppMessageLog.Status = newStatus;
				await _logRepository.UpdateAsync(whatsAppMessageLog, true);
			}
		}
	}

	public async Task AddToBlacklistAsync(int customerId, string phoneNumber, string reason)
	{
		if (await AsyncIQueryableExtensions.FirstOrDefaultAsync<WhatsAppBlacklist>(_blacklistRepository.Table, (Expression<Func<WhatsAppBlacklist, bool>>)((WhatsAppBlacklist b) => b.PhoneNumber == phoneNumber)) == null)
		{
			IRepository<WhatsAppBlacklist> blacklistRepository = _blacklistRepository;
			WhatsAppBlacklist obj = new WhatsAppBlacklist
			{
				CustomerId = customerId,
				PhoneNumber = phoneNumber,
				FailedAt = DateTime.UtcNow
			};
			obj.Reason = ((reason != null && reason.Length > 500) ? reason.Substring(0, 500) : reason);
			await blacklistRepository.InsertAsync(obj, true);
		}
	}

	public async Task RemoveFromBlacklistAsync(int blacklistId)
	{
		WhatsAppBlacklist whatsAppBlacklist = await _blacklistRepository.GetByIdAsync(blacklistId);
		if (whatsAppBlacklist != null)
		{
			await _blacklistRepository.DeleteAsync(whatsAppBlacklist, true);
		}
	}

	public async Task<IList<WhatsAppBlacklist>> GetBlacklistAsync()
	{
		return await AsyncIQueryableExtensions.ToListAsync<WhatsAppBlacklist>((IQueryable<WhatsAppBlacklist>)_blacklistRepository.Table.OrderByDescending((WhatsAppBlacklist b) => b.FailedAt));
	}

	public async Task<bool> HasBeenNotifiedAsync(int orderId, string messageType)
	{
		return await AsyncIQueryableExtensions.AnyAsync<WhatsAppMessageLog>(_logRepository.Table, (Expression<Func<WhatsAppMessageLog, bool>>)((WhatsAppMessageLog l) => l.OrderId == orderId && l.MessageType == messageType && l.Status != "Failed"));
	}

	private async Task<bool> SendPayloadAsync(int orderId, int customerId, string phoneNumber, string messageType, object payload, string? trackingNumber, string? templateUsed)
	{
		if (!_settings.IsEnabled)
			return false;

		string correlationId = ((orderId > 0) ? orderId.ToString() : Guid.NewGuid().ToString("N").Substring(0, 8));
		string maskedTo = WhatsAppLogHelper.MaskPhone(phoneNumber);
		if (string.IsNullOrWhiteSpace(phoneNumber))
		{
			await LogWarningAsync($"[{correlationId}] WhatsApp {messageType} send skipped: recipient phone number is missing.");
			await WriteLogAsync(orderId, customerId, phoneNumber, messageType, "Failed", "Recipient phone number is missing.", trackingNumber, null, templateUsed);
			return false;
		}
		if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.PhoneNumberId))
		{
			await LogWarningAsync($"[{correlationId}] WhatsApp {messageType} send skipped for {maskedTo}: Meta Cloud API " + "credentials not configured (access token / phone-number-id missing).");
			await WriteLogAsync(orderId, customerId, phoneNumber, messageType, "Failed", "Meta Cloud API credentials not configured.", trackingNumber, null, templateUsed);
			return false;
		}
		string arg = (string.IsNullOrWhiteSpace(_settings.ApiVersion) ? "v23.0" : _settings.ApiVersion);
		string url = string.Format(WhatsAppBusinessDefaults.ApiUrlTemplate, arg, _settings.PhoneNumberId);
		await LogInformationAsync($"[{correlationId}] Sending WhatsApp {messageType} message to {maskedTo} via Meta Cloud API (order {orderId}).");
		HttpResponseMessage response;
		string responseBody;
		try
		{
			string content = JsonSerializer.Serialize(payload, JsonOptions);
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url)
			{
				Content = new StringContent(content, Encoding.UTF8, "application/json")
			};
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
			response = await _httpClientFactory.CreateClient(WhatsAppBusinessDefaults.HttpClientName).SendAsync(request);
			responseBody = await response.Content.ReadAsStringAsync();
		}
		catch (TaskCanceledException exception)
		{
			await LogErrorAsync($"[{correlationId}] WhatsApp API request timed out sending {messageType} to {maskedTo}.", exception);
			await WriteLogAsync(orderId, customerId, phoneNumber, messageType, "Failed", "Request timed out.", trackingNumber, null, templateUsed);
			return false;
		}
		catch (HttpRequestException exception2)
		{
			await LogErrorAsync($"[{correlationId}] Network error calling WhatsApp API for {messageType} to {maskedTo}.", exception2);
			await WriteLogAsync(orderId, customerId, phoneNumber, messageType, "Failed", "Network error calling WhatsApp API.", trackingNumber, null, templateUsed);
			return false;
		}
		catch (Exception exception3)
		{
			await LogErrorAsync($"[{correlationId}] Unexpected error sending WhatsApp {messageType} to {maskedTo}.", exception3);
			await WriteLogAsync(orderId, customerId, phoneNumber, messageType, "Failed", "Unexpected error sending WhatsApp message.", trackingNumber, null, templateUsed);
			return false;
		}
		if (response.IsSuccessStatusCode)
		{
			string? waMessageId = ExtractMessageId(responseBody);
			await LogInformationAsync($"[{correlationId}] WhatsApp API accepted {messageType} to {maskedTo}. Status {(int)response.StatusCode}. Meta message id: {waMessageId ?? "(none)"}.");
			await WriteLogAsync(orderId, customerId, phoneNumber, messageType, "Sent", null, trackingNumber, waMessageId, templateUsed);
			return true;
		}
		int statusCode = (int)response.StatusCode;
		string text = ((statusCode >= 500) ? "Meta server error" : (statusCode switch
		{
			401 => "authentication failed (access token invalid or expired)", 
			403 => "authorization failed (permission / phone-number-id)", 
			429 => "rate limit exceeded", 
			_ => "request rejected (e.g. invalid recipient, template not approved, or outside 24h window)", 
		}));
		string value = text;
		await LogWarningAsync($"[{correlationId}] WhatsApp API rejected {messageType} to {maskedTo}: {value}. Status {(int)response.StatusCode} ({response.StatusCode}). Response: {Truncate(responseBody)}");
		if (response.StatusCode >= HttpStatusCode.BadRequest && response.StatusCode < HttpStatusCode.InternalServerError && response.StatusCode != HttpStatusCode.TooManyRequests)
		{
			await AddToBlacklistAsync(customerId, phoneNumber, responseBody);
		}
		await WriteLogAsync(orderId, customerId, phoneNumber, messageType, "Failed", responseBody, trackingNumber, null, templateUsed);
		return false;
	}

	private static string? ExtractMessageId(string responseBody)
	{
		if (string.IsNullOrWhiteSpace(responseBody))
		{
			return null;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(responseBody);
			if (jsonDocument.RootElement.ValueKind == JsonValueKind.Object && jsonDocument.RootElement.TryGetProperty("messages", out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 && value[0].TryGetProperty("id", out var value2))
			{
				return value2.GetString();
			}
		}
		catch (JsonException)
		{
		}
		return null;
	}

	private static string Truncate(string value, int max = 500)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}
		if (value.Length > max)
		{
			return value.Substring(0, max) + "…";
		}
		return value;
	}

	private async Task WriteLogAsync(int orderId, int customerId, string phoneNumber, string messageType, string status, string? error, string? trackingNumber = null, string? whatsAppMessageId = null, string? templateUsed = null)
	{
		try
		{
			IRepository<WhatsAppMessageLog> logRepository = _logRepository;
			WhatsAppMessageLog obj = new WhatsAppMessageLog
			{
				OrderId = orderId,
				CustomerId = customerId,
				PhoneNumber = phoneNumber,
				MessageType = messageType,
				Status = status,
				SentAt = DateTime.UtcNow
			};
			obj.Error = ((error != null && error.Length > 1000) ? error.Substring(0, 1000) : error);
			obj.TrackingNumber = trackingNumber;
			obj.WhatsAppMessageId = whatsAppMessageId;
			obj.TemplateUsed = templateUsed;
			await logRepository.InsertAsync(obj, true);
		}
		catch (Exception exception)
		{
			await LogErrorAsync("[" + ((orderId > 0) ? orderId.ToString() : "-") + "] Failed to persist WhatsApp message log entry.", exception);
		}
	}

	private async Task LogInformationAsync(string message)
	{
		if (_logger != null)
		{
			await _logger.InformationAsync(message);
		}
	}

	private async Task LogWarningAsync(string message)
	{
		if (_logger != null)
		{
			await _logger.WarningAsync(message);
		}
	}

	private async Task LogErrorAsync(string message, Exception exception)
	{
		if (_logger != null)
		{
			await _logger.ErrorAsync(message, exception);
		}
	}
}
