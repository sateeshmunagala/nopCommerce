using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nop.Data;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class AzureUsageService : IAzureUsageService
{
    private const int MaxSpeechCharactersPerEvent = 50000;
    private const long MaxSpeechDurationMsPerEvent = 1800000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IRepository<AzureUsageMetric> _azureUsageMetricRepository;
    private readonly IRepository<InterviewSession> _interviewSessionRepository;
    private readonly AIInterviewSettings _settings;
    private readonly ILogger<AzureUsageService> _logger;

    private sealed record AzureUsagePricingSnapshot(
        bool TrackAzureOpenAiUsage,
        bool TrackAzureSpeechUsage,
        bool CalculateAzureCostPerInterview,
        decimal AzureOpenAiPromptTokenPricePerThousand,
        decimal AzureOpenAiCompletionTokenPricePerThousand,
        decimal AzureSpeechRecognitionPricePerHour,
        decimal AzureSpeechSynthesisPricePerThousandCharacters,
        string AzureUsageCurrencyCode);

    public AzureUsageService(
        IRepository<AzureUsageMetric> azureUsageMetricRepository,
        IRepository<InterviewSession> interviewSessionRepository,
        AIInterviewSettings settings,
        ILogger<AzureUsageService> logger = null)
    {
        _azureUsageMetricRepository = azureUsageMetricRepository;
        _interviewSessionRepository = interviewSessionRepository;
        _settings = settings;
        _logger = logger;
    }

    public async Task RecordOpenAiUsageAsync(AzureOpenAiUsageRecordRequest request)
    {
        if (request?.InterviewSessionId <= 0 || request.UsageInfo == null || _settings?.TrackAzureOpenAiUsage != true)
            return;

        var promptTokens = ClampToNonNegative(request.UsageInfo.PromptTokens);
        var completionTokens = ClampToNonNegative(request.UsageInfo.CompletionTokens);
        var totalTokens = ClampToNonNegative(request.UsageInfo.TotalTokens);
        if (totalTokens <= 0)
            totalTokens = promptTokens + completionTokens;

        if (promptTokens <= 0 &&
            completionTokens <= 0 &&
            totalTokens <= 0 &&
            string.IsNullOrWhiteSpace(request.UsageInfo.RawUsageJson))
        {
            return;
        }

        try
        {
            var session = await _interviewSessionRepository.GetByIdAsync(request.InterviewSessionId);
            if (session == null)
                return;

            var metric = new AzureUsageMetric
            {
                InterviewSessionId = request.InterviewSessionId,
                InterviewTurnId = request.InterviewTurnId,
                UsageKind = request.UsageKind,
                Provider = AzureUsageMetricDefaults.ProviderAzureOpenAi,
                DeploymentOrModel = request.UsageInfo.DeploymentOrModel?.Trim(),
                ModelName = string.IsNullOrWhiteSpace(request.UsageInfo.ModelName)
                    ? request.UsageInfo.DeploymentOrModel?.Trim()
                    : request.UsageInfo.ModelName.Trim(),
                OperationName = request.OperationName?.Trim(),
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = totalTokens,
                EstimatedCostUsd = CalculateOpenAiCost(promptTokens, completionTokens),
                CurrencyCode = NormalizeCurrencyCode(_settings.AzureUsageCurrencyCode),
                PricingSnapshotJson = BuildPricingSnapshotJson(),
                RawUsageJson = Truncate(request.UsageInfo.RawUsageJson, 4000),
                MetadataJson = MergeMetadataJson(request.UsageInfo.MetadataJson, request.MetadataJson),
                CreatedOnUtc = DateTime.UtcNow
            };

            await _azureUsageMetricRepository.InsertAsync(metric);
            await RecalculateSessionSummaryAsync(request.InterviewSessionId);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception,
                "AIInterview Azure OpenAI usage tracking failed for session {SessionId}, turn {TurnId}, usage kind {UsageKind}.",
                request.InterviewSessionId, request.InterviewTurnId, request.UsageKind);
        }
    }

    public async Task RecordSpeechUsageAsync(AzureSpeechUsageRecordRequest request)
    {
        if (request?.InterviewSessionId <= 0 || _settings?.TrackAzureSpeechUsage != true)
            return;

        var clientEventId = NormalizeClientEventId(request.ClientEventId);
        var recognitionCharacters = ClampToRange(request.SpeechRecognitionCharacters, MaxSpeechCharactersPerEvent);
        var synthesisCharacters = ClampToRange(request.SpeechSynthesisCharacters, MaxSpeechCharactersPerEvent);
        var speechDurationMs = ClampToRange(request.SpeechDurationMs, MaxSpeechDurationMsPerEvent);

        if (recognitionCharacters <= 0 && synthesisCharacters <= 0 && speechDurationMs <= 0)
            return;

        try
        {
            var session = await _interviewSessionRepository.GetByIdAsync(request.InterviewSessionId);
            if (session == null)
                return;

            if (!string.IsNullOrWhiteSpace(clientEventId))
            {
                var existingMetrics = await _azureUsageMetricRepository.GetAllAsync(query => query
                    .Where(metric => metric.ClientEventId == clientEventId)
                    .Take(1));
                if (existingMetrics.Any())
                    return;
            }

            var metric = new AzureUsageMetric
            {
                InterviewSessionId = request.InterviewSessionId,
                InterviewTurnId = request.InterviewTurnId,
                UsageKind = request.UsageKind,
                Provider = AzureUsageMetricDefaults.ProviderAzureSpeech,
                OperationName = request.OperationName?.Trim(),
                SpeechRecognitionCharacters = recognitionCharacters,
                SpeechSynthesisCharacters = synthesisCharacters,
                SpeechDurationMs = speechDurationMs,
                EstimatedCostUsd = CalculateSpeechCost(recognitionCharacters, synthesisCharacters, speechDurationMs),
                CurrencyCode = NormalizeCurrencyCode(_settings.AzureUsageCurrencyCode),
                PricingSnapshotJson = BuildPricingSnapshotJson(),
                MetadataJson = request.MetadataJson,
                ClientEventId = clientEventId,
                CreatedOnUtc = DateTime.UtcNow
            };

            await _azureUsageMetricRepository.InsertAsync(metric);
            await RecalculateSessionSummaryAsync(request.InterviewSessionId);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception,
                "AIInterview Azure Speech usage tracking failed for session {SessionId}, turn {TurnId}, usage kind {UsageKind}, client event {ClientEventId}.",
                request.InterviewSessionId, request.InterviewTurnId, request.UsageKind, clientEventId);
        }
    }

    public async Task RecalculateSessionSummaryAsync(int interviewSessionId)
    {
        if (interviewSessionId <= 0)
            return;

        try
        {
            var session = await _interviewSessionRepository.GetByIdAsync(interviewSessionId);
            if (session == null)
                return;

            var metrics = await _azureUsageMetricRepository.GetAllAsync(query => query
                .Where(metric => metric.InterviewSessionId == interviewSessionId));

            var allMetrics = metrics.ToList();
            var openAiMetrics = allMetrics
                .Where(metric => string.Equals(metric.Provider, AzureUsageMetricDefaults.ProviderAzureOpenAi, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var speechMetrics = allMetrics
                .Where(metric => string.Equals(metric.Provider, AzureUsageMetricDefaults.ProviderAzureSpeech, StringComparison.OrdinalIgnoreCase))
                .ToList();

            session.TotalPromptTokens = openAiMetrics.Sum(metric => ClampToNonNegative(metric.PromptTokens));
            session.TotalCompletionTokens = openAiMetrics.Sum(metric => ClampToNonNegative(metric.CompletionTokens));
            session.TotalOpenAiCostUsd = RoundCost(openAiMetrics.Sum(metric => ClampToNonNegative(metric.EstimatedCostUsd)));
            session.TotalSpeechRecognitionCharacters = speechMetrics.Sum(metric => ClampToNonNegative(metric.SpeechRecognitionCharacters));
            session.TotalSpeechSynthesisCharacters = speechMetrics.Sum(metric => ClampToNonNegative(metric.SpeechSynthesisCharacters));
            session.TotalSpeechDurationMs = speechMetrics.Sum(metric => ClampToNonNegative(metric.SpeechDurationMs));
            session.TotalSpeechCostUsd = RoundCost(speechMetrics.Sum(metric => ClampToNonNegative(metric.EstimatedCostUsd)));
            session.TotalAzureCostUsd = RoundCost(session.TotalOpenAiCostUsd + session.TotalSpeechCostUsd);

            await _interviewSessionRepository.UpdateAsync(session);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception,
                "AIInterview Azure usage summary recalculation failed for session {SessionId}.",
                interviewSessionId);
        }
    }

    protected virtual decimal CalculateOpenAiCost(int promptTokens, int completionTokens)
    {
        if (_settings?.CalculateAzureCostPerInterview != true)
            return 0m;

        var promptCost = ClampToNonNegative(promptTokens) / 1000m * ClampToNonNegative(_settings.AzureOpenAiPromptTokenPricePerThousand);
        var completionCost = ClampToNonNegative(completionTokens) / 1000m * ClampToNonNegative(_settings.AzureOpenAiCompletionTokenPricePerThousand);
        return RoundCost(promptCost + completionCost);
    }

    protected virtual decimal CalculateSpeechCost(int recognitionCharacters, int synthesisCharacters, long speechDurationMs)
    {
        if (_settings?.CalculateAzureCostPerInterview != true)
            return 0m;

        var recognitionCost = ClampToNonNegative(speechDurationMs) / 3600000m * ClampToNonNegative(_settings.AzureSpeechRecognitionPricePerHour);
        var synthesisCost = ClampToNonNegative(synthesisCharacters) / 1000m * ClampToNonNegative(_settings.AzureSpeechSynthesisPricePerThousandCharacters);
        return RoundCost(recognitionCost + synthesisCost);
    }

    protected virtual string BuildPricingSnapshotJson()
    {
        var snapshot = new AzureUsagePricingSnapshot(
            _settings?.TrackAzureOpenAiUsage == true,
            _settings?.TrackAzureSpeechUsage == true,
            _settings?.CalculateAzureCostPerInterview == true,
            ClampToNonNegative(_settings?.AzureOpenAiPromptTokenPricePerThousand ?? 0m),
            ClampToNonNegative(_settings?.AzureOpenAiCompletionTokenPricePerThousand ?? 0m),
            ClampToNonNegative(_settings?.AzureSpeechRecognitionPricePerHour ?? 0m),
            ClampToNonNegative(_settings?.AzureSpeechSynthesisPricePerThousandCharacters ?? 0m),
            NormalizeCurrencyCode(_settings?.AzureUsageCurrencyCode));

        return JsonSerializer.Serialize(snapshot, SerializerOptions);
    }

    protected static string MergeMetadataJson(string leftJson, string rightJson)
    {
        JsonObject merged = null;

        if (!string.IsNullOrWhiteSpace(leftJson))
            merged = TryParseJsonObject(leftJson);

        if (!string.IsNullOrWhiteSpace(rightJson))
        {
            merged ??= new JsonObject();
            var rightObject = TryParseJsonObject(rightJson);
            if (rightObject != null)
            {
                foreach (var property in rightObject)
                    merged[property.Key] = property.Value?.DeepClone();
            }
        }

        return merged == null || merged.Count == 0
            ? null
            : merged.ToJsonString(SerializerOptions);
    }

    protected static JsonObject TryParseJsonObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    protected static string NormalizeClientEventId(string clientEventId)
    {
        var normalized = clientEventId?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : Truncate(normalized, 200);
    }

    protected static string NormalizeCurrencyCode(string currencyCode)
    {
        var normalized = currencyCode?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "USD" : Truncate(normalized, 10);
    }

    protected static decimal RoundCost(decimal amount)
    {
        return Math.Round(ClampToNonNegative(amount), 4, MidpointRounding.AwayFromZero);
    }

    protected static int ClampToRange(int value, int max)
    {
        return Math.Clamp(value, 0, max);
    }

    protected static long ClampToRange(long value, long max)
    {
        return Math.Clamp(value, 0L, max);
    }

    protected static int ClampToNonNegative(int value)
    {
        return Math.Max(0, value);
    }

    protected static long ClampToNonNegative(long value)
    {
        return Math.Max(0L, value);
    }

    protected static decimal ClampToNonNegative(decimal value)
    {
        return Math.Max(0m, value);
    }

    protected static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}
