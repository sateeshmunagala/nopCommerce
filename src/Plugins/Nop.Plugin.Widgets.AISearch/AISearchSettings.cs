using Nop.Core.Configuration;

namespace Nop.Plugin.Widgets.AISearch;

public class AISearchSettings : ISettings
{
    public bool Enabled { get; set; }
    public string AzureOpenAiEndpointUrl { get; set; }
    public string AzureOpenAiApiKey { get; set; }
    public string AzureOpenAiEmbeddingDeploymentName { get; set; }
    public string AzureOpenAiApiVersion { get; set; } = "2024-10-21";
    public int TopK { get; set; } = 8;
    public double SimilarityThreshold { get; set; } = 0.75;
    public string SyncFusionLicenseKey { get; set; }
    public int ReindexIntervalMinutes { get; set; } = 30;
}
