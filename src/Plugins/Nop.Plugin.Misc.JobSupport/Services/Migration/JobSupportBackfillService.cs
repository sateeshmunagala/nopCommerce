using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LinqToDB;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Domain;
using Nop.Plugin.Misc.JobSupport.Domain.Enums;

namespace Nop.Plugin.Misc.JobSupport.Services.Migration;

public partial class JobSupportBackfillService : IJobSupportBackfillService
{
    private const string PROFILES = "Profiles";
    private const string SKILLS = "SkillsAndAttributes";
    private const string RELATIONSHIPS = "Relationships";
    private const string VIEWS_REVEALS = "ViewsAndReveals";
    private const string SUBSCRIPTIONS = "Subscriptions";

    private static readonly Regex EmailPattern = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhonePattern = new(@"^\+?[\d\s().-]{7,}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<CustomerAttribute> _customerAttributeRepository;
    private readonly IRepository<CustomerAttributeValue> _customerAttributeValueRepository;
    private readonly IRepository<GenericAttribute> _genericAttributeRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderItem> _orderItemRepository;
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<ProductSpecificationAttribute> _productSpecificationRepository;
    private readonly IRepository<ShoppingCartItem> _shoppingCartItemRepository;
    private readonly IRepository<SpecificationAttributeOption> _specificationOptionRepository;
    private readonly IRepository<JobSupportContactReveal> _contactRevealRepository;
    private readonly IRepository<JobSupportMigrationCheckpoint> _checkpointRepository;
    private readonly IRepository<JobSupportProfile> _profileRepository;
    private readonly IRepository<JobSupportProfileAttributeDefinition> _attributeDefinitionRepository;
    private readonly IRepository<JobSupportProfileAttributeOption> _attributeOptionRepository;
    private readonly IRepository<JobSupportProfileAttributeValue> _attributeValueRepository;
    private readonly IRepository<JobSupportProfileSkill> _skillRepository;
    private readonly IRepository<JobSupportProfileView> _profileViewRepository;
    private readonly IRepository<JobSupportRelationship> _relationshipRepository;
    private readonly IRepository<JobSupportSubscription> _subscriptionRepository;
    private readonly JobSupportSettings _settings;
    private readonly ShoppingCartSettings _shoppingCartSettings;

    public JobSupportBackfillService(IRepository<Customer> customerRepository,
        IRepository<CustomerAttribute> customerAttributeRepository,
        IRepository<CustomerAttributeValue> customerAttributeValueRepository,
        IRepository<GenericAttribute> genericAttributeRepository,
        IRepository<Order> orderRepository,
        IRepository<OrderItem> orderItemRepository,
        IRepository<Product> productRepository,
        IRepository<ProductSpecificationAttribute> productSpecificationRepository,
        IRepository<ShoppingCartItem> shoppingCartItemRepository,
        IRepository<SpecificationAttributeOption> specificationOptionRepository,
        IRepository<JobSupportContactReveal> contactRevealRepository,
        IRepository<JobSupportMigrationCheckpoint> checkpointRepository,
        IRepository<JobSupportProfile> profileRepository,
        IRepository<JobSupportProfileAttributeDefinition> attributeDefinitionRepository,
        IRepository<JobSupportProfileAttributeOption> attributeOptionRepository,
        IRepository<JobSupportProfileAttributeValue> attributeValueRepository,
        IRepository<JobSupportProfileSkill> skillRepository,
        IRepository<JobSupportProfileView> profileViewRepository,
        IRepository<JobSupportRelationship> relationshipRepository,
        IRepository<JobSupportSubscription> subscriptionRepository,
        JobSupportSettings settings,
        ShoppingCartSettings shoppingCartSettings)
    {
        _customerRepository = customerRepository;
        _customerAttributeRepository = customerAttributeRepository;
        _customerAttributeValueRepository = customerAttributeValueRepository;
        _genericAttributeRepository = genericAttributeRepository;
        _orderRepository = orderRepository;
        _orderItemRepository = orderItemRepository;
        _productRepository = productRepository;
        _productSpecificationRepository = productSpecificationRepository;
        _shoppingCartItemRepository = shoppingCartItemRepository;
        _specificationOptionRepository = specificationOptionRepository;
        _contactRevealRepository = contactRevealRepository;
        _checkpointRepository = checkpointRepository;
        _profileRepository = profileRepository;
        _attributeDefinitionRepository = attributeDefinitionRepository;
        _attributeOptionRepository = attributeOptionRepository;
        _attributeValueRepository = attributeValueRepository;
        _skillRepository = skillRepository;
        _profileViewRepository = profileViewRepository;
        _relationshipRepository = relationshipRepository;
        _subscriptionRepository = subscriptionRepository;
        _settings = settings;
        _shoppingCartSettings = shoppingCartSettings;
    }

    public async Task<BackfillStepResult> BackfillProfilesAsync(int batchSize, CancellationToken cancellationToken)
    {
        var checkpoint = await GetCheckpointAsync(PROFILES, cancellationToken);
        var customers = await _customerRepository.Table
            .Where(customer => customer.Id > checkpoint.LastProcessedId && !customer.Deleted && customer.CustomerProfileTypeId > 0)
            .OrderBy(customer => customer.Id)
            .Take(NormalizeBatchSize(batchSize))
            .ToListAsync(cancellationToken);
        if (customers.Count == 0)
            return await CompleteAsync(checkpoint);

        var customerIds = customers.Select(customer => customer.Id).ToArray();
        var products = await _productRepository.Table
            .Where(product => customerIds.Contains(product.VendorId) && !product.Deleted)
            .OrderBy(product => product.Id)
            .ToListAsync(cancellationToken);
        var existingProfiles = await _profileRepository.Table
            .Where(profile => customerIds.Contains(profile.CustomerId))
            .ToListAsync(cancellationToken);
        var attributes = await _genericAttributeRepository.Table
            .Where(attribute => customerIds.Contains(attribute.EntityId) && attribute.KeyGroup == nameof(Customer) &&
                (attribute.Key == Nop.Core.Domain.Customers.NopCustomerDefaults.AvatarPictureIdAttribute ||
                 attribute.Key == JobSupportDefaults.CurrentAvailabilityAttribute))
            .OrderBy(attribute => attribute.Id)
            .ToListAsync(cancellationToken);

        var productIds = products.Select(product => product.Id).ToArray();
        var descriptiveOptions = productIds.Length == 0
            ? new List<SpecificationProjection>()
            : await (from mapping in _productSpecificationRepository.Table
                join option in _specificationOptionRepository.Table on mapping.SpecificationAttributeOptionId equals option.Id
                where productIds.Contains(mapping.ProductId) &&
                      (option.SpecificationAttributeId == _settings.MotherTongueSpecificationAttributeId ||
                       option.SpecificationAttributeId == _settings.RelevantExperienceSpecificationAttributeId)
                select new SpecificationProjection(mapping.ProductId, option.SpecificationAttributeId, option.Name, mapping.DisplayOrder))
                .ToListAsync(cancellationToken);

        var inserts = new List<JobSupportProfile>();
        var updates = new List<JobSupportProfile>();
        var skipped = 0L;
        var errors = new List<string>();
        foreach (var customer in customers)
        {
            var matches = products.Where(product => product.VendorId == customer.Id).ToList();
            if (matches.Count != 1)
            {
                skipped++;
                errors.Add(matches.Count == 0
                    ? $"profile-source-missing:{customer.Id}"
                    : $"profile-source-ambiguous:{customer.Id}");
                continue;
            }

            var product = matches[0];
            var currentAvailability = LatestAttribute(attributes, customer.Id, JobSupportDefaults.CurrentAvailabilityAttribute);
            var avatarValue = LatestAttribute(attributes, customer.Id, Nop.Core.Domain.Customers.NopCustomerDefaults.AvatarPictureIdAttribute);
            var entity = existingProfiles.FirstOrDefault(profile => profile.CustomerId == customer.Id) ?? new JobSupportProfile();
            entity.CustomerId = customer.Id;
            entity.LegacyProductId = product.Id;
            entity.ProfileType = customer.CustomerProfileTypeId;
            entity.DisplayName = product.Name;
            entity.ShortDescription = product.ShortDescription;
            entity.FullDescription = product.FullDescription;
            entity.CurrentAvailability = currentAvailability;
            entity.MotherTongue = FirstOption(descriptiveOptions, product.Id, _settings.MotherTongueSpecificationAttributeId);
            entity.RelevantExperience = FirstOption(descriptiveOptions, product.Id, _settings.RelevantExperienceSpecificationAttributeId);
            entity.AvatarPictureId = PositiveOrNull(ParseInt(avatarValue));
            entity.CountryId = PositiveOrNull(customer.CountryId);
            entity.StateProvinceId = PositiveOrNull(customer.StateProvinceId);
            entity.City = customer.City;
            entity.IsPublished = product.Published;
            entity.CreatedOnUtc = product.CreatedOnUtc;
            entity.UpdatedOnUtc = product.UpdatedOnUtc;
            entity.MigrationSource = nameof(Product);
            entity.LegacySourceId = product.Id;
            if (entity.Id == 0)
                inserts.Add(entity);
            else
                updates.Add(entity);
        }

        await PersistAsync(inserts, updates, _profileRepository, checkpoint, customers[^1].Id,
            customers.Count - skipped, skipped, errors);
        return Result(checkpoint, errors);
    }

    public async Task<BackfillStepResult> BackfillSkillsAsync(int batchSize, CancellationToken cancellationToken)
    {
        var checkpoint = await GetCheckpointAsync(SKILLS, cancellationToken);
        var profiles = await _profileRepository.Table
            .Where(profile => profile.Id > checkpoint.LastProcessedId)
            .OrderBy(profile => profile.Id)
            .Take(NormalizeBatchSize(batchSize))
            .ToListAsync(cancellationToken);
        if (profiles.Count == 0)
            return await CompleteAsync(checkpoint);

        var profileIds = profiles.Select(profile => profile.Id).ToArray();
        var legacyProductIds = profiles.Where(profile => profile.LegacyProductId.HasValue)
            .Select(profile => profile.LegacyProductId.Value).ToArray();
        var customerIds = profiles.Select(profile => profile.CustomerId).ToArray();
        var customers = await _customerRepository.Table.Where(customer => customerIds.Contains(customer.Id)).ToListAsync(cancellationToken);
        var mappings = legacyProductIds.Length == 0
            ? new List<SkillProjection>()
            : await (from mapping in _productSpecificationRepository.Table
                join option in _specificationOptionRepository.Table on mapping.SpecificationAttributeOptionId equals option.Id
                where legacyProductIds.Contains(mapping.ProductId) &&
                      (option.SpecificationAttributeId == _settings.PrimaryTechnologySpecificationAttributeId ||
                       option.SpecificationAttributeId == _settings.SecondaryTechnologySpecificationAttributeId)
                select new SkillProjection(mapping.ProductId, option.SpecificationAttributeId, option.Id, option.Name, mapping.DisplayOrder))
                .ToListAsync(cancellationToken);
        var existingSkills = await _skillRepository.Table.Where(skill => profileIds.Contains(skill.ProfileId)).ToListAsync(cancellationToken);

        var skillInserts = new List<JobSupportProfileSkill>();
        var now = DateTime.UtcNow;
        foreach (var profile in profiles.Where(profile => profile.LegacyProductId.HasValue))
        foreach (var mapping in mappings.Where(mapping => mapping.ProductId == profile.LegacyProductId.Value))
        {
            var skillType = mapping.AttributeId == _settings.PrimaryTechnologySpecificationAttributeId
                ? (int)SkillType.PrimaryTechnology
                : (int)SkillType.SecondaryTechnology;
            if (existingSkills.Any(skill => skill.ProfileId == profile.Id && skill.SkillType == skillType && skill.Name == mapping.Name))
                continue;
            skillInserts.Add(new JobSupportProfileSkill
            {
                ProfileId = profile.Id,
                SkillType = skillType,
                Name = mapping.Name,
                LegacySpecificationAttributeId = mapping.AttributeId,
                LegacySpecificationAttributeOptionId = mapping.OptionId,
                DisplayOrder = mapping.DisplayOrder,
                CreatedOnUtc = now,
                UpdatedOnUtc = now
            });
        }

        var parsedValues = customers.SelectMany(customer => ParseCustomerAttributes(customer.CustomCustomerAttributesXML)
                .Select(value => new ParsedAttributeValue(customer.Id, value.AttributeId, value.Value)))
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .ToList();
        var attributeIds = parsedValues.Select(value => value.AttributeId).Distinct().ToArray();
        var legacyDefinitions = attributeIds.Length == 0
            ? new List<CustomerAttribute>()
            : await _customerAttributeRepository.Table.Where(attribute => attributeIds.Contains(attribute.Id)).ToListAsync(cancellationToken);
        legacyDefinitions = legacyDefinitions.Where(attribute => !IsContactField(attribute.Name)).ToList();
        var safeAttributeIds = legacyDefinitions.Select(attribute => attribute.Id).ToArray();
        parsedValues = parsedValues.Where(value => safeAttributeIds.Contains(value.AttributeId) && !LooksLikeContactValue(value.Value)).ToList();

        var definitions = await _attributeDefinitionRepository.Table
            .Where(definition => safeAttributeIds.Contains(definition.LegacyCustomerAttributeId))
            .ToListAsync(cancellationToken);
        var newDefinitions = legacyDefinitions.Where(attribute => definitions.All(definition => definition.LegacyCustomerAttributeId != attribute.Id))
            .Select(attribute => new JobSupportProfileAttributeDefinition
            {
                LegacyCustomerAttributeId = attribute.Id,
                Name = attribute.Name,
                IsRequired = attribute.IsRequired,
                DisplayOrder = attribute.DisplayOrder,
                CreatedOnUtc = now,
                UpdatedOnUtc = now
            }).ToList();
        if (newDefinitions.Count > 0)
        {
            await _attributeDefinitionRepository.InsertAsync(newDefinitions, false);
            definitions.AddRange(newDefinitions);
        }

        var numericValueIds = parsedValues.Select(value => ParseInt(value.Value)).Where(id => id > 0).Distinct().ToArray();
        var legacyOptions = numericValueIds.Length == 0
            ? new List<CustomerAttributeValue>()
            : await _customerAttributeValueRepository.Table.Where(option => numericValueIds.Contains(option.Id)).ToListAsync(cancellationToken);
        var definitionIds = definitions.Select(definition => definition.Id).ToArray();
        var options = await _attributeOptionRepository.Table
            .Where(option => definitionIds.Contains(option.AttributeDefinitionId))
            .ToListAsync(cancellationToken);
        var newOptions = (from legacyOption in legacyOptions
            join definition in definitions on legacyOption.AttributeId equals definition.LegacyCustomerAttributeId
            where options.All(option => option.LegacyCustomerAttributeValueId != legacyOption.Id)
            select new JobSupportProfileAttributeOption
            {
                AttributeDefinitionId = definition.Id,
                LegacyCustomerAttributeValueId = legacyOption.Id,
                Name = legacyOption.Name,
                DisplayOrder = legacyOption.DisplayOrder
            }).ToList();
        if (newOptions.Count > 0)
        {
            await _attributeOptionRepository.InsertAsync(newOptions, false);
            options.AddRange(newOptions);
        }

        var existingValues = await _attributeValueRepository.Table
            .Where(value => profileIds.Contains(value.ProfileId))
            .ToListAsync(cancellationToken);
        var valueInserts = new List<JobSupportProfileAttributeValue>();
        foreach (var parsed in parsedValues)
        {
            var profile = profiles.FirstOrDefault(item => item.CustomerId == parsed.CustomerId);
            var definition = definitions.FirstOrDefault(item => item.LegacyCustomerAttributeId == parsed.AttributeId);
            if (profile == null || definition == null)
                continue;
            var legacyValueId = ParseInt(parsed.Value);
            var option = options.FirstOrDefault(item => item.LegacyCustomerAttributeValueId == legacyValueId &&
                item.AttributeDefinitionId == definition.Id);
            if (existingValues.Any(value => value.ProfileId == profile.Id && value.AttributeDefinitionId == definition.Id &&
                value.LegacyCustomerAttributeValueId == (option == null ? null : legacyValueId) && value.Value == (option == null ? parsed.Value : null)))
                continue;
            valueInserts.Add(new JobSupportProfileAttributeValue
            {
                ProfileId = profile.Id,
                AttributeDefinitionId = definition.Id,
                AttributeOptionId = option?.Id,
                Value = option == null ? parsed.Value : null,
                LegacyCustomerAttributeId = parsed.AttributeId,
                LegacyCustomerAttributeValueId = option == null ? null : legacyValueId,
                CreatedOnUtc = now,
                UpdatedOnUtc = now
            });
        }

        var errors = new List<string>();
        try
        {
            if (skillInserts.Count > 0)
                await _skillRepository.InsertAsync(skillInserts, false);
            if (valueInserts.Count > 0)
                await _attributeValueRepository.InsertAsync(valueInserts, false);
            await AdvanceAsync(checkpoint, profiles[^1].Id, profiles.Count, 0, 0, errors, "Running");
        }
        catch (Exception exception)
        {
            errors.Add(SanitizeException(exception, profiles[0].Id, profiles[^1].Id));
            await AdvanceAsync(checkpoint, profiles[^1].Id, 0, 0, profiles.Count, errors, "Failed");
        }
        return Result(checkpoint, errors);
    }

    public async Task<BackfillStepResult> BackfillRelationshipsAsync(int batchSize, CancellationToken cancellationToken)
    {
        var checkpoint = await GetCheckpointAsync(RELATIONSHIPS, cancellationToken);
        var batch = await _shoppingCartItemRepository.Table
            .Where(item => item.Id > checkpoint.LastProcessedId && item.ShoppingCartTypeId >= 2 && item.ShoppingCartTypeId <= 13)
            .OrderBy(item => item.Id)
            .Take(NormalizeBatchSize(batchSize))
            .ToListAsync(cancellationToken);
        if (batch.Count == 0)
            return await CompleteAsync(checkpoint);

        var relationshipBatch = batch.Where(item => item.ShoppingCartTypeId <= 11).ToList();
        var productIds = relationshipBatch.Select(item => item.ProductId).Distinct().ToArray();
        var customerIds = relationshipBatch.Select(item => item.CustomerId).Distinct().ToArray();
        var products = await _productRepository.Table.Where(product => productIds.Contains(product.Id)).ToListAsync(cancellationToken);
        var counterpartProductIds = await _productRepository.Table
            .Where(product => customerIds.Contains(product.VendorId) && !product.Deleted)
            .Select(product => product.Id)
            .ToArrayAsync(cancellationToken);
        var relatedRows = await _shoppingCartItemRepository.Table
            .Where(item => item.ShoppingCartTypeId >= 2 && item.ShoppingCartTypeId <= 11 &&
                (customerIds.Contains(item.CustomerId) || productIds.Contains(item.ProductId) || counterpartProductIds.Contains(item.ProductId)))
            .ToListAsync(cancellationToken);
        var relatedProductIds = relatedRows.Select(item => item.ProductId).Distinct().ToArray();
        products = await _productRepository.Table.Where(product => relatedProductIds.Contains(product.Id)).ToListAsync(cancellationToken);

        var normalized = relatedRows.Select(row => NormalizeRelationship(row, products)).Where(item => item != null).ToList();
        var batchIds = relationshipBatch.Select(item => item.Id).ToHashSet();
        var groups = normalized.GroupBy(item => new { item.SourceCustomerId, item.TargetCustomerId, item.RelationshipTypeId })
            .Where(group => group.Any(item => batchIds.Contains(item.LegacyId))).ToList();
        var involvedCustomerIds = groups.SelectMany(group => new[] { group.Key.SourceCustomerId, group.Key.TargetCustomerId }).Distinct().ToArray();
        var profiles = await _profileRepository.Table.Where(profile => involvedCustomerIds.Contains(profile.CustomerId)).ToListAsync(cancellationToken);
        var existing = await _relationshipRepository.Table
            .Where(relationship => involvedCustomerIds.Contains(relationship.SourceCustomerId) || involvedCustomerIds.Contains(relationship.TargetCustomerId))
            .ToListAsync(cancellationToken);
        var inserts = new List<JobSupportRelationship>();
        var updates = new List<JobSupportRelationship>();
        var skipped = batch.Count - relationshipBatch.Count;
        var errors = new List<string>();
        foreach (var group in groups)
        {
            var statuses = group.Select(item => item.Status).ToHashSet();
            if (statuses.Contains(RelationshipStatus.Accepted) && statuses.Contains(RelationshipStatus.Declined))
            {
                skipped++;
                errors.Add($"relationship-contradiction:{group.Min(item => item.LegacyId)}");
                continue;
            }
            var sourceProfile = profiles.FirstOrDefault(profile => profile.CustomerId == group.Key.SourceCustomerId);
            var targetProfile = profiles.FirstOrDefault(profile => profile.CustomerId == group.Key.TargetCustomerId);
            if (sourceProfile == null || targetProfile == null)
            {
                skipped++;
                errors.Add($"relationship-profile-missing:{group.Min(item => item.LegacyId)}");
                continue;
            }
            var status = ResolveStatus(statuses);
            var entity = existing.FirstOrDefault(item => item.SourceCustomerId == group.Key.SourceCustomerId &&
                item.TargetCustomerId == group.Key.TargetCustomerId && item.RelationshipTypeId == group.Key.RelationshipTypeId) ?? new JobSupportRelationship();
            entity.SourceCustomerId = group.Key.SourceCustomerId;
            entity.TargetCustomerId = group.Key.TargetCustomerId;
            entity.SourceProfileId = sourceProfile.Id;
            entity.TargetProfileId = targetProfile.Id;
            entity.RelationshipTypeId = group.Key.RelationshipTypeId;
            entity.StatusId = (int)status;
            entity.CreatedOnUtc = group.Min(item => item.CreatedOnUtc);
            entity.UpdatedOnUtc = group.Max(item => item.UpdatedOnUtc);
            entity.RespondedOnUtc = status is RelationshipStatus.Accepted or RelationshipStatus.Declined
                ? group.Where(item => item.Status == status).Max(item => (DateTime?)item.UpdatedOnUtc)
                : null;
            entity.LegacyShoppingCartItemId = group.Min(item => item.LegacyId);
            entity.MetadataJson = null;
            if (entity.Id == 0) inserts.Add(entity); else updates.Add(entity);
        }

        await PersistAsync(inserts, updates, _relationshipRepository, checkpoint, batch[^1].Id,
            groups.Count - skipped + (batch.Count - relationshipBatch.Count), skipped, errors);
        return Result(checkpoint, errors);
    }

    public async Task<BackfillStepResult> BackfillViewsAndRevealsAsync(int batchSize, CancellationToken cancellationToken)
    {
        var checkpoint = await GetCheckpointAsync(VIEWS_REVEALS, cancellationToken);
        return checkpoint.LastProcessedId >= 0
            ? await BackfillViewsAsync(checkpoint, batchSize, cancellationToken)
            : await BackfillRevealsAsync(checkpoint, batchSize, cancellationToken);
    }

    public async Task<BackfillStepResult> BackfillSubscriptionsAsync(int batchSize, CancellationToken cancellationToken)
    {
        var checkpoint = await GetCheckpointAsync(SUBSCRIPTIONS, cancellationToken);
        var plans = GetSubscriptionPlans();
        if (plans.Count == 0)
            return await CompleteAsync(checkpoint);
        var productIds = plans.Keys.ToArray();
        var items = await _orderItemRepository.Table
            .Where(item => item.Id > checkpoint.LastProcessedId && productIds.Contains(item.ProductId))
            .OrderBy(item => item.Id)
            .Take(NormalizeBatchSize(batchSize))
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
            return await CompleteAsync(checkpoint);

        var orderIds = items.Select(item => item.OrderId).Distinct().ToArray();
        var orders = await _orderRepository.Table.Where(order => orderIds.Contains(order.Id)).ToListAsync(cancellationToken);
        var customerIds = orders.Select(order => order.CustomerId).Distinct().ToArray();
        var attributes = await _genericAttributeRepository.Table
            .Where(attribute => customerIds.Contains(attribute.EntityId) && attribute.KeyGroup == nameof(Customer) &&
                (attribute.Key == JobSupportDefaults.SubscriptionOrderIdAttribute ||
                 attribute.Key == JobSupportDefaults.SubscriptionAllottedCountAttribute ||
                 attribute.Key == JobSupportDefaults.SubscriptionUsedCreditCountAttribute ||
                 attribute.Key == JobSupportDefaults.SubscriptionExpiryDateAttribute))
            .OrderBy(attribute => attribute.Id)
            .ToListAsync(cancellationToken);
        var existing = await _subscriptionRepository.Table.Where(subscription => orderIds.Contains(subscription.OrderId)).ToListAsync(cancellationToken);
        var inserts = new List<JobSupportSubscription>();
        var updates = new List<JobSupportSubscription>();
        var skipped = 0L;
        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            var order = orders.FirstOrDefault(candidate => candidate.Id == item.OrderId);
            if (order == null || order.Deleted || order.PaidDateUtc == null)
            {
                skipped++;
                continue;
            }
            var plan = plans[item.ProductId];
            var recordedOrderId = ParseInt(LatestAttribute(attributes, order.CustomerId, JobSupportDefaults.SubscriptionOrderIdAttribute));
            var legacyAllotted = ParseInt(LatestAttribute(attributes, order.CustomerId, JobSupportDefaults.SubscriptionAllottedCountAttribute));
            var legacyUsed = ParseInt(LatestAttribute(attributes, order.CustomerId, JobSupportDefaults.SubscriptionUsedCreditCountAttribute));
            var isCurrentGrant = recordedOrderId == order.Id;
            var carried = isCurrentGrant ? Math.Max(0, legacyAllotted - plan.Credits) : 0;
            var end = isCurrentGrant && DateTime.TryParse(LatestAttribute(attributes, order.CustomerId, JobSupportDefaults.SubscriptionExpiryDateAttribute),
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var legacyEnd)
                ? legacyEnd
                : order.CreatedOnUtc.AddMonths(plan.Months);
            var entity = existing.FirstOrDefault(subscription => subscription.OrderId == order.Id && subscription.OrderItemId == item.Id)
                         ?? new JobSupportSubscription();
            entity.CustomerId = order.CustomerId;
            entity.OrderId = order.Id;
            entity.OrderItemId = item.Id;
            entity.ProductId = item.ProductId;
            entity.Status = (int)(end <= now ? SubscriptionStatus.Expired :
                isCurrentGrant && legacyUsed >= plan.Credits + carried ? SubscriptionStatus.Exhausted : SubscriptionStatus.Active);
            entity.StartOnUtc = order.CreatedOnUtc;
            entity.EndOnUtc = end;
            entity.AllottedCredits = plan.Credits;
            entity.CarriedCredits = carried;
            entity.UsedCredits = isCurrentGrant ? Math.Max(0, legacyUsed) : 0;
            entity.CreatedOnUtc = order.CreatedOnUtc;
            entity.UpdatedOnUtc = now;
            if (entity.Id == 0) inserts.Add(entity); else updates.Add(entity);
        }

        var errors = new List<string>();
        await PersistAsync(inserts, updates, _subscriptionRepository, checkpoint, items[^1].Id, items.Count - skipped, skipped, errors);
        return Result(checkpoint, errors);
    }

