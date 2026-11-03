using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class AzureOpenAiChatCompletionAdapter : IAzureOpenAiChatCompletionAdapter
{
    private const string AzureOpenAiApiVersion = "2025-04-01-preview";

    private readonly AIInterviewSettings _settings;
    private readonly HttpClient _httpClient;

    public AzureOpenAiChatCompletionAdapter(AIInterviewSettings settings, HttpClient httpClient = null)
    {
        _settings = settings;
        _httpClient = httpClient ?? new HttpClient();
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
            using var httpRequest = BuildAzureOpenAiHttpRequest(
                BuildChatCompletionsUri(endpoint, deploymentOrModel),
                BuildChatCompletionsRequestBody(request),
                _settings.AzureOpenAiApiKey);

            using var response = await _httpClient.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (IsUnsupportedMaxTokensCompatibilityFailure(response, responseBody))
                {
                    using var fallbackRequest = BuildAzureOpenAiHttpRequest(
                        BuildResponsesUri(endpoint),
                        BuildResponsesRequestBody(request, deploymentOrModel),
                        _settings.AzureOpenAiApiKey);

                    using var fallbackResponse = await _httpClient.SendAsync(fallbackRequest);
                    var fallbackResponseBody = await fallbackResponse.Content.ReadAsStringAsync();
                    if (!fallbackResponse.IsSuccessStatusCode)
                        return BuildHttpFailureResult(fallbackResponse, fallbackResponseBody, endpoint, deploymentOrModel, "responses", true);

                    return BuildSuccessResult(fallbackResponseBody, request?.Mode, endpoint, deploymentOrModel, "responses", true);
                }

                return BuildHttpFailureResult(response, responseBody, endpoint, deploymentOrModel, "chat-completions", false);
            }

            return BuildSuccessResult(responseBody, request?.Mode, endpoint, deploymentOrModel, "chat-completions", false);
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

    private static Uri BuildChatCompletionsUri(Uri endpoint, string deploymentOrModel)
    {
        var builder = new UriBuilder(endpoint)
        {
            Path = $"openai/deployments/{Uri.EscapeDataString(deploymentOrModel)}/chat/completions",
            Query = $"api-version={AzureOpenAiApiVersion}"
        };

        return builder.Uri;
    }

    private static Uri BuildResponsesUri(Uri endpoint)
    {
        var builder = new UriBuilder(endpoint)
        {
            Path = "openai/responses",
            Query = $"api-version={AzureOpenAiApiVersion}"
        };

        return builder.Uri;
    }

    private static HttpRequestMessage BuildAzureOpenAiHttpRequest(Uri uri, string requestBody, string apiKey)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Headers.Add("api-key", apiKey?.Trim());
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private static string BuildChatCompletionsRequestBody(AzureOpenAiChatCompletionRequest request)
    {
        var body = new Dictionary<string, object>
        {
            ["messages"] = new[]
            {
                new { role = "system", content = request?.SystemPrompt ?? string.Empty },
                new { role = "user", content = request?.UserPrompt ?? string.Empty }
            },
            ["max_completion_tokens"] = request?.MaxCompletionTokens ?? 0
        };
        if (!string.IsNullOrWhiteSpace(request?.ReasoningEffort))
            body["reasoning_effort"] = request.ReasoningEffort;
        return JsonSerializer.Serialize(body);
    }

    private static string BuildResponsesRequestBody(AzureOpenAiChatCompletionRequest request, string deploymentOrModel)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = deploymentOrModel,
            ["instructions"] = request?.SystemPrompt ?? string.Empty,
            ["input"] = request?.UserPrompt ?? string.Empty,
            ["max_output_tokens"] = request?.MaxCompletionTokens ?? 0
        };
        if (!string.IsNullOrWhiteSpace(request?.ReasoningEffort))
            body["reasoning_effort"] = request.ReasoningEffort;
        return JsonSerializer.Serialize(body);
    }

    private static AzureOpenAiChatCompletionResult BuildSuccessResult(string responseBody, string mode, Uri endpoint, string deploymentOrModel, string requestShape, bool fallbackUsed)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var finishReason = ExtractFinishReason(root);

        return new AzureOpenAiChatCompletionResult
        {
            Success = true,
            Content = ExtractAssistantContent(root),
            ResponseBody = SanitizeDiagnosticText(responseBody),
            Endpoint = endpoint.ToString(),
            EndpointHost = endpoint.Host,
            DeploymentOrModel = deploymentOrModel,
            ModelName = TryGetString(root, "model"),
            ResponseId = TryGetString(root, "id"),
            FinishReason = finishReason,
            IsLengthTruncated = IsLengthFinishReason(finishReason),
            RequestShape = requestShape,
            FallbackUsed = fallbackUsed,
            UsageInfo = BuildUsageInfo(root, mode, endpoint, deploymentOrModel, requestShape, fallbackUsed)
        };
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

    private static AzureOpenAiUsageInfo BuildUsageInfo(JsonElement response, string mode, Uri endpoint, string deploymentOrModel, string requestShape, bool fallbackUsed)
    {
        var hasUsage = response.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object;
        if (!hasUsage && string.IsNullOrWhiteSpace(deploymentOrModel))
            return null;

        var promptTokens = hasUsage ? Math.Max(0, TryGetInt(usage, "prompt_tokens") ?? TryGetInt(usage, "input_tokens") ?? 0) : 0;
        var completionTokens = hasUsage ? Math.Max(0, TryGetInt(usage, "completion_tokens") ?? TryGetInt(usage, "output_tokens") ?? 0) : 0;
        var totalTokens = hasUsage ? Math.Max(0, TryGetInt(usage, "total_tokens") ?? promptTokens + completionTokens) : 0;
        var rawUsageJson = !hasUsage
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
            ModelName = TryGetString(response, "model"),
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            RawUsageJson = rawUsageJson,
            MetadataJson = JsonSerializer.Serialize(new
            {
                mode,
                responseId = TryGetString(response, "id"),
                endpoint = BuildEndpointMetadataValue(endpoint),
                requestShape,
                fallbackUsed
            })
        };
    }

    private static string BuildEndpointMetadataValue(Uri endpoint)
    {
        return endpoint == null ? "<empty>" : $"{endpoint.Host}/";
    }

    private static AzureOpenAiChatCompletionResult BuildHttpFailureResult(HttpResponseMessage response, string responseBody, Uri endpoint, string deploymentOrModel, string requestShape = null, bool fallbackUsed = false)
    {
        var errorCode = string.Empty;
        var errorMessage = string.Empty;

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    errorCode = TryGetString(error, "code");
                    errorMessage = TryGetString(error, "message");
                }
            }
            catch (JsonException)
            {
                errorMessage = responseBody;
            }
        }

        var reasonPhrase = SanitizeDiagnosticText(response?.ReasonPhrase);
        var sanitizedErrorMessage = SanitizeDiagnosticText(errorMessage);

        return new AzureOpenAiChatCompletionResult
        {
            Success = false,
            FailureKind = "azure-openai-http-failure",
            Reason = "http failure",
            StatusCode = response == null ? null : (int)response.StatusCode,
            ReasonPhrase = reasonPhrase,
            ErrorCode = SanitizeDiagnosticText(errorCode),
            ErrorMessage = string.IsNullOrWhiteSpace(sanitizedErrorMessage) ? reasonPhrase : sanitizedErrorMessage,
            ResponseBody = SanitizeDiagnosticText(responseBody),
            Endpoint = endpoint?.ToString(),
            EndpointHost = endpoint?.Host,
            DeploymentOrModel = deploymentOrModel,
            RequestShape = requestShape,
            FallbackUsed = fallbackUsed
        };
    }

    private static bool IsUnsupportedMaxTokensCompatibilityFailure(HttpResponseMessage response, string responseBody)
    {
        if (response == null || (int)response.StatusCode != 400 || string.IsNullOrWhiteSpace(responseBody))
            return false;

        var sanitized = SanitizeDiagnosticText(responseBody);
        return sanitized.Contains("unsupported_parameter", StringComparison.OrdinalIgnoreCase) &&
            sanitized.Contains("max_tokens", StringComparison.OrdinalIgnoreCase);
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

    private static string ExtractAssistantContent(JsonElement response)
    {
        var chatContent = ExtractChatCompletionContent(response);
        if (!string.IsNullOrWhiteSpace(chatContent))
            return chatContent;

        var directOutputText = TryGetString(response, "output_text");
        if (!string.IsNullOrWhiteSpace(directOutputText))
            return directOutputText;

        if (!response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
            AppendTextParts(item, builder);

        return builder.ToString();
    }

    private static string ExtractChatCompletionContent(JsonElement response)
    {
        if (!response.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return string.Empty;

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString();

        var builder = new StringBuilder();
        AppendTextParts(content, builder);

        return builder.ToString();
    }

    private static void AppendTextParts(JsonElement element, StringBuilder builder)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            builder.Append(element.GetString());
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                AppendTextParts(item, builder);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        var outputText = TryGetString(element, "output_text");
        if (!string.IsNullOrWhiteSpace(outputText))
        {
            builder.Append(outputText);
            return;
        }

        var text = TryGetString(element, "text");
        if (!string.IsNullOrWhiteSpace(text))
        {
            builder.Append(text);
            return;
        }

        if (element.TryGetProperty("content", out var content))
            AppendTextParts(content, builder);
    }

    private static string ExtractFinishReason(JsonElement response)
    {
        if (response.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var chatFinishReason = TryGetString(choices[0], "finish_reason");
            if (!string.IsNullOrWhiteSpace(chatFinishReason))
                return SanitizeDiagnosticText(chatFinishReason);
        }

        var finishReason = TryGetString(response, "finish_reason");
        if (!string.IsNullOrWhiteSpace(finishReason))
            return SanitizeDiagnosticText(finishReason);

        if (response.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                finishReason = TryGetString(item, "finish_reason");
                if (!string.IsNullOrWhiteSpace(finishReason))
                    return SanitizeDiagnosticText(finishReason);
            }
        }

        return string.Empty;
    }

    private static bool IsLengthFinishReason(string finishReason)
    {
        return string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finishReason, "max_tokens", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finishReason, "max_output_tokens", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finishReason, "max_completion_tokens", StringComparison.OrdinalIgnoreCase);
    }

    private static string TryGetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
            return value;

        return null;
    }
}
