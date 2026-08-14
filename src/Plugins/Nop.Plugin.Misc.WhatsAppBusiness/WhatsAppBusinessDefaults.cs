namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness;

public static class WhatsAppBusinessDefaults
{
	public const int DefaultPollingIntervalSeconds = 300;

	public static string SystemName => "Misc.WhatsAppBusiness";

	public static string ConfigurationRouteName => "Plugin.Misc.WhatsAppBusiness.Configure";

	public static string DashboardRouteName => "Plugin.Misc.WhatsAppBusiness.Dashboard";

	public static string ApiUrlTemplate => "https://graph.facebook.com/{0}/{1}/messages";

	public static (string Name, string Type, int Seconds) ScheduleTask =>
		(Name: $"Send WhatsApp order notifications ({SystemName})",
		 Type: typeof(Infrastructure.WhatsAppScheduleTask).FullName!,
		 Seconds: DefaultPollingIntervalSeconds);

	public static string LegacyScheduleTaskType => "Nop.Plugin.Misc.WhatsAppBusiness.Infrastructure.WhatsAppScheduleTask";

	public static string LastProcessedUtcKey => "WhatsAppBusiness.LastProcessedUtc";

	public static string HttpClientName => "SplatDev.WhatsAppBusiness";

	public static string CustomerOptInAttribute => "WhatsApp.OptIn";

	public static string CustomerPhoneAttribute => "WhatsApp.Phone";

	public static string WebhookPath => "api/whatsapp/webhook";
}