    private async Task<BackfillStepResult> BackfillViewsAsync(JobSupportMigrationCheckpoint checkpoint, int batchSize, CancellationToken cancellationToken)
    {
        var batch = await _shoppingCartItemRepository.Table
            .Where(item => item.Id > checkpoint.LastProcessedId && item.ShoppingCartTypeId >= 12 && item.ShoppingCartTypeId <= 13)
            .OrderBy(item => item.Id)
            .Take(NormalizeBatchSize(batchSize))
            .ToListAsync(cancellationToken);
        if (batch.Count == 0)
        {
            checkpoint.LastProcessedId = -1;
            checkpoint.Status = "Running";
            checkpoint.UpdatedOnUtc = DateTime.UtcNow;
            await _checkpointRepository.UpdateAsync(checkpoint, false);
            return Result(checkpoint, Array.Empty<string>());
        }
        var productIds = batch.Select(item => item.ProductId).Distinct().ToArray();
        var products = await _productRepository.Table.Where(product => productIds.Contains(product.Id)).ToListAsync(cancellationToken);
        var normalized = batch.Select(row => NormalizeView(row, products)).Where(item => item != null).ToList();
        var customerIds = normalized.SelectMany(item => new[] { item.ViewerCustomerId, item.ViewedCustomerId }).Distinct().ToArray();
        var profiles = await _profileRepository.Table.Where(profile => customerIds.Contains(profile.CustomerId)).ToListAsync(cancellationToken);
        var existing = await _profileViewRepository.Table.Where(view => customerIds.Contains(view.ViewerCustomerId)).ToListAsync(cancellationToken);
        var inserts = new List<JobSupportProfileView>();
        var updates = new List<JobSupportProfileView>();
        var skipped = 0L;
        var errors = new List<string>();
        foreach (var group in normalized.GroupBy(item => new { item.ViewerCustomerId, item.ViewedCustomerId }))
        {
            var viewerProfile = profiles.FirstOrDefault(profile => profile.CustomerId == group.Key.ViewerCustomerId);
            var viewedProfile = profiles.FirstOrDefault(profile => profile.CustomerId == group.Key.ViewedCustomerId);
            if (viewerProfile == null || viewedProfile == null)
            {
                skipped++;
                errors.Add($"view-profile-missing:{group.Min(item => item.LegacyId)}");
                continue;
            }
            var entity = existing.FirstOrDefault(view => view.ViewerCustomerId == group.Key.ViewerCustomerId && view.ViewedProfileId == viewedProfile.Id)
                         ?? new JobSupportProfileView();
            entity.ViewerCustomerId = group.Key.ViewerCustomerId;
            entity.ViewedCustomerId = group.Key.ViewedCustomerId;
            entity.ViewerProfileId = viewerProfile.Id;
            entity.ViewedProfileId = viewedProfile.Id;
            entity.FirstViewedOnUtc = group.Min(item => item.CreatedOnUtc);
            entity.LastViewedOnUtc = group.Max(item => item.UpdatedOnUtc);
            entity.ViewCount = Math.Max(entity.ViewCount, group.Count());
            entity.LegacyShoppingCartItemId = group.Min(item => item.LegacyId);
            if (entity.Id == 0) inserts.Add(entity); else updates.Add(entity);
        }
        await PersistAsync(inserts, updates, _profileViewRepository, checkpoint, batch[^1].Id, batch.Count - skipped, skipped, errors);
        return Result(checkpoint, errors);
    }

