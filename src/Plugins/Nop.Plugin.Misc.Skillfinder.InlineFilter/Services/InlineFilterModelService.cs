using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.Skillfinder.InlineFilter.Models;
using Nop.Services.Catalog;
using Nop.Services.Html;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.Seo;
using Nop.Web.Factories;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.Skillfinder.InlineFilter.Services;

public interface IInlineFilterModelService
{
    Task<PublicInfoModel> PreparePublicInfoModelAsync(string selectedCategorySeName = null);

    Task<FilteredProductsGridModel> PrepareFilteredProductsGridModelAsync(string selectedCategorySeName = null);
}

public class InlineFilterModelService : IInlineFilterModelService
{
    private readonly ICategoryService _categoryService;
    private readonly IHtmlFormatter _htmlFormatter;
    private readonly ILocalizationService _localizationService;
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly IPluginService _pluginService;
    private readonly IProductModelFactory _productModelFactory;
    private readonly IProductService _productService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly IStoreContext _storeContext;
    private readonly IUrlRecordService _urlRecordService;

    public InlineFilterModelService(
        ICategoryService categoryService,
        IHtmlFormatter htmlFormatter,
        ILocalizationService localizationService,
        INopUrlHelper nopUrlHelper,
        IPluginService pluginService,
        IProductModelFactory productModelFactory,
        IProductService productService,
        IProductTemplateService productTemplateService,
        IStoreContext storeContext,
        IUrlRecordService urlRecordService)
    {
        _categoryService = categoryService;
        _htmlFormatter = htmlFormatter;
        _localizationService = localizationService;
        _nopUrlHelper = nopUrlHelper;
        _pluginService = pluginService;
        _productModelFactory = productModelFactory;
        _productService = productService;
        _productTemplateService = productTemplateService;
        _storeContext = storeContext;
        _urlRecordService = urlRecordService;
    }

    public async Task<PublicInfoModel> PreparePublicInfoModelAsync(string selectedCategorySeName = null)
    {
        var categories = await GetAvailableCategoriesAsync();
        var selectedCategory = await ResolveSelectedCategoryAsync(categories, selectedCategorySeName);
        var selectedSeName = selectedCategory == null
            ? null
            : await _urlRecordService.GetSeNameAsync(selectedCategory);

        var model = new PublicInfoModel
        {
            Results = await PrepareFilteredProductsGridModelAsync(selectedSeName)
        };

        foreach (var category in categories)
        {
            var seName = await _urlRecordService.GetSeNameAsync(category);
            if (string.IsNullOrWhiteSpace(seName))
                continue;

            model.Categories.Add(new InlineFilterCategoryModel
            {
                Name = await _localizationService.GetLocalizedAsync(category, entity => entity.Name),
                SeName = seName,
                IsSelected = selectedCategory?.Id == category.Id
            });
        }

        return model;
    }

    public async Task<FilteredProductsGridModel> PrepareFilteredProductsGridModelAsync(string selectedCategorySeName = null)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var categories = await GetAvailableCategoriesAsync();

        var selectedCategory = await ResolveSelectedCategoryAsync(categories, selectedCategorySeName);
        var resolvedSeName = selectedCategory == null
            ? null
            : await _urlRecordService.GetSeNameAsync(selectedCategory);
        var useAiInterviewCards = await IsAiInterviewAvailableAsync(store.Id);
        var products = await GetProductsAsync(selectedCategory, store.Id, useAiInterviewCards);
        var overviewModels = (await _productModelFactory.PrepareProductOverviewModelsAsync(products))
            .Take(SkillfinderInlineFilterDefaults.ResultCount)
            .ToList();

        var model = new FilteredProductsGridModel
        {
            SelectedCategorySeName = resolvedSeName,
            ViewMoreUrl = selectedCategory == null
                ? string.Empty
                : await _nopUrlHelper.RouteGenericUrlAsync(selectedCategory) ?? string.Empty,
            UseAiInterviewCards = useAiInterviewCards
        };

