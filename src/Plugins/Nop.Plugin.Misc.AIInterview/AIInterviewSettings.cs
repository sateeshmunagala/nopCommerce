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

    public string AgoraAppId { get; set; }

    public string AgoraTokenServiceUrl { get; set; }

    public string AzureSpeechKey { get; set; }

    public string AzureSpeechRegion { get; set; }

    public string AzureBlobStorageContainerUrl { get; set; }

    public string AzureBlobStorageSasToken { get; set; }

    /// <summary>
    /// Gets or sets the minimum score required in an interview to apply
    /// </summary>
    public decimal MinimumScore { get; set; }

    /// <summary>
    /// Gets or sets the AI provider
    /// </summary>
    public string Provider { get; set; }

    /// <summary>
    /// Gets or sets the AI model
    /// </summary>
    public string Model { get; set; }

    /// <summary>
    /// Gets or sets the system prompt
    /// </summary>
    public string Prompt { get; set; }

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

    /// <summary>
    /// Gets or sets the credit pack amount
    /// </summary>
    public decimal CreditPackAmount { get; set; }

    /// <summary>
    /// Gets or sets the credit pack price
    /// </summary>
    public decimal CreditPackPrice { get; set; }
}