    private async Task<BackfillStepResult> BackfillRevealsAsync(JobSupportMigrationCheckpoint checkpoint, int batchSize, CancellationToken cancellationToken)
    {
        var cursor = -checkpoint.LastProcessedId - 1;
        var batch = await _genericAttributeRepository.Table
            .Where(attribute => attribute.Id > cursor && attribute.KeyGroup == nameof(Customer) &&
                attribute.Key == JobSupportDefaults.RevealedProfileIdsAttribute)
            .OrderBy(attribute => attribute.Id)
            .Take(NormalizeBatchSize(batchSize))
            .ToListAsync(cancellationToken);
        if (batch.Count == 0)
            return await CompleteAsync(checkpoint);

        var parsed = batch.SelectMany(attribute => ParseIdentifiers(attribute.Value)
            .Select(productId => new RevealProjection(attribute.Id, attribute.EntityId, productId,
                attribute.CreatedOrUpdatedDateUTC ?? DateTime.UtcNow))).ToList();
        var productIds = parsed.Select(item => item.LegacyProductId).Distinct().ToArray();
        var products = await _productRepository.Table.Where(product => productIds.Contains(product.Id)).ToListAsync(cancellationToken);
        var targetCustomerIds = products.Select(product => product.VendorId).Distinct().ToArray();
        var profiles = await _profileRepository.Table.Where(profile => targetCustomerIds.Contains(profile.CustomerId)).ToListAsync(cancellationToken);
        var viewerIds = batch.Select(attribute => attribute.EntityId).Distinct().ToArray();
        var subscriptions = await _subscriptionRepository.Table.Where(subscription => viewerIds.Contains(subscription.CustomerId)).ToListAsync(cancellationToken);
        var existing = await _contactRevealRepository.Table.Where(reveal => viewerIds.Contains(reveal.ViewerCustomerId)).ToListAsync(cancellationToken);
        var inserts = new List<JobSupportContactReveal>();
        var skipped = 0L;
        var errors = new List<string>();
        foreach (var item in parsed)
        {
            var product = products.FirstOrDefault(candidate => candidate.Id == item.LegacyProductId);
            var profile = product == null ? null : profiles.FirstOrDefault(candidate => candidate.CustomerId == product.VendorId);
            if (profile == null || existing.Any(reveal => reveal.ViewerCustomerId == item.ViewerCustomerId && reveal.TargetProfileId == profile.Id))
            {
                skipped++;
                continue;
            }
            var subscription = subscriptions.Where(candidate => candidate.CustomerId == item.ViewerCustomerId && candidate.StartOnUtc <= item.RevealedOnUtc)
                .OrderByDescending(candidate => candidate.StartOnUtc).FirstOrDefault();
            inserts.Add(new JobSupportContactReveal
            {
                SubscriptionId = subscription?.Id,
                ViewerCustomerId = item.ViewerCustomerId,
                TargetCustomerId = profile.CustomerId,
                TargetProfileId = profile.Id,
                CreditCost = 1,
                RevealedOnUtc = item.RevealedOnUtc,
                LegacyGenericAttributeId = item.LegacyGenericAttributeId
            });
        }
        try
        {
            if (inserts.Count > 0)
                await _contactRevealRepository.InsertAsync(inserts, false);
            checkpoint.LastProcessedId = -(batch[^1].Id + 1);
            await AdvanceAsync(checkpoint, checkpoint.LastProcessedId, parsed.Count - skipped, skipped, 0, errors, "Running");
        }
        catch (Exception exception)
        {
            errors.Add(SanitizeException(exception, batch[0].Id, batch[^1].Id));
            checkpoint.LastProcessedId = -(batch[^1].Id + 1);
            await AdvanceAsync(checkpoint, checkpoint.LastProcessedId, 0, skipped, parsed.Count - skipped, errors, "Failed");
        }
        return Result(checkpoint, errors);
    }

