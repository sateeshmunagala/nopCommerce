using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Models.Account;
using Nop.Services.Affiliates;
using Nop.Services.Attributes;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Seo;
using Nop.Plugin.Misc.JobSupport.Services;

namespace Nop.Plugin.Misc.JobSupport.Factories;

public class JobSupportAccountModelFactory : IJobSupportAccountModelFactory
{
    private readonly IAffiliateService _affiliateService;
    private readonly IAttributeParser<CustomerAttribute, CustomerAttributeValue> _attributeParser;
    private readonly IAttributeService<CustomerAttribute, CustomerAttributeValue> _attributeService;
    private readonly ICustomerService _customerService;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly IJobSupportProfileModelFactory _profileModelFactory;
    private readonly IJobSupportProfileQueryService _profileQueryService;
    private readonly IJobSupportProfileService _profileService;
    private readonly IJobSupportSubscriptionService _subscriptionService;
    private readonly ILocalizationService _localizationService;
    private readonly IProductService _productService;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IUrlRecordService _urlRecordService;
    private readonly IWebHelper _webHelper;
    private readonly JobSupportSettings _settings;

    public JobSupportAccountModelFactory(IAffiliateService affiliateService,
        IAttributeParser<CustomerAttribute, CustomerAttributeValue> attributeParser,
        IAttributeService<CustomerAttribute, CustomerAttributeValue> attributeService,
        ICustomerService customerService,
        IDateTimeHelper dateTimeHelper,
        IJobSupportProfileModelFactory profileModelFactory,
        IJobSupportProfileQueryService profileQueryService,
        IJobSupportProfileService profileService,
        IJobSupportSubscriptionService subscriptionService,
        ILocalizationService localizationService,
        IProductService productService,
        ISpecificationAttributeService specificationAttributeService,
        IUrlRecordService urlRecordService,
        IWebHelper webHelper,
        JobSupportSettings settings)
    {
        _affiliateService = affiliateService;
        _attributeParser = attributeParser;
        _attributeService = attributeService;
        _customerService = customerService;
        _dateTimeHelper = dateTimeHelper;
        _profileModelFactory = profileModelFactory;
        _profileQueryService = profileQueryService;
        _profileService = profileService;
        _subscriptionService = subscriptionService;
        _localizationService = localizationService;
        _productService = productService;
        _specificationAttributeService = specificationAttributeService;
        _urlRecordService = urlRecordService;
        _webHelper = webHelper;
        _settings = settings;
    }

    public async Task<ProfileEditModel> PrepareProfileEditAsync(Customer customer, ProfileEditModel model = null)
    {
        model ??= new ProfileEditModel();
        var xml = customer.CustomCustomerAttributesXML ?? string.Empty;
        model.ProfileTypeId = model.ProfileTypeId > 0 ? model.ProfileTypeId : Selected(xml, _settings.ProfileTypeSpecificationAttributeId).FirstOrDefault();
        model.PrimaryTechnologyIds = model.PrimaryTechnologyIds.Any() ? model.PrimaryTechnologyIds : Selected(xml, _settings.PrimaryTechnologySpecificationAttributeId);
        model.SecondaryTechnologyIds = model.SecondaryTechnologyIds.Any() ? model.SecondaryTechnologyIds : Selected(xml, _settings.SecondaryTechnologySpecificationAttributeId);
        model.AvailabilityId = model.AvailabilityId > 0 ? model.AvailabilityId : Selected(xml, _settings.CurrentAvailabilitySpecificationAttributeId).FirstOrDefault();
        model.RelevantExperienceId = model.RelevantExperienceId > 0 ? model.RelevantExperienceId : Selected(xml, _settings.RelevantExperienceSpecificationAttributeId).FirstOrDefault();
        model.MotherTongueId = model.MotherTongueId > 0 ? model.MotherTongueId : Selected(xml, _settings.MotherTongueSpecificationAttributeId).FirstOrDefault();
        var profile = customer.VendorId > 0 ? await _productService.GetProductByIdAsync(customer.VendorId) : null;
        model.ShortDescription ??= profile?.ShortDescription;
        model.Description ??= profile?.FullDescription;
        model.ProfileTypes = await OptionsAsync(_settings.ProfileTypeSpecificationAttributeId, new[] { model.ProfileTypeId });
        model.PrimaryTechnologies = await OptionsAsync(_settings.PrimaryTechnologySpecificationAttributeId, model.PrimaryTechnologyIds);
        model.SecondaryTechnologies = await OptionsAsync(_settings.SecondaryTechnologySpecificationAttributeId, model.SecondaryTechnologyIds);
        model.Availabilities = await OptionsAsync(_settings.CurrentAvailabilitySpecificationAttributeId, new[] { model.AvailabilityId });
        model.RelevantExperiences = await OptionsAsync(_settings.RelevantExperienceSpecificationAttributeId, new[] { model.RelevantExperienceId });
        model.MotherTongues = await OptionsAsync(_settings.MotherTongueSpecificationAttributeId, new[] { model.MotherTongueId });
        return model;
    }

