using LinqToDB;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Domain;
using Nop.Plugin.Misc.JobSupport.Domain.Enums;
using Nop.Plugin.Misc.JobSupport.Models.Account;
using Nop.Plugin.Misc.JobSupport.Services;
using Nop.Services.Affiliates;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Seo;

namespace Nop.Plugin.Misc.JobSupport.Factories;

public class JobSupportAccountModelFactory : IJobSupportAccountModelFactory
{
    private readonly IAffiliateService _affiliateService;
    private readonly ICustomerService _customerService;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly IJobSupportProfileModelFactory _profileModelFactory;
    private readonly IJobSupportProfileQueryService _profileQueryService;
    private readonly IJobSupportProfileService _profileService;
    private readonly IJobSupportSubscriptionService _subscriptionService;
    private readonly ILocalizationService _localizationService;
    private readonly IProductService _productService;
    private readonly IUrlRecordService _urlRecordService;
    private readonly IWebHelper _webHelper;
    private readonly JobSupportSettings _settings;
    private readonly IRepository<JobSupportProfile> _profileRepository;
    private readonly IRepository<JobSupportProfileSkill> _skillRepository;
    private readonly IRepository<JobSupportProfileAttributeDefinition> _attributeDefinitionRepository;
    private readonly IRepository<JobSupportProfileAttributeOption> _attributeOptionRepository;
    private readonly IRepository<JobSupportProfileAttributeValue> _attributeValueRepository;

    public JobSupportAccountModelFactory(IAffiliateService affiliateService,
        ICustomerService customerService,
        IDateTimeHelper dateTimeHelper,
        IJobSupportProfileModelFactory profileModelFactory,
        IJobSupportProfileQueryService profileQueryService,
        IJobSupportProfileService profileService,
        IJobSupportSubscriptionService subscriptionService,
        ILocalizationService localizationService,
        IProductService productService,
        IUrlRecordService urlRecordService,
        IWebHelper webHelper,
        JobSupportSettings settings,
        IRepository<JobSupportProfile> profileRepository,
        IRepository<JobSupportProfileSkill> skillRepository,
        IRepository<JobSupportProfileAttributeDefinition> attributeDefinitionRepository,
        IRepository<JobSupportProfileAttributeOption> attributeOptionRepository,
        IRepository<JobSupportProfileAttributeValue> attributeValueRepository)
    {
        _affiliateService = affiliateService;
        _customerService = customerService;
        _dateTimeHelper = dateTimeHelper;
        _profileModelFactory = profileModelFactory;
        _profileQueryService = profileQueryService;
        _profileService = profileService;
        _subscriptionService = subscriptionService;
        _localizationService = localizationService;
        _productService = productService;
        _urlRecordService = urlRecordService;
        _webHelper = webHelper;
        _settings = settings;
        _profileRepository = profileRepository;
        _skillRepository = skillRepository;
        _attributeDefinitionRepository = attributeDefinitionRepository;
        _attributeOptionRepository = attributeOptionRepository;
        _attributeValueRepository = attributeValueRepository;
    }