    private async Task<JobSupportMigrationCheckpoint> GetCheckpointAsync(string name, CancellationToken cancellationToken)
    {
        var checkpoint = await _checkpointRepository.Table.FirstOrDefaultAsync(item => item.MigrationName == name, cancellationToken);
        if (checkpoint != null)
            return checkpoint;
        checkpoint = new JobSupportMigrationCheckpoint
        {
            MigrationName = name,
            Status = "NotStarted",
            UpdatedOnUtc = DateTime.UtcNow
        };
        await _checkpointRepository.InsertAsync(checkpoint, false);
        return checkpoint;
    }

    private async Task<BackfillStepResult> CompleteAsync(JobSupportMigrationCheckpoint checkpoint)
    {
        checkpoint.Status = "Completed";
        checkpoint.LastExecutedOnUtc = DateTime.UtcNow;
        checkpoint.UpdatedOnUtc = checkpoint.LastExecutedOnUtc.Value;
        await _checkpointRepository.UpdateAsync(checkpoint, false);
        return Result(checkpoint, Array.Empty<string>());
    }

    private async Task PersistAsync<TEntity>(IList<TEntity> inserts, IList<TEntity> updates, IRepository<TEntity> repository,
        JobSupportMigrationCheckpoint checkpoint, int lastId, long processed, long skipped, IList<string> errors)
        where TEntity : Nop.Core.BaseEntity
    {
        try
        {
            if (inserts.Count > 0)
                await repository.InsertAsync(inserts, false);
            if (updates.Count > 0)
                await repository.UpdateAsync(updates, false);
            await AdvanceAsync(checkpoint, lastId, processed, skipped, 0, errors, "Running");
        }
        catch (Exception exception)
        {
            errors.Add(SanitizeException(exception, checkpoint.LastProcessedId + 1, lastId));
            await AdvanceAsync(checkpoint, lastId, 0, skipped, processed, errors, "Failed");
        }
    }

