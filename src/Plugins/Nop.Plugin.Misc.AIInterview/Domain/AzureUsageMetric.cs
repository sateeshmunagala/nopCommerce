using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class AzureUsageMetric : BaseEntity
{
    public int InterviewSessionId { get; set; }
    public int? InterviewTurnId { get; set; }
    public string UsageKind { get; set; }
    public string Provider { get; set; }
    public string DeploymentOrModel { get; set; }
    public string ModelName { get; set; }
    public string OperationName { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int SpeechRecognitionCharacters { get; set; }
    public int SpeechSynthesisCharacters { get; set; }
    public long SpeechDurationMs { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public string CurrencyCode { get; set; }
    public string PricingSnapshotJson { get; set; }
    public string RawUsageJson { get; set; }
    public string MetadataJson { get; set; }
    public string ClientEventId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}
