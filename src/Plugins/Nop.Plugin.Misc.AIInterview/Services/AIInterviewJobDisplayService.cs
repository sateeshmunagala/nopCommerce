using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Services.Catalog;
using Nop.Services.Html;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Vendors;
using Nop.Web.Framework.Mvc.Routing;
using Nop.Web.Models.Catalog;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class AIInterviewJobDisplayService : IAIInterviewJobDisplayService
{
    public static readonly string[] WorkArrangementAliases = ["Work Arrangement", "Work Mode", "Work Type", "Workplace Type", "Workplace", "Work Setup", "Work Location Type", "Remote Type"];
    public static readonly string[] EmploymentTypeAliases = ["Employment Type", "Job Type", "Contract Type", "Employment Basis"];
    public static readonly string[] JobLocationAliases = ["Job Location", "Location", "Office Location", "Work Location", "City", "Region"];
    public static readonly string[] SalaryRangeAliases = ["Salary Range", "Compensation", "Pay Range", "Salary", "Compensation Range"];
    public static readonly string[] ExperienceLevelAliases = ["Experience Level", "Experience", "Seniority", "Seniority Level", "Level"];

    public static readonly ISet<string> CompactSpecificationAliases = new HashSet<string>(
        WorkArrangementAliases
            .Concat(EmploymentTypeAliases)
            .Concat(JobLocationAliases)
            .Concat(SalaryRangeAliases)
            .Concat(ExperienceLevelAliases)
            .Select(NormalizeSpecificationAttributeName),
        StringComparer.OrdinalIgnoreCase);

    private readonly IApplicationService _applicationService;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly IHtmlFormatter _htmlFormatter;
    private readonly IJobRequirementService _jobRequirementService;
    private readonly ILocalizationService _localizationService;
    private readonly IPictureService _pictureService;
    private readonly IProductService _productService;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IStoreContext _storeContext;
    private readonly IVendorService _vendorService;
    private readonly IWorkContext _workContext;
    private readonly INopUrlHelper _nopUrlHelper;

    public AIInterviewJobDisplayService(IApplicationService applicationService,
        IDateTimeHelper dateTimeHelper,
        IHtmlFormatter htmlFormatter,
        IJobRequirementService jobRequirementService,
        ILocalizationService localizationService,
        IPictureService pictureService,
        IProductService productService,
        ISpecificationAttributeService specificationAttributeService,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        IVendorService vendorService,
        IWorkContext workContext,
        INopUrlHelper nopUrlHelper)
    {
        _applicationService = applicationService;
        _dateTimeHelper = dateTimeHelper;
        _htmlFormatter = htmlFormatter;
        _jobRequirementService = jobRequirementService;
        _localizationService = localizationService;
        _pictureService = pictureService;
        _productService = productService;
        _specificationAttributeService = specificationAttributeService;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _vendorService = vendorService;
        _workContext = workContext;
        _nopUrlHelper = nopUrlHelper;
    }

    public async Task<AIInterviewJobProductCardModel> PrepareJobProductCardModelAsync(ProductOverviewModel productOverviewModel)
    {
        if (productOverviewModel == null)
            return null;

        var product = await _productService.GetProductByIdAsync(productOverviewModel.Id);
        if (product == null || !await _jobRequirementService.IsJobProductAsync(product))
            return null;

        var vendor = product.VendorId > 0 ? await _vendorService.GetVendorByIdAsync(product.VendorId) : null;
        var companyName = vendor != null
            ? await _localizationService.GetLocalizedAsync(vendor, entity => entity.Name)
            : string.Empty;

        var currentCustomer = await _workContext.GetCurrentCustomerAsync();
        var currentStore = await _storeContext.GetCurrentStoreAsync();
        var wishlistItems = currentCustomer == null
            ? new List<ShoppingCartItem>()
            : await _shoppingCartService.GetShoppingCartAsync(currentCustomer, ShoppingCartType.Wishlist, currentStore.Id, productId: product.Id, customWishlistId: 0);
        var wishlistItem = wishlistItems.FirstOrDefault();

        var imageModel = await PrepareImageModelAsync(productOverviewModel, vendor, companyName, productOverviewModel.Name);
        var summary = NormalizePlainText(productOverviewModel.ShortDescription);
        if (string.IsNullOrWhiteSpace(summary))
            summary = NormalizePlainText(await _localizationService.GetLocalizedAsync(product, entity => entity.ShortDescription));

        var previewDescription = NormalizePlainText(await _localizationService.GetLocalizedAsync(product, entity => entity.FullDescription));
        if (string.IsNullOrWhiteSpace(previewDescription))
            previewDescription = summary;

        var appliedCount = await _applicationService.GetApplicationCountAsync(productId: product.Id);

        return new AIInterviewJobProductCardModel
        {
            Id = product.Id,
            JobTitle = productOverviewModel.Name,
            CompanyName = companyName,
            Summary = summary,
            PreviewDescription = previewDescription,
            SeName = productOverviewModel.SeName,
            ProductUrl = await ResolveProductUrlAsync(product),
            PostedDateText = await FormatPostedDateAsync(product.CreatedOnUtc),
            ImageUrl = imageModel.ImageUrl,
            ImageAlt = imageModel.ImageAlt,
            UseImagePlaceholder = imageModel.UsePlaceholder,
            ImagePlaceholderText = imageModel.PlaceholderText,
            CreatedOnUtc = product.CreatedOnUtc,
            AppliedCount = appliedCount,
            CanSaveJob = !productOverviewModel.ProductPrice.DisableWishlistButton,
            IsSavedJob = wishlistItem != null,
            WishlistItemId = wishlistItem?.Id ?? 0,
            Specifications = await GetSpecificationSnapshotAsync(product.Id)
        };
    }

    public bool IsCompactSpecificationAttributeName(string name)
    {
        return CompactSpecificationAliases.Contains(NormalizeSpecificationAttributeName(name));
    }

    public async Task<AIInterviewJobSpecificationSnapshotModel> GetSpecificationSnapshotAsync(int productId, ProductSpecificationModel preparedSpecificationModel = null)
    {
        var entries = preparedSpecificationModel?.Groups?.Any() == true
            ? BuildEntriesFromPreparedModel(preparedSpecificationModel)
            : await BuildEntriesFromDatabaseAsync(productId);

        return new AIInterviewJobSpecificationSnapshotModel
        {
            WorkArrangement = ResolveValue(entries, WorkArrangementAliases),
            EmploymentType = ResolveValue(entries, EmploymentTypeAliases),
            JobLocation = ResolveValue(entries, JobLocationAliases),
            SalaryRange = ResolveValue(entries, SalaryRangeAliases),
            ExperienceLevel = ResolveValue(entries, ExperienceLevelAliases)
        };
    }

    protected virtual IList<(string Name, string Value)> BuildEntriesFromPreparedModel(ProductSpecificationModel preparedSpecificationModel)
    {
        return (preparedSpecificationModel?.Groups ?? [])
            .SelectMany(group => group.Attributes ?? [])
            .Select(attribute => (attribute.Name ?? string.Empty, NormalizePlainText(string.Join(", ", (attribute.Values ?? [])
                .Select(value => value.ValueRaw)
                .Where(value => !string.IsNullOrWhiteSpace(value))))))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Item1) && !string.IsNullOrWhiteSpace(entry.Item2))
            .ToList();
    }

    protected virtual async Task<IList<(string Name, string Value)>> BuildEntriesFromDatabaseAsync(int productId)
    {
        var languageId = (await _workContext.GetWorkingLanguageAsync())?.Id ?? 0;
        var mappings = await _specificationAttributeService.GetProductSpecificationAttributesAsync(productId);
        if (!mappings.Any())
            return [];

        var optionIds = mappings.Select(mapping => mapping.SpecificationAttributeOptionId).Distinct().ToArray();
        var options = await _specificationAttributeService.GetSpecificationAttributeOptionsByIdsAsync(optionIds);
        var specificationAttributes = await _specificationAttributeService.GetSpecificationAttributeByIdsAsync(options.Select(option => option.SpecificationAttributeId).Distinct().ToArray());

        var optionLookup = options.ToDictionary(option => option.Id);
        var attributeLookup = specificationAttributes.ToDictionary(attribute => attribute.Id);
        var values = new List<(string Name, string Value)>();

        foreach (var mapping in mappings.OrderBy(mapping => mapping.DisplayOrder).ThenBy(mapping => mapping.Id))
        {
            if (!optionLookup.TryGetValue(mapping.SpecificationAttributeOptionId, out var option))
                continue;

            if (!attributeLookup.TryGetValue(option.SpecificationAttributeId, out var specificationAttribute))
                continue;

            var name = await _localizationService.GetLocalizedAsync(specificationAttribute, entity => entity.Name, languageId, false, false);
            var value = mapping.AttributeType == SpecificationAttributeType.CustomText
                ? await _localizationService.GetLocalizedAsync(mapping, entity => entity.CustomValue, languageId, false, false)
                : await _localizationService.GetLocalizedAsync(option, entity => entity.Name, languageId, false, false);

            name = NormalizePlainText(name);
            value = NormalizePlainText(value);

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                values.Add((name, value));
        }

        return values;
    }

    protected virtual string ResolveValue(IEnumerable<(string Name, string Value)> entries, params string[] aliases)
    {
        var matchedValues = entries
            .Where(entry => IsAliasMatch(entry.Name, aliases))
            .Select(entry => entry.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matchedValues.Any() ? string.Join(", ", matchedValues) : string.Empty;
    }

    protected virtual bool IsAliasMatch(string value, IEnumerable<string> aliases)
    {
        var normalized = NormalizeSpecificationAttributeName(value);
        return aliases.Any(alias => string.Equals(normalized, NormalizeSpecificationAttributeName(alias), StringComparison.OrdinalIgnoreCase));
    }

    protected virtual string NormalizePlainText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var stripped = _htmlFormatter.StripTags(value)
            ?.Replace("\r", " ", StringComparison.Ordinal)
            ?.Replace("\n", " ", StringComparison.Ordinal)
            ?.Trim();

        return string.IsNullOrWhiteSpace(stripped)
            ? string.Empty
            : Regex.Replace(stripped, "\\s+", " ");
    }

    public static string NormalizeSpecificationAttributeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return Regex.Replace(value.Trim(), "\\s+", " ");
    }

    protected virtual async Task<(string ImageUrl, string ImageAlt, bool UsePlaceholder, string PlaceholderText)> PrepareImageModelAsync(ProductOverviewModel productOverviewModel, Vendor vendor, string companyName, string jobTitle)
    {
        if (vendor?.PictureId > 0)
        {
            var vendorPicture = await _pictureService.GetPictureByIdAsync(vendor.PictureId);
            if (vendorPicture != null)
            {
                var (imageUrl, _) = await _pictureService.GetPictureUrlAsync(vendorPicture, 160, true);
                if (!string.IsNullOrWhiteSpace(imageUrl))
                    return (imageUrl, string.IsNullOrWhiteSpace(companyName) ? jobTitle : companyName, false, string.Empty);
            }
        }

        var productPicture = productOverviewModel.PictureModels.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(productPicture?.ImageUrl))
            return (productPicture.ImageUrl, string.IsNullOrWhiteSpace(productPicture.AlternateText) ? jobTitle : productPicture.AlternateText, false, string.Empty);

        var seedText = string.IsNullOrWhiteSpace(companyName) ? jobTitle : companyName;
        return (string.Empty, seedText, true, GetInitials(seedText));
    }

    protected virtual string GetInitials(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "AI";

        var initials = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray();

        return initials.Length > 0 ? new string(initials) : "AI";
    }

    protected virtual async Task<string> ResolveProductUrlAsync(Product product)
    {
        if (product == null || _nopUrlHelper == null)
            return string.Empty;

        return await _nopUrlHelper.RouteGenericUrlAsync(product) ?? string.Empty;
    }

    protected virtual async Task<string> FormatPostedDateAsync(DateTime createdOnUtc)
    {
        var language = await _workContext.GetWorkingLanguageAsync();
        var culture = !string.IsNullOrWhiteSpace(language?.LanguageCulture)
            ? new CultureInfo(language.LanguageCulture)
            : CultureInfo.CurrentCulture;
        var userTime = _dateTimeHelper == null
            ? createdOnUtc
            : await _dateTimeHelper.ConvertToUserTimeAsync(createdOnUtc, DateTimeKind.Utc);

        return userTime.ToString("d", culture);
    }
}
