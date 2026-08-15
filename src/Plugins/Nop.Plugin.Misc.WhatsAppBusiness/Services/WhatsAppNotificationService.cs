using Nop.Services.Logging;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Services;

/// <summary>
/// Adapts the plugin-specific sender to nopCommerce's optional WhatsApp contract.
/// </summary>
public class WhatsAppNotificationService : IWhatsAppNotificationService
{
    private readonly IWhatsAppBusinessService _whatsAppBusinessService;
    private readonly WhatsAppBusinessSettings _settings;
    private readonly ILogger _logger;

    public WhatsAppNotificationService(
        IWhatsAppBusinessService whatsAppBusinessService,
        WhatsAppBusinessSettings settings,
        ILogger logger = null)
    {
        _whatsAppBusinessService = whatsAppBusinessService;
        _settings = settings;
        _logger = logger;
    }

    public bool IsEnabled => _settings.IsEnabled;

    public async Task<bool> SendNotificationAsync(WhatsAppNotificationRequest request)
    {
        if (!_settings.IsEnabled || request == null || string.IsNullOrWhiteSpace(request.PhoneNumber))
            return false;

        try
        {
            var templateName = string.IsNullOrWhiteSpace(request.TemplateName)
                ? ResolveTemplateName(request.MessageType)
                : request.TemplateName;

            var isSent = _settings.UseTemplateMessages && !string.IsNullOrWhiteSpace(templateName)
                ? await _whatsAppBusinessService.SendTemplateMessageAsync(
                    0,
                    request.CustomerId,
                    request.PhoneNumber,
                    request.MessageType,
                    templateName,
                    string.IsNullOrWhiteSpace(request.LanguageCode) ? _settings.DefaultLanguageCode : request.LanguageCode,
                    request.TemplateParameters ?? new List<string>())
                : await _whatsAppBusinessService.SendMessageAsync(
                    0,
                    request.CustomerId,
                    request.PhoneNumber,
                    request.MessageType,
                    request.MessageBody ?? string.Empty);

            if (!isSent)
                await LogWarningAsync($"Optional WhatsApp {request.MessageType} delivery was not accepted for {WhatsAppLogHelper.MaskPhone(request.PhoneNumber)}.");

            return isSent;
        }
        catch (Exception exception)
        {
            await LogWarningAsync($"Optional WhatsApp {request.MessageType} delivery failed for {WhatsAppLogHelper.MaskPhone(request.PhoneNumber)} ({exception.GetType().Name}).");
            return false;
        }
    }

    protected virtual string ResolveTemplateName(string messageType)
    {
        return messageType switch
        {
            "AIInterview.ApplicantCompletion" => _settings.ApplicantInterviewCompletionTemplateName,
            "AIInterview.VendorCompletion" => _settings.VendorInterviewCompletionTemplateName,
            "AIInterview.ReportSharing" => _settings.InterviewReportSharingTemplateName,
            "Authentication.Otp" => _settings.OtpTemplateName,
            _ => string.Empty
        };
    }

    protected virtual async Task LogWarningAsync(string message)
    {
        if (_logger == null)
            return;

        try
        {
            await _logger.WarningAsync(message);
        }
        catch
        {
            // Optional notification delivery must not fail because diagnostic logging is unavailable.
        }
    }
}
