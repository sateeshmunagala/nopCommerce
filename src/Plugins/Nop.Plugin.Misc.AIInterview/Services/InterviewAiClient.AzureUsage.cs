using System.Text.Json;

namespace Nop.Plugin.Misc.AIInterview.Services;

public partial class InterviewAiClient
{
    private sealed record AzureContentCallResult(bool Success, string Content, string ErrorMessage, AzureOpenAiUsageInfo UsageInfo, bool IsLengthTruncated = false, string FinishReason = null);

    protected virtual AzureOpenAiUsageInfo BuildAzureOpenAiUsageInfo(JsonElement rootElement, string mode, string endpoint)
    {
        var deploymentOrModel = _settings?.AzureOpenAiDeploymentOrModel?.Trim();
        var modelName = rootElement.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String
            ? modelElement.GetString()?.Trim()
            : null;
        var responseId = rootElement.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()?.Trim()
            : null;

        if (!rootElement.TryGetProperty("usage", out var usageElement) || usageElement.ValueKind != JsonValueKind.Object)
        {
            return string.IsNullOrWhiteSpace(modelName) && string.IsNullOrWhiteSpace(responseId) && string.IsNullOrWhiteSpace(deploymentOrModel)
                ? null
                : new AzureOpenAiUsageInfo
                {
                    DeploymentOrModel = deploymentOrModel,
                    ModelName = modelName,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        mode,
                        responseId,
                        endpoint = BuildSanitizedEndpointValue(endpoint)
                    })
                };
        }

        var promptTokens = TryGetUsageInt(usageElement, "prompt_tokens", "input_tokens");
        var completionTokens = TryGetUsageInt(usageElement, "completion_tokens", "output_tokens");
        var totalTokens = TryGetUsageInt(usageElement, "total_tokens");
        if (totalTokens <= 0)
            totalTokens = promptTokens + completionTokens;

        return new AzureOpenAiUsageInfo
        {
            DeploymentOrModel = deploymentOrModel,
            ModelName = modelName,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            RawUsageJson = usageElement.GetRawText(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                mode,
                responseId,
                endpoint = BuildSanitizedEndpointValue(endpoint)
            })
        };
    }

    protected static int TryGetUsageInt(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames ?? Array.Empty<string>())
        {
            if (!element.TryGetProperty(propertyName, out var property))
                continue;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
                return Math.Max(0, number);

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out number))
                return Math.Max(0, number);
        }

        return 0;
    }
}
