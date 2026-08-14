namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Services;

/// <summary>
/// Contains provider-neutral WhatsApp notification data.
/// </summary>
public class WhatsAppNotificationRequest
{
    public int CustomerId { get; set; }

    public string PhoneNumber { get; set; }

    public string MessageType { get; set; }

    public string MessageBody { get; set; }

    public string TemplateName { get; set; }

    public string LanguageCode { get; set; }

    public IList<string> TemplateParameters { get; set; } = new List<string>();

    public IDictionary<string, string> Tokens { get; set; } = new Dictionary<string, string>();
}
