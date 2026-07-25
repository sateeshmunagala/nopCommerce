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
                Temperature = request.Temperature,
                MaxOutputTokenCount = request.MaxTokens
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
        var value = endpoint?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Azure OpenAI endpoint is not configured.");

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Azure OpenAI endpoint is not a valid absolute URI.");

        return new Uri($"{uri.Scheme}://{uri.Authority}");
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
