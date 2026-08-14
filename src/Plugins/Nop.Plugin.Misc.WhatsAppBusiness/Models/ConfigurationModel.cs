using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Nop.Web.Framework.Models;
using SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Domain;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Models;

public record ConfigurationModel : BaseNopModel
{
	public string ApiKey { get; set; } = string.Empty;
	public string PhoneNumberId { get; set; } = string.Empty;
	public string BusinessAccountId { get; set; } = string.Empty;
	public string AppId { get; set; } = string.Empty;
	public string AppSecret { get; set; } = string.Empty;
	public string ApiVersion { get; set; } = string.Empty;
	public bool IsEnabled { get; set; }
	public bool EnableOrderPlaced { get; set; }
	public bool EnableOrderProcessing { get; set; }
	public bool EnableShipmentCreated { get; set; }
	public bool EnableShipmentDelivered { get; set; }
	public bool EnableOrderCancelled { get; set; }
	public bool EnableRefundIssued { get; set; }
	public bool UseTemplateMessages { get; set; }
	public string DefaultLanguageCode { get; set; } = string.Empty;
	public string OrderConfirmationTemplateName { get; set; } = string.Empty;
	public string ShipmentTrackingTemplateName { get; set; } = string.Empty;
	public string DeliveryConfirmationTemplateName { get; set; } = string.Empty;
	[Range(1, int.MaxValue)]
	public int PollingIntervalSeconds { get; set; }
	public int MinDelayBetweenSendsSeconds { get; set; }
	public int MaxDelayBetweenSendsSeconds { get; set; }
	public int MaxMessagesPerBatch { get; set; }
	public int LookbackWindowDays { get; set; }
	public string WebhookVerifyToken { get; set; } = string.Empty;
	public string WebhookUrl { get; set; } = string.Empty;
	public string DefaultTrackingUrlPattern { get; set; } = string.Empty;
	public string CarrierTrackingUrls { get; set; } = string.Empty;
	public bool ShowOptInOnCheckoutCompleted { get; set; }
	public bool ShowTrackingOnOrderDetails { get; set; }
	public bool RequireCustomerAccount { get; set; }
	public IList<WhatsAppMessageLog> RecentLogs { get; set; } = new List<WhatsAppMessageLog>();
	public IList<WhatsAppBlacklist> BlacklistedNumbers { get; set; } = new List<WhatsAppBlacklist>();
}