    public async Task SaveProfileAsync(Customer customer, ProfileEditModel model)
    {
        var xml = customer.CustomCustomerAttributesXML ?? string.Empty;
        xml = await ReplaceAsync(xml, _settings.ProfileTypeSpecificationAttributeId, new[] { model.ProfileTypeId });
        xml = await ReplaceAsync(xml, _settings.PrimaryTechnologySpecificationAttributeId, model.PrimaryTechnologyIds);
        xml = await ReplaceAsync(xml, _settings.SecondaryTechnologySpecificationAttributeId, model.SecondaryTechnologyIds);
        xml = await ReplaceAsync(xml, _settings.CurrentAvailabilitySpecificationAttributeId, new[] { model.AvailabilityId });
        xml = await ReplaceAsync(xml, _settings.RelevantExperienceSpecificationAttributeId, new[] { model.RelevantExperienceId });
        xml = await ReplaceAsync(xml, _settings.MotherTongueSpecificationAttributeId, new[] { model.MotherTongueId });
        if (_settings.DataWriteMode == Nop.Plugin.Misc.JobSupport.Domain.Enums.DataAccessMode.Plugin)
        {
            customer.CustomCustomerAttributesXML = xml;
            await _profileService.EnsureProfileForCustomerAsync(customer, _settings);
            await _profileService.UpdatePluginProfileContentAsync(customer.Id, model.ShortDescription, model.Description);
            return;
        }

        customer.CustomCustomerAttributesXML = xml;
        await _customerService.UpdateCustomerAsync(customer);
        await _profileService.EnsureProfileForCustomerAsync(customer, _settings);

        var profile = customer.VendorId > 0 ? await _productService.GetProductByIdAsync(customer.VendorId) : null;
        if (profile != null)
        {
            profile.ShortDescription = model.ShortDescription ?? string.Empty;
            profile.FullDescription = model.Description ?? string.Empty;
            profile.UpdatedOnUtc = DateTime.UtcNow;
            await _productService.UpdateProductAsync(profile);
        }
    }

    public async Task<RelationshipListModel> PrepareRelationshipsAsync(Customer customer,
        RelationshipType relationshipType)
    {
        var result = await _profileQueryService.GetProfilesByRelationshipAsync(new ProfileSearchRequest
        {
            CustomerId = customer.Id,
            RelationshipType = relationshipType,
            PageIndex = 0,
            PageSize = _settings.DefaultPageSize,
            SortOrder = 0
        });
        var model = new RelationshipListModel { RelationshipType = relationshipType, QuerySucceeded = result.Succeeded };
        foreach (var item in result.Items)
            model.Profiles.Add(await _profileModelFactory.PrepareProfileCardAsync(item, customer, false));
        return model;
    }

    public async Task<SubscriptionModel> PrepareSubscriptionAsync(Customer customer, int storeId)
    {
        var summary = await _subscriptionService.GetSubscriptionAsync(customer.Id, storeId);
        var model = new SubscriptionModel
        {
            Status = await _localizationService.GetResourceAsync($"Plugins.Misc.JobSupport.Subscription.Status.{summary.Status}"),
            StartDate = summary.StartDate,
            ExpiryDate = summary.ExpiryDate,
            AllottedCredits = summary.AllottedCredits,
            UsedCredits = summary.UsedCredits,
            RemainingCredits = summary.RemainingCredits
        };
        foreach (var productId in new[]
                 {
                     _settings.ThreeMonthSubscriptionProductId,
                     _settings.SixMonthSubscriptionProductId,
                     _settings.OneYearSubscriptionProductId
                 }.Where(id => id > 0).Distinct())
        {
            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null || product.Deleted)
                continue;
            var slug = await _urlRecordService.GetSeNameAsync(product);
            model.Plans.Add(new SubscriptionPlanModel
            {
                Id = product.Id,
                Name = product.Name,
                Url = $"{_webHelper.GetStoreLocation()}{Uri.EscapeDataString(slug)}"
            });
        }
        return model;
    }

    public async Task<AffiliationModel> PrepareAffiliationsAsync(Customer customer)
    {
        var model = new AffiliationModel();
        if (customer.AffiliateId <= 0)
            return model;
        var affiliate = await _affiliateService.GetAffiliateByIdAsync(customer.AffiliateId);
        if (affiliate == null || affiliate.Deleted)
            return model;
        model.AffiliateUrl = await _affiliateService.GenerateUrlAsync(affiliate);
        foreach (var affiliated in await _customerService.GetAllCustomersAsync(affiliateId: affiliate.Id))
        {
            model.Customers.Add(new AffiliationCustomerModel
            {
                Id = affiliated.Id,
                Name = $"{affiliated.FirstName} {affiliated.LastName}".Trim(),
                CreatedOn = (await _dateTimeHelper.ConvertToUserTimeAsync(affiliated.CreatedOnUtc, DateTimeKind.Utc)).ToString("g")
            });
        }
        return model;
    }

    private List<int> Selected(string xml, int attributeId) => attributeId <= 0
        ? new List<int>()
        : _attributeParser.ParseValues(xml, attributeId)
            .Select(value => int.TryParse(value, out var id) ? id : 0).Where(id => id > 0).ToList();

    private async Task<IList<ProfileOptionModel>> OptionsAsync(int attributeId, IEnumerable<int> selectedIds)
    {
        if (attributeId <= 0)
            return new List<ProfileOptionModel>();
        var selected = selectedIds.Where(id => id > 0).ToHashSet();
        return (await _specificationAttributeService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(attributeId))
            .Select(option => new ProfileOptionModel { Id = option.Id, Name = option.Name, Selected = selected.Contains(option.Id) })
            .ToList();
    }

    private async Task<string> ReplaceAsync(string xml, int attributeId, IEnumerable<int> values)
    {
        if (attributeId <= 0)
            return xml;
        xml = _attributeParser.RemoveAttribute(xml, attributeId);
        var attribute = await _attributeService.GetAttributeByIdAsync(attributeId);
        if (attribute == null)
            return xml;
        foreach (var value in values.Where(value => value > 0).Distinct())
            xml = _attributeParser.AddAttribute(xml, attribute, value.ToString());
        return xml;
    }
}