    public async Task<ProfileEditModel> PrepareProfileEditAsync(Customer customer, ProfileEditModel model = null)
    {
        model ??= new ProfileEditModel();
        var profile = await _profileRepository.Table.FirstOrDefaultAsync(item => item.CustomerId == customer.Id);
        if (profile != null)
        {
            model.ProfileTypeId = model.ProfileTypeId > 0 ? model.ProfileTypeId : profile.ProfileType;
            model.ShortDescription ??= profile.ShortDescription;
            model.Description ??= profile.FullDescription;
            if (!model.PrimaryTechnologyIds.Any())
                model.PrimaryTechnologyIds = await SelectedSkillIdsAsync(profile.Id, SkillType.PrimaryTechnology);
            if (!model.SecondaryTechnologyIds.Any())
                model.SecondaryTechnologyIds = await SelectedSkillIdsAsync(profile.Id, SkillType.SecondaryTechnology);
            model.AvailabilityId = model.AvailabilityId > 0
                ? model.AvailabilityId
                : (await SelectedAttributeIdsAsync(profile.Id, _settings.CurrentAvailabilitySpecificationAttributeId)).FirstOrDefault();
            model.RelevantExperienceId = model.RelevantExperienceId > 0
                ? model.RelevantExperienceId
                : (await SelectedAttributeIdsAsync(profile.Id, _settings.RelevantExperienceSpecificationAttributeId)).FirstOrDefault();
            model.MotherTongueId = model.MotherTongueId > 0
                ? model.MotherTongueId
                : (await SelectedAttributeIdsAsync(profile.Id, _settings.MotherTongueSpecificationAttributeId)).FirstOrDefault();
        }

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
        await _profileService.EnsureProfileForCustomerAsync(customer, _settings);
        var profile = await _profileRepository.Table.FirstOrDefaultAsync(item => item.CustomerId == customer.Id);
        if (profile == null)
            return;

        profile.ProfileType = model.ProfileTypeId;
        profile.ShortDescription = model.ShortDescription ?? string.Empty;
        profile.FullDescription = model.Description ?? string.Empty;
        profile.CurrentAvailability = await OptionNameAsync(
            _settings.CurrentAvailabilitySpecificationAttributeId,
            model.AvailabilityId);
        profile.RelevantExperience = await OptionNameAsync(
            _settings.RelevantExperienceSpecificationAttributeId,
            model.RelevantExperienceId);
        profile.MotherTongue = await OptionNameAsync(
            _settings.MotherTongueSpecificationAttributeId,
            model.MotherTongueId);
        profile.UpdatedOnUtc = DateTime.UtcNow;
        await _profileRepository.UpdateAsync(profile, false);

        await SaveSkillsAsync(profile.Id,
            _settings.PrimaryTechnologySpecificationAttributeId,
            model.PrimaryTechnologyIds,
            SkillType.PrimaryTechnology);
        await SaveSkillsAsync(profile.Id,
            _settings.SecondaryTechnologySpecificationAttributeId,
            model.SecondaryTechnologyIds,
            SkillType.SecondaryTechnology);

        await SaveAttributeValuesAsync(profile.Id,
            _settings.ProfileTypeSpecificationAttributeId,
            new[] { model.ProfileTypeId });
        await SaveAttributeValuesAsync(profile.Id,
            _settings.PrimaryTechnologySpecificationAttributeId,
            model.PrimaryTechnologyIds);
        await SaveAttributeValuesAsync(profile.Id,
            _settings.SecondaryTechnologySpecificationAttributeId,
            model.SecondaryTechnologyIds);
        await SaveAttributeValuesAsync(profile.Id,
            _settings.CurrentAvailabilitySpecificationAttributeId,
            new[] { model.AvailabilityId });
        await SaveAttributeValuesAsync(profile.Id,
            _settings.RelevantExperienceSpecificationAttributeId,
            new[] { model.RelevantExperienceId });
        await SaveAttributeValuesAsync(profile.Id,
            _settings.MotherTongueSpecificationAttributeId,
            new[] { model.MotherTongueId });
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
                CreatedOn = (await _dateTimeHelper.ConvertToUserTimeAsync(
                    affiliated.CreatedOnUtc,
                    DateTimeKind.Utc)).ToString("g")
            });
        }
        return model;
    }

    private async Task<List<int>> SelectedSkillIdsAsync(int profileId, SkillType skillType) =>
        (await _skillRepository.Table
            .Where(item => item.ProfileId == profileId && item.SkillType == (int)skillType)
            .ToListAsync())
        .Select(item => item.LegacySpecificationAttributeOptionId ?? 0)
        .Where(id => id > 0)
        .ToList();

    private async Task<List<int>> SelectedAttributeIdsAsync(int profileId, int sourceAttributeId)
    {
        var definition = await DefinitionAsync(sourceAttributeId);
        if (definition == null)
            return new List<int>();

        var values = await _attributeValueRepository.Table
            .Where(item => item.ProfileId == profileId &&
                           item.AttributeDefinitionId == definition.Id)
            .ToListAsync();
        var optionIds = values.Where(item => item.AttributeOptionId.HasValue)
            .Select(item => item.AttributeOptionId.Value)
            .ToArray();
        var options = optionIds.Length == 0
            ? new List<JobSupportProfileAttributeOption>()
            : (await _attributeOptionRepository.Table
                .Where(item => optionIds.Contains(item.Id))
                .ToListAsync()).ToList();
        return values.Select(value =>
            value.LegacyCustomerAttributeValueId ??
            options.FirstOrDefault(option => option.Id == value.AttributeOptionId)?.LegacyOptionId ??
            value.AttributeOptionId ??
            0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    private async Task<IList<ProfileOptionModel>> OptionsAsync(int sourceAttributeId,
        IEnumerable<int> selectedIds)
    {
        var definition = await DefinitionAsync(sourceAttributeId);
        if (definition == null)
            return new List<ProfileOptionModel>();

        var selected = selectedIds.Where(id => id > 0).ToHashSet();
        return (await _attributeOptionRepository.Table
                .Where(item => item.AttributeDefinitionId == definition.Id && item.IsActive)
                .OrderBy(item => item.DisplayOrder)
                .ThenBy(item => item.Name)
                .ToListAsync())
            .Select(option =>
            {
                var id = ExternalOptionId(option);
                return new ProfileOptionModel
                {
                    Id = id,
                    Name = option.Name,
                    Selected = selected.Contains(id)
                };
            })
            .ToList();
    }

    private async Task SaveSkillsAsync(int profileId,
        int sourceAttributeId,
        IEnumerable<int> selectedIds,
        SkillType skillType)
    {
        var existing = await _skillRepository.Table
            .Where(item => item.ProfileId == profileId && item.SkillType == (int)skillType)
            .ToListAsync();
        if (existing.Count > 0)
            await _skillRepository.DeleteAsync(existing, false);

        var now = DateTime.UtcNow;
        var displayOrder = 0;
        foreach (var optionId in selectedIds.Where(id => id > 0).Distinct())
        {
            await _skillRepository.InsertAsync(new JobSupportProfileSkill
            {
                ProfileId = profileId,
                SkillType = (int)skillType,
                Name = await OptionNameAsync(sourceAttributeId, optionId),
                LegacySpecificationAttributeId = sourceAttributeId > 0 ? sourceAttributeId : null,
                LegacySpecificationAttributeOptionId = optionId,
                DisplayOrder = displayOrder++,
                CreatedOnUtc = now,
                UpdatedOnUtc = now
            }, false);
        }
    }

    private async Task SaveAttributeValuesAsync(int profileId,
        int sourceAttributeId,
        IEnumerable<int> selectedIds)
    {
        var definition = await DefinitionAsync(sourceAttributeId);
        if (definition == null)
            return;

        var existing = await _attributeValueRepository.Table
            .Where(item => item.ProfileId == profileId &&
                           item.AttributeDefinitionId == definition.Id)
            .ToListAsync();
        if (existing.Count > 0)
            await _attributeValueRepository.DeleteAsync(existing, false);

        var now = DateTime.UtcNow;
        var displayOrder = 0;
        foreach (var externalId in selectedIds.Where(id => id > 0).Distinct())
        {
            var option = await _attributeOptionRepository.Table.FirstOrDefaultAsync(item =>
                item.AttributeDefinitionId == definition.Id &&
                (item.Id == externalId ||
                 item.LegacyCustomerAttributeValueId == externalId ||
                 item.LegacyOptionId == externalId));
            if (option == null)
                continue;

            await _attributeValueRepository.InsertAsync(new JobSupportProfileAttributeValue
            {
                ProfileId = profileId,
                AttributeDefinitionId = definition.Id,
                AttributeOptionId = option.Id,
                LegacyCustomerAttributeId = definition.LegacyCustomerAttributeId,
                LegacyCustomerAttributeValueId = option.LegacyCustomerAttributeValueId,
                DisplayOrder = displayOrder++,
                CreatedOnUtc = now,
                UpdatedOnUtc = now
            }, false);
        }
    }

    private Task<JobSupportProfileAttributeDefinition> DefinitionAsync(int sourceAttributeId)
    {
        if (sourceAttributeId <= 0)
            return Task.FromResult<JobSupportProfileAttributeDefinition>(null);

        return _attributeDefinitionRepository.Table.FirstOrDefaultAsync(item =>
            item.LegacyCustomerAttributeId == sourceAttributeId && item.IsActive);
    }

    private async Task<string> OptionNameAsync(int sourceAttributeId, int externalOptionId)
    {
        var definition = await DefinitionAsync(sourceAttributeId);
        if (definition == null || externalOptionId <= 0)
            return string.Empty;

        var option = await _attributeOptionRepository.Table.FirstOrDefaultAsync(item =>
            item.AttributeDefinitionId == definition.Id &&
            (item.Id == externalOptionId ||
             item.LegacyCustomerAttributeValueId == externalOptionId ||
             item.LegacyOptionId == externalOptionId));
        return option?.Name ?? string.Empty;
    }

    private static int ExternalOptionId(JobSupportProfileAttributeOption option) =>
        option.LegacyCustomerAttributeValueId ?? option.LegacyOptionId ?? option.Id;
}
