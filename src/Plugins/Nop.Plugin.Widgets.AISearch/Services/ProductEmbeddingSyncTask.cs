using Nop.Services.Catalog;
using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;
using Nop.Services.Stores;

namespace Nop.Plugin.Widgets.AISearch.Services;

public class ProductEmbeddingSyncTask : IScheduleTask
{
    private const int BatchSize = 100;

    private readonly IProductEmbeddingService _productEmbeddingService;
    private readonly IProductService _productService;
    private readonly ILogger _logger;
    private readonly IStoreService _storeService;
    private readonly AISearchSettings _settings;

    public ProductEmbeddingSyncTask(IProductEmbeddingService productEmbeddingService,
        IProductService productService,
        ILogger logger,
        IStoreService storeService,
        AISearchSettings settings)
    {
        _productEmbeddingService = productEmbeddingService;
        _productService = productService;
        _logger = logger;
        _storeService = storeService;
        _settings = settings;
    }

    public async Task ExecuteAsync()
    {
        await _logger.InformationAsync("AI Search product embedding reindex run started.");

        if (!_settings.Enabled)
        {
            await _logger.WarningAsync("AI Search product embedding reindex skipped because Enabled is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.AzureOpenAiEndpointUrl))
        {
            await _logger.WarningAsync("AI Search product embedding reindex skipped because EndpointUrl is missing.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.AzureOpenAiApiKey))
        {
            await _logger.WarningAsync("AI Search product embedding reindex skipped because ApiKey is missing.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.AzureOpenAiEmbeddingDeploymentName))
        {
            await _logger.WarningAsync("AI Search product embedding reindex skipped because DeploymentName is missing.");
            return;
        }

        var stores = await _storeService.GetAllStoresAsync();
        var publishedProductCount = 0;
        var processedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;

        for (var pageIndex = 0; ; pageIndex++)
        {
            var products = await _productService.SearchProductsAsync(
                pageIndex: pageIndex,
                pageSize: BatchSize,
                showHidden: false);
            var publishedProducts = products.Where(product => product.Published && !product.Deleted).ToList();
            publishedProductCount += publishedProducts.Count;

            foreach (var store in stores)
            {
                foreach (var product in publishedProducts)
                {
                    if (await _productEmbeddingService.UpsertProductEmbeddingAsync(product, store.Id))
                        processedCount++;
                    else if (_productEmbeddingService.LastUpsertWasSkipped)
                        skippedCount++;
                    else
                        failedCount++;
                }
            }

            if (products.Count < BatchSize)
                break;
        }

        await _productEmbeddingService.EnsureVectorIndexAsync();
        await _logger.InformationAsync(
            $"AI Search product embedding reindex finished. Stores: {stores.Count}; published products: {publishedProductCount}; processed: {processedCount}; skipped: {skippedCount}; failed: {failedCount}.");
    }
}
