using Nop.Services.Catalog;
using Nop.Services.ScheduleTasks;
using Nop.Services.Stores;

namespace Nop.Plugin.Widgets.AISearch.Services;

public class ProductEmbeddingSyncTask : IScheduleTask
{
    private const int BatchSize = 100;

    private readonly IProductEmbeddingService _productEmbeddingService;
    private readonly IProductService _productService;
    private readonly IStoreService _storeService;
    private readonly AISearchSettings _settings;

    public ProductEmbeddingSyncTask(IProductEmbeddingService productEmbeddingService,
        IProductService productService,
        IStoreService storeService,
        AISearchSettings settings)
    {
        _productEmbeddingService = productEmbeddingService;
        _productService = productService;
        _storeService = storeService;
        _settings = settings;
    }

    public async Task ExecuteAsync()
    {
        if (!_settings.Enabled ||
            string.IsNullOrWhiteSpace(_settings.AzureOpenAiEndpointUrl) ||
            string.IsNullOrWhiteSpace(_settings.AzureOpenAiApiKey))
            return;

        var stores = await _storeService.GetAllStoresAsync();

        for (var pageIndex = 0; ; pageIndex++)
        {
            var products = await _productService.SearchProductsAsync(
                pageIndex: pageIndex,
                pageSize: BatchSize,
                showHidden: false);

            foreach (var store in stores)
            {
                foreach (var product in products.Where(product => product.Published && !product.Deleted))
                    await _productEmbeddingService.UpsertProductEmbeddingAsync(product, store.Id);
            }

            if (products.Count < BatchSize)
                break;
        }
    }
}
