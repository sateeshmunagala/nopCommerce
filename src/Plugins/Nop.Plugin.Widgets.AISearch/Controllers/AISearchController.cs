using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Widgets.AISearch.Models;
using Nop.Plugin.Widgets.AISearch.Services;
using Nop.Services.Catalog;
using Nop.Services.Media;
using Nop.Services.Seo;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Widgets.AISearch.Controllers;

[AutoValidateAntiforgeryToken]
public class AISearchController : BasePluginController
{
    private readonly IPriceCalculationService _priceCalculationService;
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IPictureService _pictureService;
    private readonly IProductEmbeddingService _productEmbeddingService;
    private readonly IProductService _productService;
    private readonly IStoreContext _storeContext;
    private readonly IUrlRecordService _urlRecordService;
    private readonly IWorkContext _workContext;
    private readonly AISearchSettings _settings;

    public AISearchController(IPriceCalculationService priceCalculationService,
        INopUrlHelper nopUrlHelper,
        IPriceFormatter priceFormatter,
        IPictureService pictureService,
        IProductEmbeddingService productEmbeddingService,
        IProductService productService,
        IStoreContext storeContext,
        IUrlRecordService urlRecordService,
        IWorkContext workContext,
        AISearchSettings settings)
    {
        _priceCalculationService = priceCalculationService;
        _nopUrlHelper = nopUrlHelper;
        _priceFormatter = priceFormatter;
        _pictureService = pictureService;
        _productEmbeddingService = productEmbeddingService;
        _productService = productService;
        _storeContext = storeContext;
        _urlRecordService = urlRecordService;
        _workContext = workContext;
        _settings = settings;
    }

    [HttpPost]
    public async Task<IActionResult> Search([FromBody] SearchRequestModel request)
    {
        var result = new SearchResultModel();
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(request?.Query))
            return Json(NoResults(result));

        var store = await _storeContext.GetCurrentStoreAsync();
        var productIds = await _productEmbeddingService.SearchAsync(request.Query, store.Id, _settings.TopK);
        var customer = await _workContext.GetCurrentCustomerAsync();

        foreach (var productId in productIds)
        {
            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null || product.Deleted || !product.Published)
                continue;

            var picture = await _pictureService.GetProductPictureAsync(product, string.Empty);
            var pictureUrl = picture == null
                ? await _pictureService.GetDefaultPictureUrlAsync(300)
                : (await _pictureService.GetPictureUrlAsync(picture, 300)).Url;
            var (_, finalPrice, _, _) = await _priceCalculationService.GetFinalPriceAsync(
                product, customer, store, decimal.Zero, true, 1);
            var seName = await _urlRecordService.GetSeNameAsync(product);

            result.Products.Add(new ProductCardModel
            {
                Id = product.Id,
                Name = product.Name,
                PictureUrl = pictureUrl,
                FormattedPrice = await _priceFormatter.FormatPriceAsync(finalPrice),
                Url = await _nopUrlHelper.RouteGenericUrlAsync<Product>(new { SeName = seName })
            });
        }

        if (!result.Products.Any())
            return Json(NoResults(result));

        result.HasResults = true;
        result.Message = $"Found {result.Products.Count} matching results";
        return Json(result);
    }

    private static SearchResultModel NoResults(SearchResultModel result)
    {
        result.HasResults = false;
        result.Message = "No matching results found, try a different search";
        return result;
    }
}
