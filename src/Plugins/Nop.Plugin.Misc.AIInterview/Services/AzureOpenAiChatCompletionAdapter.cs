using System.ClientModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class AzureOpenAiChatCompletionAdapter : IAzureOpenAiChatCompletionAdapter
{
    private readonly AIInterviewSettings _settings;

    public AzureOpenAiChatCompletionAdapter(AIInterviewSettings settings)
    {
        _settings = settings;
    }

    public virtual async Task<AzureOpenAiChatCompletionResult> CompleteChatAsync(AzureOpenAiChatCompletionRequest request)
    {
        var validationFailure = ValidateConfiguration(_settings);
        if (validationFailure != null)
            return validationFailure;

        var endpoint = NormalizeResourceEndpoint(_settings?.AzureOpenAiEndpointUrl);
        var deploymentOrModel = _settings?.AzureOpenAiDeploymentOrModel?.Trim();

        try
        {
            var azureClient = new AzureOpenAIClient(endpoint, new ApiKeyCredential(_settings.AzureOpenAiApiKey.Trim()));
            var chatClient = azureClient.GetChatClient(deploymentOrModel);
            var messages = new ChatMessage[]
            {
                new SystemChatMessage(request.SystemPrompt ?? string.Empty),
                new UserChatMessage(request.UserPrompt ?? string.Empty)
            };
            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = request.MaxCompletionTokens
            };

            var response = await chatClient.CompleteChatAsync(messages, options);
            var completion = response.Value;
            var content = string.Join(string.Empty, completion.Content.Select(part => part.Text));

            return new AzureOpenAiChatCompletionResult
            {
                Success = true,
                Content = content,
                Endpoint = endpoint.ToString(),
                EndpointHost = endpoint.Host,
                DeploymentOrModel = deploymentOrModel,
                ModelName = completion.Model,
                ResponseId = completion.Id,
                UsageInfo = BuildUsageInfo(completion, request?.Mode, endpoint, deploymentOrModel)
            };
        }
        catch (RequestFailedException ex)
        {
            return BuildRequestFailedResult(ex, endpoint, deploymentOrModel);
        }
        catch (Exception ex)
        {
            return new AzureOpenAiChatCompletionResult
            {
                Success = false,
                FailureKind = "azure-openai-exception",
                Reason = SanitizeDiagnosticText(ex.GetType().Name),
                ErrorMessage = SanitizeDiagnosticText(ex.Message),
                ResponseBody = SanitizeDiagnosticText(ex.ToString()),
                Endpoint = endpoint.ToString(),
                EndpointHost = endpoint.Host,
                DeploymentOrModel = deploymentOrModel
            };
        }
    }

    private static Uri NormalizeResourceEndpoint(string endpoint)
    {
        if (!TryNormalizeAzureOpenAiEndpoint(endpoint, out var uri, out var reason))
            throw new InvalidOperationException(reason);

        return new Uri($"{uri.Scheme}://{uri.Authority}");
    }

    public static bool TryNormalizeAzureOpenAiEndpoint(string endpoint, out Uri normalizedEndpoint, out string failureReason)
    {
        normalizedEndpoint = null;
        failureReason = string.Empty;

        var value = endpoint?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            failureReason = "Azure OpenAI endpoint is not configured.";
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            failureReason = "Azure OpenAI endpoint must be an absolute HTTPS resource endpoint.";
            return false;
        }

        if (!IsSupportedAzureOpenAiEndpointHost(uri.Host))
        {
            failureReason = "Azure OpenAI endpoint host must be an Azure OpenAI resource host under openai.azure.com or cognitiveservices.azure.com.";
            return false;
        }

        normalizedEndpoint = new Uri($"{uri.Scheme}://{uri.Authority}");
        return true;
    }

    private static AzureOpenAiChatCompletionResult ValidateConfiguration(AIInterviewSettings settings)
    {
        var deploymentOrModel = settings?.AzureOpenAiDeploymentOrModel?.Trim();
        if (string.IsNullOrWhiteSpace(settings?.AzureOpenAiEndpointUrl))
            return BuildConfigurationInvalidResult("Azure OpenAI endpoint is not configured.", settings?.AzureOpenAiEndpointUrl, deploymentOrModel);
        if (string.IsNullOrWhiteSpace(settings?.AzureOpenAiApiKey))
            return BuildConfigurationInvalidResult("Azure OpenAI API key is not configured.", settings.AzureOpenAiEndpointUrl, deploymentOrModel);
        if (string.IsNullOrWhiteSpace(deploymentOrModel))
            return BuildConfigurationInvalidResult("Azure OpenAI deployment/model is not configured.", settings.AzureOpenAiEndpointUrl, deploymentOrModel);

        if (!TryNormalizeAzureOpenAiEndpoint(settings.AzureOpenAiEndpointUrl, out _, out var endpointFailureReason))
            return BuildConfigurationInvalidResult(endpointFailureReason, settings.AzureOpenAiEndpointUrl, deploymentOrModel);

        if (deploymentOrModel.Contains('/') || deploymentOrModel.Contains('\\') || deploymentOrModel.Contains('?') || deploymentOrModel.Contains('#'))
            return BuildConfigurationInvalidResult("Azure OpenAI deployment/model must be a deployment name, not a URL or path.", settings.AzureOpenAiEndpointUrl, deploymentOrModel);

        return null;
    }

    private static bool IsSupportedAzureOpenAiEndpointHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var normalized = host.Trim().ToLowerInvariant();
        return normalized.EndsWith(".openai.azure.com", StringComparison.Ordinal) ||
            normalized.EndsWith(".cognitiveservices.azure.com", StringComparison.Ordinal);
    }

    private static AzureOpenAiChatCompletionResult BuildConfigurationInvalidResult(string reason, string endpoint, string deploymentOrModel)
    {
        return new AzureOpenAiChatCompletionResult
        {
            Success = false,
            FailureKind = "azure-openai-configuration-invalid",
            Reason = SanitizeDiagnosticText(reason),
            ErrorMessage = SanitizeDiagnosticText(reason),
            Endpoint = TryBuildEndpointValue(endpoint),
            EndpointHost = TryBuildEndpointHost(endpoint),
            DeploymentOrModel = SanitizeDiagnosticText(deploymentOrModel)
        };
    }

    private static string TryBuildEndpointValue(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "<empty>";

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri))
            return SanitizeDiagnosticText(endpoint.Trim());

        return $"{uri.Scheme}://{uri.Authority}/";
    }

    private static string TryBuildEndpointHost(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "<empty>";

        return Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
            ? uri.Host
            : SanitizeDiagnosticText(endpoint.Trim());
    }

    private static AzureOpenAiUsageInfo BuildUsageInfo(ChatCompletion completion, string mode, Uri endpoint, string deploymentOrModel)
    {
        var usage = completion?.Usage;
        if (completion == null && usage == null && string.IsNullOrWhiteSpace(deploymentOrModel))
            return null;

        var promptTokens = Math.Max(0, usage?.InputTokenCount ?? 0);
        var completionTokens = Math.Max(0, usage?.OutputTokenCount ?? 0);
        var totalTokens = Math.Max(0, usage?.TotalTokenCount ?? promptTokens + completionTokens);
        var rawUsageJson = usage == null
            ? null
            : JsonSerializer.Serialize(new
            {
                prompt_tokens = promptTokens,
                completion_tokens = completionTokens,
                total_tokens = totalTokens,
                input_tokens = promptTokens,
                output_tokens = completionTokens
            });

        return new AzureOpenAiUsageInfo
        {
            DeploymentOrModel = deploymentOrModel,
            ModelName = completion?.Model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            RawUsageJson = rawUsageJson,
            MetadataJson = JsonSerializer.Serialize(new
            {
                mode,
                responseId = completion?.Id,
                endpoint = BuildEndpointMetadataValue(endpoint)
            })
        };
    }

    private static string BuildEndpointMetadataValue(Uri endpoint)
    {
        return endpoint == null ? "<empty>" : $"{endpoint.Host}/";
    }

    private static AzureOpenAiChatCompletionResult BuildRequestFailedResult(RequestFailedException exception, Uri endpoint, string deploymentOrModel)
    {
        return new AzureOpenAiChatCompletionResult
        {
            Success = false,
            FailureKind = "azure-openai-http-failure",
            Reason = "http failure",
            StatusCode = exception?.Status,
            ReasonPhrase = SanitizeDiagnosticText(exception?.Message),
            ErrorCode = SanitizeDiagnosticText(exception?.ErrorCode),
            ErrorMessage = SanitizeDiagnosticText(exception?.Message),
            ResponseBody = SanitizeDiagnosticText(exception?.Message),
            Endpoint = endpoint?.ToString(),
            EndpointHost = endpoint?.Host,
            DeploymentOrModel = deploymentOrModel
        };
    }

    private static string SanitizeDiagnosticText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        sanitized = Regex.Replace(sanitized, "(?i)(api[-_ ]?key|authorization|access[_-]?token|refresh[_-]?token|bearer|subscription[-_ ]?key)\\s*[:=]\\s*\\\"?[^\\\"\\s,;}]+", "$1=<redacted>");
        sanitized = Regex.Replace(sanitized, "(?i)(sig|signature|code|client_secret)=([^&\\s]+)", "$1=<redacted>");
        return sanitized.Length <= 1000 ? sanitized : sanitized[..1000];
    }
}
