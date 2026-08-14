namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Services;

/// <summary>
/// Represents the optional WhatsApp notification provider exposed by this plugin.
/// </summary>
public interface IWhatsAppNotificationService
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