    private async Task AdvanceAsync(JobSupportMigrationCheckpoint checkpoint, int lastId, long processed, long skipped,
        long failed, IEnumerable<string> errors, string status)
    {
        checkpoint.LastProcessedId = lastId;
        checkpoint.ProcessedCount += processed;
        checkpoint.SkippedCount += skipped;
        checkpoint.FailedCount += failed;
        checkpoint.Status = status;
        checkpoint.ErrorLog = AppendErrors(checkpoint.ErrorLog, errors);
        checkpoint.LastExecutedOnUtc = DateTime.UtcNow;
        checkpoint.UpdatedOnUtc = checkpoint.LastExecutedOnUtc.Value;
        await _checkpointRepository.UpdateAsync(checkpoint, false);
    }

    private static BackfillStepResult Result(JobSupportMigrationCheckpoint checkpoint, IEnumerable<string> errors) => new()
    {
        MigrationName = checkpoint.MigrationName,
        LastProcessedId = checkpoint.LastProcessedId,
        ProcessedCount = checkpoint.ProcessedCount,
        SkippedCount = checkpoint.SkippedCount,
        FailedCount = checkpoint.FailedCount,
        Completed = checkpoint.Status == "Completed",
        ErrorLog = errors.Take(20).ToArray()
    };

    private static int NormalizeBatchSize(int batchSize) => Math.Clamp(batchSize, 1, 5000);
    private static int? PositiveOrNull(int value) => value > 0 ? value : null;
    private static int ParseInt(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static string FirstOption(IEnumerable<SpecificationProjection> values, int productId, int attributeId) =>
        values.Where(value => value.ProductId == productId && value.AttributeId == attributeId).OrderBy(value => value.DisplayOrder)
            .Select(value => value.Name).FirstOrDefault();
    private static string LatestAttribute(IEnumerable<GenericAttribute> attributes, int entityId, string key) =>
        attributes.Where(attribute => attribute.EntityId == entityId && attribute.Key == key).OrderByDescending(attribute => attribute.Id)
            .Select(attribute => attribute.Value).FirstOrDefault();
    private static bool IsContactField(string name) =>
        (name ?? string.Empty).Contains("email", StringComparison.OrdinalIgnoreCase) ||
        (name ?? string.Empty).Contains("phone", StringComparison.OrdinalIgnoreCase) ||
        (name ?? string.Empty).Contains("mobile", StringComparison.OrdinalIgnoreCase);
    private static bool LooksLikeContactValue(string value) => EmailPattern.IsMatch(value ?? string.Empty) || PhonePattern.IsMatch(value ?? string.Empty);
    private static IEnumerable<(int AttributeId, string Value)> ParseCustomerAttributes(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            yield break;
        XDocument document;
        try { document = XDocument.Parse(xml); }
        catch { yield break; }
        foreach (var element in document.Descendants("CustomerAttribute"))
        {
            if (!int.TryParse(element.Attribute("ID")?.Value, out var attributeId))
                continue;
            foreach (var value in element.Descendants("Value").Select(node => node.Value.Trim()).Where(value => value.Length > 0))
                yield return (attributeId, value);
        }
    }
    private static HashSet<int> ParseIdentifiers(string value) => (value ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(ParseInt).Where(id => id > 0).ToHashSet();
    private static string AppendErrors(string current, IEnumerable<string> errors)
    {
        var lines = (current ?? string.Empty).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Concat(errors.Where(error => !string.IsNullOrWhiteSpace(error))).TakeLast(100);
        return string.Join(Environment.NewLine, lines);
    }
    private static string SanitizeException(Exception exception, int firstId, int lastId) =>
        $"batch-failed:{firstId}-{lastId}:{exception.GetType().Name}";

    private static NormalizedRelationship NormalizeRelationship(ShoppingCartItem row, IList<Product> products)
    {
        var targetCustomerId = products.FirstOrDefault(product => product.Id == row.ProductId)?.VendorId ?? 0;
        if (targetCustomerId <= 0 || targetCustomerId == row.CustomerId)
            return null;
        var inverse = row.ShoppingCartTypeId is 3 or 5 or 6 or 8 or 11;
        var source = inverse ? targetCustomerId : row.CustomerId;
        var target = inverse ? row.CustomerId : targetCustomerId;
        var type = row.ShoppingCartTypeId switch
        {
            2 or 3 => 1,
            4 or 5 or 6 or 7 or 8 or 9 => 2,
            10 or 11 => 3,
            _ => 0
        };
        var status = row.ShoppingCartTypeId switch
        {
            2 or 3 => RelationshipStatus.Active,
            4 or 5 => RelationshipStatus.Pending,
            6 or 7 => RelationshipStatus.Accepted,
            8 or 9 => RelationshipStatus.Declined,
            10 or 11 => RelationshipStatus.Blocked,
            _ => RelationshipStatus.Cancelled
        };
        return type == 0 ? null : new NormalizedRelationship(source, target, type, status, row.Id, row.CreatedOnUtc, row.UpdatedOnUtc);
    }
    private static RelationshipStatus ResolveStatus(ISet<RelationshipStatus> statuses)
    {
        if (statuses.Contains(RelationshipStatus.Blocked)) return RelationshipStatus.Blocked;
        if (statuses.Contains(RelationshipStatus.Accepted)) return RelationshipStatus.Accepted;
        if (statuses.Contains(RelationshipStatus.Declined)) return RelationshipStatus.Declined;
        if (statuses.Contains(RelationshipStatus.Pending)) return RelationshipStatus.Pending;
        return RelationshipStatus.Active;
    }
    private static NormalizedView NormalizeView(ShoppingCartItem row, IList<Product> products)
    {
        var productCustomerId = products.FirstOrDefault(product => product.Id == row.ProductId)?.VendorId ?? 0;
        if (productCustomerId <= 0 || productCustomerId == row.CustomerId)
            return null;
        var inverse = row.ShoppingCartTypeId == 13;
        return new NormalizedView(inverse ? productCustomerId : row.CustomerId, inverse ? row.CustomerId : productCustomerId,
            row.Id, row.CreatedOnUtc, row.UpdatedOnUtc);
    }

    private Dictionary<int, SubscriptionPlan> GetSubscriptionPlans() => new[]
        {
            new SubscriptionPlan(_settings.ThreeMonthSubscriptionProductId, 3, _shoppingCartSettings.ThreeMonthSubscriptionAllottedCount),
            new SubscriptionPlan(_settings.SixMonthSubscriptionProductId, 6, _shoppingCartSettings.SixMonthSubscriptionAllottedCount),
            new SubscriptionPlan(_settings.OneYearSubscriptionProductId, 12, _shoppingCartSettings.OneYearSubscriptionAllottedCount)
        }.Where(plan => plan.ProductId > 0).GroupBy(plan => plan.ProductId).ToDictionary(group => group.Key, group => group.First());

    private sealed record SpecificationProjection(int ProductId, int AttributeId, string Name, int DisplayOrder);
    private sealed record SkillProjection(int ProductId, int AttributeId, int OptionId, string Name, int DisplayOrder);
    private sealed record ParsedAttributeValue(int CustomerId, int AttributeId, string Value);
    private sealed record NormalizedRelationship(int SourceCustomerId, int TargetCustomerId, int RelationshipTypeId,
        RelationshipStatus Status, int LegacyId, DateTime CreatedOnUtc, DateTime UpdatedOnUtc);
    private sealed record NormalizedView(int ViewerCustomerId, int ViewedCustomerId, int LegacyId, DateTime CreatedOnUtc, DateTime UpdatedOnUtc);
    private sealed record RevealProjection(int LegacyGenericAttributeId, int ViewerCustomerId, int LegacyProductId, DateTime RevealedOnUtc);
    private sealed record SubscriptionPlan(int ProductId, int Months, int Credits);
}
