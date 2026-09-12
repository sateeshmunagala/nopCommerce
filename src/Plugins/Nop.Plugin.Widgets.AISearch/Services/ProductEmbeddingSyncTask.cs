using Nop.Services.Catalog;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Widgets.AISearch.Services;

public class ProductEmbeddingSyncTask : IScheduleTask
{
    private const int BatchSize = 100;

    private readonly IProductEmbeddingService _productEmbeddingService;
    private readonly IProductService _productService;
    private readonly AISearchSettings _settings;

    public ProductEmbeddingSyncTask(IProductEmbeddingService productEmbeddingService,
        IProductService productService,
        AISearchSettings settings)
    {
        _productEmbeddingService = productEmbeddingService;
        _productService = productService;
        _settings = settings;
    }

    public async Task ExecuteAsync()
    {
        if (!_settings.Enabled ||
            string.IsNullOrWhiteSpace(_settings.AzureOpenAiEndpointUrl) ||
            string.IsNullOrWhiteSpace(_settings.AzureOpenAiApiKey))
            return;

        for (var pageIndex = 0; ; pageIndex++)
        {
            var products = await _productService.SearchProductsAsync(
                pageIndex: pageIndex,
                pageSize: BatchSize,
                showHidden: false);

            foreach (var product in products.Where(product => product.Published && !product.Deleted))
                await _productEmbeddingService.UpsertProductEmbeddingAsync(product);

            if (products.Count < BatchSize)
                break;
        }
    }
}
