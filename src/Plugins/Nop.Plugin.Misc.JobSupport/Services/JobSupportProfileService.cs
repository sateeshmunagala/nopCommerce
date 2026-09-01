using LinqToDB;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Domain;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportProfileService : IJobSupportProfileService
{
    private readonly IJobSupportAffiliateService _affiliateService;
    private readonly ICustomerActivityService _customerActivityService;
    private readonly ICustomerService _customerService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILogger _logger;
    private readonly IRepository<JobSupportProfile> _profileRepository;

    public JobSupportProfileService(IJobSupportAffiliateService affiliateService,
        ICustomerActivityService customerActivityService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        ILogger logger,
        IRepository<JobSupportProfile> profileRepository)
    {
        _affiliateService = affiliateService;
        _customerActivityService = customerActivityService;
        _customerService = customerService;
        _genericAttributeService = genericAttributeService;
        _logger = logger;
        _profileRepository = profileRepository;
    }

    public async Task EnsureProfileForCustomerAsync(Customer customer, JobSupportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.ExecutionMode == WorkflowExecutionMode.Shadow)
        {
            await _logger.InformationAsync($"JobSupport shadow plugin profile outcome: customer {customer.Id}.");
            return;
        }

        if (settings.ExecutionMode != WorkflowExecutionMode.Live)
            return;

        var profile = await _profileRepository.Table.FirstOrDefaultAsync(item => item.CustomerId == customer.Id);
        var isNew = profile == null;
        profile ??= new JobSupportProfile
        {
            CustomerId = customer.Id,
            Slug = $"profile-{customer.Id}",
            CreatedOnUtc = customer.CreatedOnUtc,
            MigrationSource = "PluginWrite"
        };

        profile.DisplayName = GetDisplayName(customer);
        profile.AvatarPictureId = PositiveOrNull(await _genericAttributeService.GetAttributeAsync<int>(customer,
            NopCustomerDefaults.AvatarPictureIdAttribute));
        profile.CountryId = PositiveOrNull(customer.CountryId);
        profile.StateProvinceId = PositiveOrNull(customer.StateProvinceId);
        profile.City = customer.City;
        profile.UpdatedOnUtc = DateTime.UtcNow;

        if (isNew)
            await _profileRepository.InsertAsync(profile, false);
        else
            await _profileRepository.UpdateAsync(profile, false);

        await _affiliateService.EnsureAffiliateAsync(customer, settings.ExecutionMode);
        await _customerActivityService.InsertActivityAsync(customer,
            JobSupportDefaults.ActivityTypeSystemName,
            $"JobSupport profile workflow applied for profile {profile.Id}.",
            profile);
    }

    public async Task ActivateProfileAsync(Customer customer, JobSupportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(settings);

        var profile = await _profileRepository.Table.FirstOrDefaultAsync(item => item.CustomerId == customer.Id);
        if (profile == null || !customer.Active || customer.Deleted)
            return;

        if (settings.ExecutionMode == WorkflowExecutionMode.Shadow)
        {
            await _logger.InformationAsync($"JobSupport shadow activation outcome: publish plugin profile {profile.Id}.");
            return;
        }

        if (settings.ExecutionMode != WorkflowExecutionMode.Live || profile.IsPublished)
            return;

        profile.IsPublished = true;
        profile.UpdatedOnUtc = DateTime.UtcNow;
        await _profileRepository.UpdateAsync(profile, false);
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

        var profile = await _profileRepository.Table.FirstOrDefaultAsync(item => item.CustomerId == customerId);
        if (profile == null)
        {
            result.ErrorCode = "ProfileNotFound";
            return result;
        }

        var normalized = availability?.Trim() ?? string.Empty;
        if (string.Equals(profile.CurrentAvailability, normalized, StringComparison.OrdinalIgnoreCase))
        {
            result.Succeeded = true;
            result.AlreadyApplied = true;
            return result;
        }

        if (mode == WorkflowExecutionMode.Shadow)
        {
            result.Succeeded = true;
            return result;
        }

        if (mode != WorkflowExecutionMode.Live)
        {
            result.ErrorCode = "WorkflowDisabled";
            return result;
        }

        profile.CurrentAvailability = normalized;
        profile.UpdatedOnUtc = DateTime.UtcNow;
        await _profileRepository.UpdateAsync(profile, false);
        result.Succeeded = true;
        return result;
    }

    public async Task SynchronizeAvatarAsync(GenericAttribute attribute, WorkflowExecutionMode mode)
    {
        if (!string.Equals(attribute?.Key, NopCustomerDefaults.AvatarPictureIdAttribute, StringComparison.Ordinal) ||
            !string.Equals(attribute.KeyGroup, nameof(Customer), StringComparison.Ordinal) ||
            !int.TryParse(attribute.Value, out var pictureId) ||
            pictureId <= 0)
            return;

        var profile = await _profileRepository.Table.FirstOrDefaultAsync(item => item.CustomerId == attribute.EntityId);
        if (profile == null || profile.AvatarPictureId == pictureId || mode != WorkflowExecutionMode.Live)
            return;

        profile.AvatarPictureId = pictureId;
        profile.UpdatedOnUtc = DateTime.UtcNow;
        await _profileRepository.UpdateAsync(profile, false);
    }

    public async Task UpdatePluginProfileContentAsync(int customerId,
        string shortDescription,
        string fullDescription)
    {
        var profile = await _profileRepository.Table.FirstOrDefaultAsync(item => item.CustomerId == customerId);
        if (profile == null)
            return;

        var normalizedShort = shortDescription ?? string.Empty;
        var normalizedFull = fullDescription ?? string.Empty;
        if (profile.ShortDescription == normalizedShort && profile.FullDescription == normalizedFull)
            return;

        profile.ShortDescription = normalizedShort;
        profile.FullDescription = normalizedFull;
        profile.UpdatedOnUtc = DateTime.UtcNow;
        await _profileRepository.UpdateAsync(profile, false);
    }

    private static string GetDisplayName(Customer customer)
    {
        var displayName = $"{customer.FirstName} {customer.LastName}".Trim();
        return string.IsNullOrWhiteSpace(displayName)
            ? customer.Username ?? customer.Email ?? $"Customer {customer.Id}"
            : displayName;
    }

    private static int? PositiveOrNull(int value) => value > 0 ? value : null;
}
