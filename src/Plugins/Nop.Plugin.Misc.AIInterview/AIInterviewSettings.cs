using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.AIInterview;

/// <summary>
/// Represents AI Interview settings
/// </summary>
public class AIInterviewSettings : ISettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the AI Interview is enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the API key for AI Interview
    /// </summary>
    public string ApiKey { get; set; }

    public string AzureOpenAiEndpointUrl { get; set; }

    public string AzureOpenAiApiKey { get; set; }

    public string AzureOpenAiDeploymentOrModel { get; set; }

    public string AzureSpeechKey { get; set; }

    public string AzureSpeechRegion { get; set; }

    public string AzureDocumentIntelligenceEndpointUrl { get; set; }

    public string AzureDocumentIntelligenceApiKey { get; set; }

    public string AzureDocumentIntelligenceModelId { get; set; }

    public int AzureDocumentIntelligenceTimeoutSeconds { get; set; }

    public string AzureBlobStorageContainerUrl { get; set; }

    public string AzureBlobStorageSasToken { get; set; }

    public bool TrackAzureOpenAiUsage { get; set; }

    public bool TrackAzureSpeechUsage { get; set; }

    public bool CalculateAzureCostPerInterview { get; set; }

    public bool EnableFinalScoringAtCompletion { get; set; } = true;

    public int MockInterviewQuestionCount { get; set; } = 5;

    public decimal AzureOpenAiPromptTokenPricePerThousand { get; set; }

    public decimal AzureOpenAiCompletionTokenPricePerThousand { get; set; }

    public decimal AzureSpeechRecognitionPricePerHour { get; set; }

    public decimal AzureSpeechSynthesisPricePerThousandCharacters { get; set; }

    public string AzureUsageCurrencyCode { get; set; }

    public string SupportPhoneNumber { get; set; } = AIInterviewDefaults.DefaultSupportPhoneNumber;

    public int StrengthsSummaryMaxCompletionTokens { get; set; } = AIInterviewDefaults.DefaultStrengthsSummaryMaxCompletionTokens;

    public int QuestionPlanMaxCompletionTokens { get; set; } = AIInterviewDefaults.DefaultQuestionPlanMaxCompletionTokens;

    public int QuestionPlanRetryMaxCompletionTokens { get; set; } = AIInterviewDefaults.DefaultQuestionPlanRetryMaxCompletionTokens;

    public int RecordingUploadMaxMb { get; set; } = AIInterviewDefaults.DefaultRecordingUploadMaxMb;

    public int RecordingVideoBitsPerSecond { get; set; } = AIInterviewDefaults.DefaultRecordingVideoBitsPerSecond;

    public int RecordingAudioBitsPerSecond { get; set; } = AIInterviewDefaults.DefaultRecordingAudioBitsPerSecond;

    public string RecordingSourceMode { get; set; } = AIInterviewDefaults.DefaultRecordingSourceMode;

    public int RecordingUploadTimeoutMs { get; set; } = AIInterviewDefaults.DefaultRecordingUploadTimeoutMs;

    public int FinalizationWaitTimeoutMs { get; set; } = AIInterviewDefaults.DefaultFinalizationWaitTimeoutMs;

    /// <summary>
    /// Gets or sets the minimum score required in an interview to apply
    /// </summary>
    public decimal MinimumScore { get; set; }

    /// <summary>
    /// Gets or sets the AI provider
    /// </summary>
    public string Provider { get; set; }

    public string PlatformMode { get; set; } = "Employer";

    /// <summary>
    /// Gets or sets the AI model
    /// </summary>
    public string Model { get; set; }

    /// <summary>
    /// Gets or sets the system prompt
    /// </summary>
    public string Prompt { get; set; }

    public string ResumeProfileExtractionSystemPrompt { get; set; } = AIInterviewDefaults.DefaultResumeProfileExtractionSystemPrompt;

    public string QuestionPlanSystemPrompt { get; set; } = AIInterviewDefaults.DefaultQuestionPlanSystemPrompt;

    public string QuestionPlanBuilderInstructionBlock { get; set; } = AIInterviewDefaults.DefaultQuestionPlanBuilderInstructionBlock;

    public string RuntimeQuestionGenerationSystemPrompt { get; set; } = AIInterviewDefaults.DefaultRuntimeQuestionGenerationSystemPrompt;

    public string RuntimeScoringSystemPrompt { get; set; } = AIInterviewDefaults.DefaultRuntimeScoringSystemPrompt;

    public string RuntimeScoringRetryAddendumPrompt { get; set; } = AIInterviewDefaults.DefaultRuntimeScoringRetryAddendumPrompt;

    public string FinalScoringSystemPrompt { get; set; } = AIInterviewDefaults.DefaultFinalScoringSystemPrompt;

    public string StrengthsSummarySystemPrompt { get; set; } = AIInterviewDefaults.DefaultStrengthsSummarySystemPrompt;

    public string StrengthsSummaryRetryStrictJsonSystemPrompt { get; set; } = AIInterviewDefaults.DefaultStrengthsSummaryRetryStrictJsonSystemPrompt;

    /// <summary>
    /// Gets or sets additional service settings
    /// </summary>
    public string ServiceSettings { get; set; }

    /// <summary>
    /// Gets or sets the JSON mapping between credit pack SKUs and credits granted per unit
    /// </summary>
    public string CreditProductSkuMappingsJson { get; set; }

    /// <summary>
    /// Gets or sets the URL for the pricing or credit purchase page
    /// </summary>
    public string CreditPurchasePageUrl { get; set; }
    public string CreditPageUrl { get; set; }

    /// <summary>
    /// Gets or sets the credit pack amount
    /// </summary>
    public decimal CreditPackAmount { get; set; }

    /// <summary>
    /// Gets or sets the credit pack price
    /// </summary>
    public decimal CreditPackPrice { get; set; }
}