        foreach (var productOverview in overviewModels)
        {
            var picture = productOverview.PictureModels.FirstOrDefault();
            var product = products.FirstOrDefault(item => item.Id == productOverview.Id);
            model.Products.Add(new InlineFilterProductModel
            {
                ProductOverview = productOverview,
                ProductUrl = product == null
                    ? string.Empty
                    : await _nopUrlHelper.RouteGenericUrlAsync(product) ?? string.Empty,
                Summary = NormalizePlainText(productOverview.ShortDescription),
                PictureUrl = picture?.ImageUrl ?? string.Empty,
                PictureAlt = string.IsNullOrWhiteSpace(picture?.AlternateText)
                    ? productOverview.Name
                    : picture.AlternateText
            });
        }

        return model;
    }

    protected virtual async Task<IList<Category>> GetAvailableCategoriesAsync()
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var homepageCategories = await _categoryService.GetAllCategoriesDisplayedOnHomepageAsync();
        if (homepageCategories.Any())
            return homepageCategories;

        return (await _categoryService.GetAllCategoriesAsync(store.Id))
            .Where(category => category.ParentCategoryId == 0)
            .ToList();
    }

    protected virtual async Task<Category> ResolveSelectedCategoryAsync(
        IList<Category> categories,
        string selectedCategorySeName)
    {
        if (!string.IsNullOrWhiteSpace(selectedCategorySeName))
        {
            foreach (var category in categories)
            {
                var seName = await _urlRecordService.GetSeNameAsync(category);
                if (string.Equals(seName, selectedCategorySeName, StringComparison.OrdinalIgnoreCase))
                    return category;
            }

            return categories.FirstOrDefault();
        }

        return categories.FirstOrDefault();
    }

    protected virtual async Task<IList<Product>> GetProductsAsync(
        Category selectedCategory,
        int storeId,
        bool onlyAiInterviewJobs)
    {
        if (selectedCategory == null)
            return new List<Product>();

        var categoryIds = new List<int> { selectedCategory.Id };
        categoryIds.AddRange(await _categoryService.GetChildCategoryIdsAsync(selectedCategory.Id, storeId));

        var selectedProducts = new List<Product>();
        var pageIndex = 0;

        while (selectedProducts.Count < SkillfinderInlineFilterDefaults.ResultCount)
        {
            var page = await _productService.SearchProductsAsync(
                pageIndex: pageIndex,
                pageSize: SkillfinderInlineFilterDefaults.SearchPageSize,
                categoryIds: categoryIds.Distinct().ToList(),
                storeId: storeId,
                visibleIndividuallyOnly: true,
                orderBy: ProductSortingEnum.CreatedOn,
                showHidden: false);

            foreach (var product in page)
            {
                if (!onlyAiInterviewJobs || await IsAiInterviewJobProductAsync(product))
                    selectedProducts.Add(product);

                if (selectedProducts.Count == SkillfinderInlineFilterDefaults.ResultCount)
                    break;
            }

            pageIndex++;
            if (pageIndex >= page.TotalPages)
                break;
        }

        return selectedProducts;
    }

    protected virtual async Task<bool> IsAiInterviewAvailableAsync(int storeId)
    {
        return await _pluginService.GetPluginDescriptorBySystemNameAsync<IPlugin>(
            SkillfinderInlineFilterDefaults.AiInterviewSystemName,
            storeId: storeId) != null;
    }

    protected virtual async Task<bool> IsAiInterviewJobProductAsync(Product product)
    {
        if (product?.ProductTemplateId <= 0)
            return false;

        var productTemplate = await _productTemplateService.GetProductTemplateByIdAsync(product.ProductTemplateId);
        return productTemplate != null &&
            (string.Equals(productTemplate.Name, SkillfinderInlineFilterDefaults.AiInterviewJobTemplateName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(productTemplate.ViewPath, SkillfinderInlineFilterDefaults.AiInterviewJobTemplateViewPath, StringComparison.OrdinalIgnoreCase));
    }

    protected virtual string NormalizePlainText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return _htmlFormatter.StripTags(value)?.Trim() ?? string.Empty;
    }
}
