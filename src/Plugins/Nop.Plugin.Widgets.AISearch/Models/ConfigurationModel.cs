using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Widgets.AISearch.Models;

public record ConfigurationModel : BaseNopModel
{
    public int ActiveStoreScopeConfiguration { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.AISearch.Admin.Enabled")]
    public bool Enabled { get; set; }
    public bool Enabled_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.AISearch.Admin.AzureOpenAiEndpointUrl")]
    public string AzureOpenAiEndpointUrl { get; set; }
    public bool AzureOpenAiEndpointUrl_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.AISearch.Admin.AzureOpenAiApiKey")]
    public string AzureOpenAiApiKey { get; set; }
    public bool AzureOpenAiApiKey_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.AISearch.Admin.AzureOpenAiEmbeddingDeploymentName")]
    public string AzureOpenAiEmbeddingDeploymentName { get; set; }
    public bool AzureOpenAiEmbeddingDeploymentName_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.AISearch.Admin.AzureOpenAiApiVersion")]
    public string AzureOpenAiApiVersion { get; set; }
    public bool AzureOpenAiApiVersion_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.AISearch.Admin.TopK")]
    public int TopK { get; set; }
    public bool TopK_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.AISearch.Admin.SimilarityThreshold")]
    public double SimilarityThreshold { get; set; }
    public bool SimilarityThreshold_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.AISearch.Admin.SyncFusionLicenseKey")]
    public string SyncFusionLicenseKey { get; set; }
    public bool SyncFusionLicenseKey_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.AISearch.Admin.ReindexIntervalMinutes")]
    public int ReindexIntervalMinutes { get; set; }
    public bool ReindexIntervalMinutes_OverrideForStore { get; set; }
}
