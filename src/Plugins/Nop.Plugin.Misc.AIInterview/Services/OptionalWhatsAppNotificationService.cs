using System.Reflection;

namespace Nop.Plugin.Misc.AIInterview.Services;

/// <summary>
/// Defines the WhatsApp capability consumed by AIInterview without requiring a provider assembly.
/// </summary>
public interface IOptionalWhatsAppNotificationService
{
    bool IsEnabled { get; }

    Task<bool> SendNotificationAsync(AIInterviewWhatsAppNotificationRequest request);
}

/// <summary>
/// Contains the provider-neutral notification data produced by AIInterview.
/// </summary>
public class AIInterviewWhatsAppNotificationRequest
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

/// <summary>
/// Resolves an optional WhatsApp provider without a compile-time assembly reference.
/// </summary>
public static class OptionalWhatsAppNotificationServiceResolver
{
    public const string ProviderTypeName =
        "Nop.Plugin.Misc.WhatsAppBusiness.Services.IWhatsAppNotificationService, Nop.Plugin.Misc.WhatsAppBusiness";

    public static IOptionalWhatsAppNotificationService Resolve(IServiceProvider serviceProvider)
    {
        if (serviceProvider == null)
            return null;

        var registeredService = serviceProvider.GetService(typeof(IOptionalWhatsAppNotificationService))
            as IOptionalWhatsAppNotificationService;
        return registeredService ?? ResolveLateBound(serviceProvider, ProviderTypeName);
    }

    public static IOptionalWhatsAppNotificationService ResolveLateBound(
        IServiceProvider serviceProvider,
        string providerTypeName)
    {
        if (serviceProvider == null || string.IsNullOrWhiteSpace(providerTypeName))
            return null;

        var providerType = Type.GetType(providerTypeName, throwOnError: false);
        if (providerType == null)
            return null;

        var provider = serviceProvider.GetService(providerType);
        if (provider == null)
            return null;

        var isEnabledProperty = providerType.GetProperty("IsEnabled", BindingFlags.Instance | BindingFlags.Public);
        var sendMethod = providerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(method => method.Name == "SendNotificationAsync" && method.GetParameters().Length == 1);
        var requestType = sendMethod?.GetParameters()[0].ParameterType;

        if (isEnabledProperty == null || sendMethod == null || requestType == null)
            return null;

        return new LateBoundWhatsAppNotificationService(provider, isEnabledProperty, sendMethod, requestType);
    }

    private sealed class LateBoundWhatsAppNotificationService : IOptionalWhatsAppNotificationService
    {
        private readonly object _provider;
        private readonly PropertyInfo _isEnabledProperty;
        private readonly MethodInfo _sendMethod;
        private readonly Type _requestType;

        public LateBoundWhatsAppNotificationService(
            object provider,
            PropertyInfo isEnabledProperty,
            MethodInfo sendMethod,
            Type requestType)
        {
            _provider = provider;
            _isEnabledProperty = isEnabledProperty;
            _sendMethod = sendMethod;
            _requestType = requestType;
        }

        public bool IsEnabled => _isEnabledProperty.GetValue(_provider) is true;

        public async Task<bool> SendNotificationAsync(AIInterviewWhatsAppNotificationRequest request)
        {
            var providerRequest = Activator.CreateInstance(_requestType);
            if (providerRequest == null)
                return false;

            SetProperty(providerRequest, "CustomerId", request.CustomerId);
            SetProperty(providerRequest, "PhoneNumber", request.PhoneNumber);
            SetProperty(providerRequest, "MessageType", request.MessageType);
            SetProperty(providerRequest, "MessageBody", request.MessageBody);
            SetProperty(providerRequest, "TemplateName", request.TemplateName);
            SetProperty(providerRequest, "LanguageCode", request.LanguageCode);
            SetProperty(providerRequest, "TemplateParameters", request.TemplateParameters);
            SetProperty(providerRequest, "Tokens", request.Tokens);

            try
            {
                return _sendMethod.Invoke(_provider, new[] { providerRequest }) is Task<bool> sendTask &&
                    await sendTask;
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }

        private void SetProperty(object target, string propertyName, object value)
        {
            _requestType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.SetValue(target, value);
        }
    }
}
