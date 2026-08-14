using Nop.Core.Configuration;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness;

public class WhatsAppBusinessSettings : ISettings
{
	public string ApiKey { get; set; } = string.Empty;

	public string PhoneNumberId { get; set; } = string.Empty;

	public string BusinessAccountId { get; set; } = string.Empty;

	public string AppId { get; set; } = string.Empty;

	public string AppSecret { get; set; } = string.Empty;

	public string ApiVersion { get; set; } = "v23.0";

	public bool IsEnabled { get; set; }

	public bool EnableOrderPlaced { get; set; } = true;

	public bool EnableOrderProcessing { get; set; } = true;

	public bool EnableShipmentCreated { get; set; } = true;

	public bool EnableShipmentDelivered { get; set; } = true;

	public bool EnableOrderCancelled { get; set; }

	public bool EnableRefundIssued { get; set; }

	public bool UseTemplateMessages { get; set; } = true;

	public string DefaultLanguageCode { get; set; } = "pt_BR";

	public string OrderConfirmationTemplateName { get; set; } = "order_confirmation";

	public string ShipmentTrackingTemplateName { get; set; } = "shipment_tracking_notification";

	public string DeliveryConfirmationTemplateName { get; set; } = "delivery_confirmation";

	public string ApplicantInterviewCompletionTemplateName { get; set; } = "aiinterview_applicant_completion";

	public string VendorInterviewCompletionTemplateName { get; set; } = "aiinterview_vendor_completion";

	public string InterviewReportSharingTemplateName { get; set; } = "aiinterview_report_sharing";

	public string OtpTemplateName { get; set; } = "customer_login_otp";

	public string PasswordRecoveryTemplateName { get; set; } = "customer_password_recovery";

	public int PollingIntervalSeconds { get; set; } = WhatsAppBusinessDefaults.DefaultPollingIntervalSeconds;

	public int MinDelayBetweenSendsSeconds { get; set; } = 10;

	public int MaxDelayBetweenSendsSeconds { get; set; } = 45;

	public int MaxMessagesPerBatch { get; set; } = 50;

	public int LookbackWindowDays { get; set; } = 30;

	public string WebhookVerifyToken { get; set; } = string.Empty;

	public string DefaultTrackingUrlPattern { get; set; } = "https://www.linkcorreios.com.br/?id={tracking}";

	public string CarrierTrackingUrls { get; set; } = "{}";

	public bool ShowOptInOnCheckoutCompleted { get; set; } = true;

	public bool ShowTrackingOnOrderDetails { get; set; } = true;

	public bool RequireCustomerAccount { get; set; }

	public long LastProcessedUtcTicks { get; set; }
}
