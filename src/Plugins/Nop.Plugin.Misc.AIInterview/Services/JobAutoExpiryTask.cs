using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class JobAutoExpiryTask : IScheduleTask
{
    private readonly IProductService _productService;
    private readonly IJobRequirementService _jobRequirementService;
    private readonly ILocalizationService _localizationService;

    public JobAutoExpiryTask(
        IProductService productService,
        IJobRequirementService jobRequirementService,
        ILocalizationService localizationService)
    {
        _productService = productService;
        _jobRequirementService = jobRequirementService;
        _localizationService = localizationService;
    }

    public async Task ExecuteAsync()
    {
        var products = await _productService.SearchProductsAsync(pageSize: int.MaxValue, showHidden: true);
        var cutoffUtc = DateTime.UtcNow.AddDays(-60);

        foreach (var product in products)
        {
            if (product == null || product.Deleted)
                continue;

            if (!product.Published)
                continue;

            if (product.CreatedOnUtc > cutoffUtc)
                continue;

            if (!await _jobRequirementService.IsJobProductAsync(product))
                continue;

            product.Published = false;
            await _productService.UpdateProductAsync(product);
        }
    }
}
