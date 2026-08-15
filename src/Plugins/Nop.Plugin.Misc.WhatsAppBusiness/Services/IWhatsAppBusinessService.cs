using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Plugin.Misc.WhatsAppBusiness.Domain;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Services;

public interface IWhatsAppBusinessService
{
	Task<bool> SendMessageAsync(int orderId, int customerId, string phoneNumber, string messageType, string messageBody, string? trackingNumber = null);

	Task<bool> SendTemplateMessageAsync(int orderId, int customerId, string phoneNumber, string messageType, string templateName, string languageCode, IList<string> bodyParameters, string? trackingNumber = null);

	Task<bool> IsBlacklistedAsync(string phoneNumber);

	Task<IList<WhatsAppMessageLog>> GetRecentLogsAsync(int count = 50);

	Task<IList<WhatsAppMessageLog>> GetOrderLogsAsync(int orderId);

	Task UpdateMessageStatusAsync(string whatsAppMessageId, string newStatus);

	Task AddToBlacklistAsync(int customerId, string phoneNumber, string reason);

	Task RemoveFromBlacklistAsync(int blacklistId);

	Task<IList<WhatsAppBlacklist>> GetBlacklistAsync();

	Task<bool> HasBeenNotifiedAsync(int orderId, string messageType);
}
