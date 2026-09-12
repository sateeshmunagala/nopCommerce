using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Widgets.AISearch.Models;
using Nop.Plugin.Widgets.AISearch.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Widgets.AISearch.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class AISearchAdminController : BasePluginController
{
    private readonly ILocalizationService _localizationService;
    private readonly ILogger _logger;
    private readonly INotificationService _notificationService;
    private readonly IProductEmbeddingService _productEmbeddingService;
    private readonly ProductEmbeddingSyncTask _syncTask;
    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;

    public AISearchAdminController(ILocalizationService localizationService,
        ILogger logger,
        INotificationService notificationService,
        IProductEmbeddingService productEmbeddingService,
        ProductEmbeddingSyncTask syncTask,
        ISettingService settingService,
        IStoreContext storeContext)
    {
        _localizationService = localizationService;
        _logger = logger;
        _notificationService = notificationService;
        _productEmbeddingService = productEmbeddingService;
        _syncTask = syncTask;
        _settingService = settingService;
        _storeContext = storeContext;
    }

    [CheckPermission(AISearchPermissionConfigManager.ADMIN_ACCESS_AISEARCH)]
    public async Task<IActionResult> Configure()
    {
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<AISearchSettings>(storeScope);
        var model = new ConfigurationModel
        {
            ActiveStoreScopeConfiguration = storeScope,
            Enabled = settings.Enabled,
            AzureOpenAiEndpointUrl = settings.AzureOpenAiEndpointUrl,
            AzureOpenAiApiKey = settings.AzureOpenAiApiKey,
            AzureOpenAiEmbeddingDeploymentName = settings.AzureOpenAiEmbeddingDeploymentName,
            AzureOpenAiApiVersion = settings.AzureOpenAiApiVersion,
            TopK = settings.TopK,
            SimilarityThreshold = settings.SimilarityThreshold,
            SyncFusionLicenseKey = settings.SyncFusionLicenseKey,
            ReindexIntervalMinutes = settings.ReindexIntervalMinutes
        };

        if (storeScope > 0)
        {
            model.Enabled_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.Enabled, storeScope);
            model.AzureOpenAiEndpointUrl_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.AzureOpenAiEndpointUrl, storeScope);
            model.AzureOpenAiApiKey_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.AzureOpenAiApiKey, storeScope);
            model.AzureOpenAiEmbeddingDeploymentName_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.AzureOpenAiEmbeddingDeploymentName, storeScope);
            model.AzureOpenAiApiVersion_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.AzureOpenAiApiVersion, storeScope);
            model.TopK_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.TopK, storeScope);
            model.SimilarityThreshold_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.SimilarityThreshold, storeScope);
            model.SyncFusionLicenseKey_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.SyncFusionLicenseKey, storeScope);
            model.ReindexIntervalMinutes_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ReindexIntervalMinutes, storeScope);
        }

        return View("~/Plugins/Widgets.AISearch/Views/Admin/Configure.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(AISearchPermissionConfigManager.ADMIN_ACCESS_AISEARCH)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return View("~/Plugins/Widgets.AISearch/Views/Admin/Configure.cshtml", model);

        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<AISearchSettings>(storeScope);
        settings.Enabled = model.Enabled;
        settings.AzureOpenAiEndpointUrl = model.AzureOpenAiEndpointUrl;
        settings.AzureOpenAiApiKey = model.AzureOpenAiApiKey;
        settings.AzureOpenAiEmbeddingDeploymentName = model.AzureOpenAiEmbeddingDeploymentName;
        settings.AzureOpenAiApiVersion = model.AzureOpenAiApiVersion;
        settings.TopK = Math.Clamp(model.TopK, 1, 50);
        settings.SimilarityThreshold = Math.Clamp(model.SimilarityThreshold, 0d, 1d);
        settings.SyncFusionLicenseKey = model.SyncFusionLicenseKey;
        settings.ReindexIntervalMinutes = Math.Max(1, model.ReindexIntervalMinutes);

        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.Enabled, model.Enabled_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.AzureOpenAiEndpointUrl, model.AzureOpenAiEndpointUrl_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.AzureOpenAiApiKey, model.AzureOpenAiApiKey_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.AzureOpenAiEmbeddingDeploymentName, model.AzureOpenAiEmbeddingDeploymentName_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.AzureOpenAiApiVersion, model.AzureOpenAiApiVersion_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.TopK, model.TopK_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.SimilarityThreshold, model.SimilarityThreshold_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.SyncFusionLicenseKey, model.SyncFusionLicenseKey_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ReindexIntervalMinutes, model.ReindexIntervalMinutes_OverrideForStore, storeScope, false);
        await _settingService.ClearCacheAsync();

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));
        return RedirectToRoute(AISearchDefaults.ConfigurationRouteName);
    }

    [HttpPost]
    [CheckPermission(AISearchPermissionConfigManager.ADMIN_ACCESS_AISEARCH)]
    public async Task<IActionResult> ReindexNow()
    {
        await _syncTask.ExecuteAsync();
        await _logger.InformationAsync("AI Search manual product embedding reindex finished.");
        await _productEmbeddingService.EnsureVectorIndexAsync();
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Widgets.AISearch.Admin.ReindexStarted"));
        return RedirectToRoute(AISearchDefaults.ConfigurationRouteName);
    }
}
