using Nop.Services.Messages;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Services;

/// <summary>
/// Adapts the plugin-specific sender to nopCommerce's optional WhatsApp contract.
/// </summary>
public class WhatsAppNotificationService : IWhatsAppNotificationService
{
    private readonly IWhatsAppBusinessService _whatsAppBusinessService;
    private readonly WhatsAppBusinessSettings _settings;

    public WhatsAppNotificationService(
        IWhatsAppBusinessService whatsAppBusinessService,
        WhatsAppBusinessSettings settings)
    {
        _whatsAppBusinessService = whatsAppBusinessService;
        _settings = settings;
    }

    public bool IsEnabled => _settings.IsEnabled;

    public async Task<bool> SendNotificationAsync(WhatsAppNotificationRequest request)
    {
        if (!_settings.IsEnabled || request == null || string.IsNullOrWhiteSpace(request.PhoneNumber))
            return false;

        var templateName = string.IsNullOrWhiteSpace(request.TemplateName)
            ? ResolveTemplateName(request.MessageType)
            : request.TemplateName;

        if (_settings.UseTemplateMessages && !string.IsNullOrWhiteSpace(templateName))
        {
            return await _whatsAppBusinessService.SendTemplateMessageAsync(
                0,
                request.CustomerId,
                request.PhoneNumber,
                request.MessageType,
                templateName,
                string.IsNullOrWhiteSpace(request.LanguageCode) ? _settings.DefaultLanguageCode : request.LanguageCode,
                request.TemplateParameters ?? new List<string>());
        }

        return await _whatsAppBusinessService.SendMessageAsync(
            0,
            request.CustomerId,
            request.PhoneNumber,
            request.MessageType,
            request.MessageBody ?? string.Empty);
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
}
