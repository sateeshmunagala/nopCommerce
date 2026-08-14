namespace Nop.Services.Messages;

/// <summary>
/// Represents an optional WhatsApp notification supplied by an installed messaging plugin.
/// </summary>
public partial interface IWhatsAppNotificationService
{
    /// <summary>
    /// Gets a value indicating whether the provider is enabled for sending.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Sends a WhatsApp notification when the provider is enabled and configured.
    /// </summary>
    /// <param name="request">Notification data</param>
    /// <returns>Whether the provider accepted the message</returns>
    Task<bool> SendNotificationAsync(WhatsAppNotificationRequest request);
}

/// <summary>
/// Contains provider-neutral WhatsApp notification data.
/// </summary>
public partial class WhatsAppNotificationRequest
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
