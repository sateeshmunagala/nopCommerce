using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Nop.Web.Framework.Models;
using Nop.Plugin.Misc.WhatsAppBusiness.Domain;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Models;

public record ConfigurationModel : BaseNopModel
{
	public string? ApiKey { get; set; }
	public string? PhoneNumberId { get; set; }
	public string? BusinessAccountId { get; set; }
	public string? AppId { get; set; }
	public string? AppSecret { get; set; }
	public string? ApiVersion { get; set; }
	public bool IsEnabled { get; set; }
	public bool EnableOrderPlaced { get; set; }
	public bool EnableOrderProcessing { get; set; }
	public bool EnableShipmentCreated { get; set; }
	public bool EnableShipmentDelivered { get; set; }
	public bool EnableOrderCancelled { get; set; }
	public bool EnableRefundIssued { get; set; }
	public bool UseTemplateMessages { get; set; }
	public string? DefaultLanguageCode { get; set; }
	public string? OrderConfirmationTemplateName { get; set; }
	public string? ShipmentTrackingTemplateName { get; set; }
	public string? DeliveryConfirmationTemplateName { get; set; }
	public string? ApplicantInterviewCompletionTemplateName { get; set; }
	public string? VendorInterviewCompletionTemplateName { get; set; }
	public string? InterviewReportSharingTemplateName { get; set; }
	public string? OtpTemplateName { get; set; }
	[Range(1, int.MaxValue)]
	public int PollingIntervalSeconds { get; set; }
	public int MinDelayBetweenSendsSeconds { get; set; }
	public int MaxDelayBetweenSendsSeconds { get; set; }
	public int MaxMessagesPerBatch { get; set; }
	public int LookbackWindowDays { get; set; }
	public string? WebhookVerifyToken { get; set; }
	public string? WebhookUrl { get; set; }
	public string? DefaultTrackingUrlPattern { get; set; }
	public string? CarrierTrackingUrls { get; set; }
	public bool ShowOptInOnCheckoutCompleted { get; set; }
	public bool ShowTrackingOnOrderDetails { get; set; }
	public bool RequireCustomerAccount { get; set; }
	public IList<WhatsAppMessageLog> RecentLogs { get; set; } = new List<WhatsAppMessageLog>();
	public IList<WhatsAppBlacklist> BlacklistedNumbers { get; set; } = new List<WhatsAppBlacklist>();
}
