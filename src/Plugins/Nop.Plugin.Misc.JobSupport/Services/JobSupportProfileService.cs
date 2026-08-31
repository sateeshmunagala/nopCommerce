using System.Text;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Services.Attributes;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Seo;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportProfileService : IJobSupportProfileService
{
    private readonly IJobSupportAffiliateService _affiliateService;
    private readonly IAttributeParser<CustomerAttribute, CustomerAttributeValue> _attributeParser;
    private readonly ICategoryService _categoryService;
    private readonly ICustomerActivityService _customerActivityService;
    private readonly ICustomerService _customerService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly IJobSupportNotificationService _notificationService;
    private readonly ILogger _logger;
    private readonly IPictureService _pictureService;
    private readonly IProductService _productService;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IUrlRecordService _urlRecordService;

    public JobSupportProfileService(IJobSupportAffiliateService affiliateService,
        IAttributeParser<CustomerAttribute, CustomerAttributeValue> attributeParser,
        ICategoryService categoryService,
        ICustomerActivityService customerActivityService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        IJobSupportNotificationService notificationService,
        ILogger logger,
        IPictureService pictureService,
        IProductService productService,
        ISpecificationAttributeService specificationAttributeService,
        IUrlRecordService urlRecordService)
    {
        _affiliateService = affiliateService;
        _attributeParser = attributeParser;
        _categoryService = categoryService;
        _customerActivityService = customerActivityService;
        _customerService = customerService;
        _genericAttributeService = genericAttributeService;
        _notificationService = notificationService;
        _logger = logger;
        _pictureService = pictureService;
        _productService = productService;
        _specificationAttributeService = specificationAttributeService;
        _urlRecordService = urlRecordService;
    }

    public async Task EnsureProfileForCustomerAsync(Customer customer, JobSupportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(settings);

        var selectedOptionIds = GetSelectedOptionIds(customer, settings);
        var selectedOptions = selectedOptionIds.Count == 0
            ? new List<SpecificationAttributeOption>()
            : (await _specificationAttributeService.GetSpecificationAttributeOptionsByIdsAsync(selectedOptionIds.ToArray())).ToList();
        var profileTypeOption = GetProfileTypeOption(customer, settings, selectedOptions);
        var role = await ResolveProfileRoleAsync(profileTypeOption, settings);
        var profile = await GetLinkedProfileAsync(customer);
        var categoryIds = await ResolveCategoryIdsAsync(selectedOptions);

        if (settings.ExecutionMode == WorkflowExecutionMode.Shadow)
        {
            await _logger.InformationAsync(
                $"JobSupport shadow registration outcome: customer {customer.Id}, profile {(profile == null ? "create" : "update")}, " +
                $"role {(role?.SystemName ?? "unresolved")}, specification mappings {selectedOptions.Count}, category mappings {categoryIds.Count}.");
            return;
        }

        if (settings.ExecutionMode != WorkflowExecutionMode.Live)
            return;

        var changed = false;
        if (profileTypeOption != null && customer.CustomerProfileTypeId != profileTypeOption.Id)
        {
            customer.CustomerProfileTypeId = profileTypeOption.Id;
            changed = true;
        }

        if (role != null && !await _customerService.IsInCustomerRoleAsync(customer, role.SystemName, false))
        {
            await _customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping
            {
                CustomerId = customer.Id,
                CustomerRoleId = role.Id
            });
            changed = true;
        }

        var now = DateTime.UtcNow;
        if (profile == null)
        {
            profile = new Product
            {
                ProductTypeId = (int)ProductType.SimpleProduct,
                VisibleIndividually = true,
                Name = GetDisplayName(customer),
                ShortDescription = string.Empty,
                FullDescription = string.Empty,
                VendorId = customer.Id,
                Published = false,
                Deleted = false,
                Sku = $"JobSupport-{customer.CustomerGuid:N}",
                AllowCustomerReviews = true,
                IsShipEnabled = false,
                IsTaxExempt = true,
                OrderMinimumQuantity = 1,
                OrderMaximumQuantity = 1,
                CreatedOnUtc = now,
                UpdatedOnUtc = now
            };
            await _productService.InsertProductAsync(profile);
            changed = true;
        }
        else
        {
            var displayName = GetDisplayName(customer);
            if (!string.Equals(profile.Name, displayName, StringComparison.Ordinal) || profile.VendorId != customer.Id)
            {
                profile.Name = displayName;
                profile.VendorId = customer.Id;
                profile.UpdatedOnUtc = now;
                await _productService.UpdateProductAsync(profile);
                changed = true;
            }
        }

        if (customer.VendorId != profile.Id || changed)
        {
            customer.VendorId = profile.Id;
            await _customerService.UpdateCustomerAsync(customer);
        }

        changed |= await EnsureCategoryMappingsAsync(profile, categoryIds);
        changed |= await EnsureSpecificationMappingsAsync(profile, selectedOptions, settings);

        var slug = await _urlRecordService.ValidateSeNameAsync(profile, string.Empty, profile.Name, true);
        var existingSlug = await _urlRecordService.GetSeNameAsync(profile, returnDefaultValue: false);
        if (!string.Equals(existingSlug, slug, StringComparison.OrdinalIgnoreCase))
        {
            await _urlRecordService.SaveSlugAsync(profile, slug, 0);
            changed = true;
        }

        var customerAttributes = await _genericAttributeService.GetAttributesForEntityAsync(customer.Id, nameof(Customer));
        if (customerAttributes.All(attribute => attribute.Key != JobSupportDefaults.NotifiedAboutAvailabilityAttribute))
        {
            await _genericAttributeService.SaveAttributeAsync(customer,
                JobSupportDefaults.NotifiedAboutAvailabilityAttribute,
                string.Empty);
            changed = true;
        }

        await _affiliateService.EnsureAffiliateAsync(customer, settings.ExecutionMode);

        var registrationRecorded = await _genericAttributeService.GetAttributeAsync<bool>(customer,
            JobSupportDefaults.RegistrationCompletedAttribute);
        if (!registrationRecorded)
        {
            await _customerActivityService.InsertActivityAsync(customer,
                JobSupportDefaults.ActivityTypeSystemName,
                $"JobSupport registration workflow applied for profile {profile.Id}.",
                profile);
            await _genericAttributeService.SaveAttributeAsync(customer,
                JobSupportDefaults.RegistrationCompletedAttribute,
                true);
        }
        else if (changed)
        {
            await _logger.InformationAsync($"JobSupport registration replay updated profile {profile.Id} for customer {customer.Id}.");
        }
    }

    public async Task ActivateProfileAsync(Customer customer, JobSupportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(settings);

        var profile = await GetLinkedProfileAsync(customer);
        if (profile == null || profile.Deleted || !customer.Active || customer.Deleted)
            return;

        var selectedOptions = await GetSelectedOptionsAsync(customer, settings);
        var primaryOptionIds = GetAttributeOptionIds(customer, settings.PrimaryTechnologySpecificationAttributeId);
        var targetRole = await ResolveOppositeRoleAsync(customer, settings);
        var recipients = new List<Customer>();

        if (targetRole != null && primaryOptionIds.Count > 0)
        {
            var candidates = await _customerService.GetAllCustomersAsync(customerRoleIds: new[] { targetRole.Id });
            recipients.AddRange(candidates.Where(candidate => candidate.Id != customer.Id &&
                candidate.Active && !candidate.Deleted &&
                GetAttributeOptionIds(candidate, settings.PrimaryTechnologySpecificationAttributeId)
                    .Intersect(primaryOptionIds).Any()));
        }

        if (settings.ExecutionMode == WorkflowExecutionMode.Shadow)
        {
            await _logger.InformationAsync(
                $"JobSupport shadow activation outcome: publish profile {profile.Id} and evaluate {recipients.Count} matching recipients.");
            return;
        }

        if (settings.ExecutionMode != WorkflowExecutionMode.Live)
            return;

        if (!profile.Published)
        {
            profile.Published = true;
            profile.UpdatedOnUtc = DateTime.UtcNow;
            await _productService.UpdateProductAsync(profile);
        }

        foreach (var recipient in recipients)
        {
            var notifiedIds = ParseIdentifiers(await _genericAttributeService.GetAttributeAsync<string>(recipient,
                JobSupportDefaults.NotifiedAboutAvailabilityAttribute));
            if (notifiedIds.Contains(customer.Id))
                continue;

            if (await _notificationService.QueueProfileAvailableNotificationAsync(profile, recipient))
            {
                notifiedIds.Add(customer.Id);
                await _genericAttributeService.SaveAttributeAsync(recipient,
                    JobSupportDefaults.NotifiedAboutAvailabilityAttribute,
                    string.Join(',', notifiedIds.OrderBy(id => id)));
            }
        }
    }

    public async Task<RelationshipActionResult> UpdateAvailabilityAsync(int customerId,
        string availability,
        WorkflowExecutionMode mode)
    {
        var result = new RelationshipActionResult
        {
            SourceCustomerId = customerId,
            UserMessageKey = "Plugins.Misc.JobSupport.Availability.Updated"
        };
        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        if (customer == null || customer.Deleted)
        {
            result.ErrorCode = "CustomerNotFound";
            return result;
        }

        var normalizedAvailability = availability?.Trim() ?? string.Empty;
        var previousAvailability = await _genericAttributeService.GetAttributeAsync<string>(customer,
            JobSupportDefaults.CurrentAvailabilityAttribute);
        if (string.Equals(previousAvailability, normalizedAvailability, StringComparison.OrdinalIgnoreCase))
        {
            result.Succeeded = true;
            result.AlreadyApplied = true;
            return result;
        }

        var becameAvailable = IsUnavailable(previousAvailability) && IsAvailable(normalizedAvailability);
        if (mode == WorkflowExecutionMode.Shadow)
        {
            await _logger.InformationAsync(
                $"JobSupport shadow availability outcome: customer {customerId}, transition reset {becameAvailable}.");
            result.Succeeded = true;
            return result;
        }

        if (mode != WorkflowExecutionMode.Live)
        {
            result.ErrorCode = "WorkflowDisabled";
            return result;
        }

        await _genericAttributeService.SaveAttributeAsync(customer,
            JobSupportDefaults.CurrentAvailabilityAttribute,
            normalizedAvailability);
        if (becameAvailable)
        {
            await _genericAttributeService.SaveAttributeAsync(customer,
                JobSupportDefaults.NotifiedAboutAvailabilityAttribute,
                string.Empty);
            await _customerActivityService.InsertActivityAsync(customer,
                JobSupportDefaults.ActivityTypeSystemName,
                "JobSupport availability changed from unavailable to available.",
                customer);
        }

        result.Succeeded = true;
        return result;
    }

    public async Task SynchronizeAvatarAsync(GenericAttribute attribute, WorkflowExecutionMode mode)
    {
        if (!string.Equals(attribute?.Key, NopCustomerDefaults.AvatarPictureIdAttribute, StringComparison.Ordinal))
            return;
        if (!string.Equals(attribute.KeyGroup, nameof(Customer), StringComparison.Ordinal))
            return;
        if (!int.TryParse(attribute.Value, out var pictureId) || pictureId <= 0)
            return;

        var picture = await _pictureService.GetPictureByIdAsync(pictureId);
        if (picture == null)
            return;

        var customer = await _customerService.GetCustomerByIdAsync(attribute.EntityId);
        if (customer == null || customer.Deleted)
            return;
        var profile = await GetLinkedProfileAsync(customer);
        if (profile == null || profile.Deleted)
            return;

        var mappings = await _productService.GetProductPicturesByProductIdAsync(profile.Id);
        var matchingMapping = mappings.FirstOrDefault(mapping => mapping.PictureId == pictureId);
        if (mappings.Count == 1 && matchingMapping != null)
            return;

        if (mode == WorkflowExecutionMode.Shadow)
        {
            await _logger.InformationAsync(
                $"JobSupport shadow avatar outcome: profile {profile.Id}, picture {pictureId}, surplus mappings {Math.Max(0, mappings.Count - 1)}.");
            return;
        }

        if (mode != WorkflowExecutionMode.Live)
            return;

        foreach (var mapping in mappings.Where(mapping => mapping.Id != matchingMapping?.Id))
            await _productService.DeleteProductPictureAsync(mapping);

        if (matchingMapping == null)
        {
            await _productService.InsertProductPictureAsync(new ProductPicture
            {
                ProductId = profile.Id,
                PictureId = pictureId,
                DisplayOrder = 0
            });
        }
    }

    private async Task<Product> GetLinkedProfileAsync(Customer customer)
    {
        if (customer.VendorId > 0)
        {
            var linked = await _productService.GetProductByIdAsync(customer.VendorId);
            if (linked != null && !linked.Deleted && linked.VendorId == customer.Id)
                return linked;
        }

        return (await _productService.SearchProductsAsync(vendorId: customer.Id,
            showHidden: true,
            overridePublished: null)).FirstOrDefault();
    }

    private async Task<IList<SpecificationAttributeOption>> GetSelectedOptionsAsync(Customer customer,
        JobSupportSettings settings)
    {
        var optionIds = GetSelectedOptionIds(customer, settings);
        return optionIds.Count == 0
            ? new List<SpecificationAttributeOption>()
            : await _specificationAttributeService.GetSpecificationAttributeOptionsByIdsAsync(optionIds.ToArray());
    }

    private List<int> GetSelectedOptionIds(Customer customer, JobSupportSettings settings)
    {
        return GetConfiguredSpecificationAttributeIds(settings)
            .SelectMany(attributeId => GetAttributeOptionIds(customer, attributeId))
            .Distinct()
            .ToList();
    }

    private List<int> GetAttributeOptionIds(Customer customer, int attributeId)
    {
        if (attributeId <= 0 || string.IsNullOrWhiteSpace(customer.CustomCustomerAttributesXML))
            return new List<int>();

        return _attributeParser.ParseValues(customer.CustomCustomerAttributesXML, attributeId)
            .Select(value => int.TryParse(value, out var optionId) ? optionId : 0)
            .Where(optionId => optionId > 0)
            .Distinct()
            .ToList();
    }

    private static IEnumerable<int> GetConfiguredSpecificationAttributeIds(JobSupportSettings settings)
    {
        return new[]
        {
            settings.ProfileTypeSpecificationAttributeId,
            settings.CurrentAvailabilitySpecificationAttributeId,
            settings.RelevantExperienceSpecificationAttributeId,
            settings.MotherTongueSpecificationAttributeId,
            settings.PrimaryTechnologySpecificationAttributeId,
            settings.SecondaryTechnologySpecificationAttributeId
        }.Where(id => id > 0).Distinct();
    }

    private SpecificationAttributeOption GetProfileTypeOption(Customer customer,
        JobSupportSettings settings,
        IEnumerable<SpecificationAttributeOption> selectedOptions)
    {
        var profileTypeOptionId = GetAttributeOptionIds(customer, settings.ProfileTypeSpecificationAttributeId)
            .FirstOrDefault();
        return selectedOptions.FirstOrDefault(option => option.Id == profileTypeOptionId);
    }

    private async Task<CustomerRole> ResolveProfileRoleAsync(SpecificationAttributeOption profileTypeOption,
        JobSupportSettings settings)
    {
        if (profileTypeOption == null)
            return null;

        var roles = new[]
        {
            await _customerService.GetCustomerRoleBySystemNameAsync(settings.GiveSupportRoleSystemName),
            await _customerService.GetCustomerRoleBySystemNameAsync(settings.TakeSupportRoleSystemName)
        }.Where(role => role != null);
        var optionName = NormalizeName(profileTypeOption.Name);
        return roles.FirstOrDefault(role => NormalizeName(role.Name) == optionName ||
            NormalizeName(role.SystemName).EndsWith(optionName, StringComparison.Ordinal));
    }

    private async Task<CustomerRole> ResolveOppositeRoleAsync(Customer customer, JobSupportSettings settings)
    {
        var giveRole = await _customerService.GetCustomerRoleBySystemNameAsync(settings.GiveSupportRoleSystemName);
        var takeRole = await _customerService.GetCustomerRoleBySystemNameAsync(settings.TakeSupportRoleSystemName);
        if (giveRole != null && await _customerService.IsInCustomerRoleAsync(customer, giveRole.SystemName, false))
            return takeRole;
        if (takeRole != null && await _customerService.IsInCustomerRoleAsync(customer, takeRole.SystemName, false))
            return giveRole;
        return null;
    }

    private async Task<IList<int>> ResolveCategoryIdsAsync(IEnumerable<SpecificationAttributeOption> options)
    {
        var categoryIds = new List<int>();
        foreach (var optionName in options.Select(option => option.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct())
        {
            var matches = await _categoryService.GetAllCategoriesAsync(optionName,
                showHidden: true,
                overridePublished: null);
            var match = matches.FirstOrDefault(category =>
                string.Equals(category.Name?.Trim(), optionName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null)
                categoryIds.Add(match.Id);
        }

        return categoryIds.Distinct().ToList();
    }

    private async Task<bool> EnsureCategoryMappingsAsync(Product profile, IEnumerable<int> categoryIds)
    {
        var changed = false;
        var existing = await _categoryService.GetProductCategoriesByProductIdAsync(profile.Id, true);
        foreach (var categoryId in categoryIds.Where(categoryId => existing.All(mapping => mapping.CategoryId != categoryId)))
        {
            await _categoryService.InsertProductCategoryAsync(new ProductCategory
            {
                ProductId = profile.Id,
                CategoryId = categoryId,
                DisplayOrder = 0
            });
            changed = true;
        }

        return changed;
    }

    private async Task<bool> EnsureSpecificationMappingsAsync(Product profile,
        IEnumerable<SpecificationAttributeOption> selectedOptions,
        JobSupportSettings settings)
    {
        var changed = false;
        var profileTypeIds = GetAttributeOptionIdsFromOptions(selectedOptions,
            settings.ProfileTypeSpecificationAttributeId);
        var existing = await _specificationAttributeService.GetProductSpecificationAttributesAsync(profile.Id);
        foreach (var option in selectedOptions.Where(option =>
                     existing.All(mapping => mapping.SpecificationAttributeOptionId != option.Id)))
        {
            await _specificationAttributeService.InsertProductSpecificationAttributeAsync(
                new ProductSpecificationAttribute
                {
                    ProductId = profile.Id,
                    SpecificationAttributeOptionId = option.Id,
                    AllowFiltering = !profileTypeIds.Contains(option.Id),
                    ShowOnProductPage = true
                });
            changed = true;
        }

        return changed;
    }

    private static HashSet<int> GetAttributeOptionIdsFromOptions(IEnumerable<SpecificationAttributeOption> options,
        int specificationAttributeId)
    {
        return options.Where(option => option.SpecificationAttributeId == specificationAttributeId)
            .Select(option => option.Id)
            .ToHashSet();
    }

    private static HashSet<int> ParseIdentifiers(string value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => int.TryParse(token, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToHashSet();
    }

    private static string GetDisplayName(Customer customer)
    {
        var displayName = $"{customer.FirstName} {customer.LastName}".Trim();
        return string.IsNullOrWhiteSpace(displayName) ? $"Job Support Profile {customer.Id}" : displayName;
    }

    private static string NormalizeName(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static bool IsAvailable(string value) =>
        string.Equals(value?.Trim(), "available", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnavailable(string value) =>
        string.Equals(value?.Trim(), "unavailable", StringComparison.OrdinalIgnoreCase);
}
