using Microsoft.AspNetCore.Mvc;
using LinqToDB;
using Nop.Core;
using Nop.Data;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Domain;
using Nop.Plugin.Misc.JobSupport.Factories;
using Nop.Plugin.Misc.JobSupport.Models.Public;
using Nop.Plugin.Misc.JobSupport.Services;
using Nop.Services.Customers;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.JobSupport.Controllers;

public class JobSupportProfileController : BasePluginController
{
    private const string LIST_VIEW = "~/Plugins/Misc.JobSupport/Views/Profile/List.cshtml";
    private const string DETAIL_VIEW = "~/Plugins/Misc.JobSupport/Views/Profile/Detail.cshtml";
    private readonly ICustomerService _customerService;
    private readonly IRepository<JobSupportProfile> _profileRepository;
    private readonly IRepository<JobSupportProfileAttributeOption> _attributeOptionRepository;
    private readonly IJobSupportProfileModelFactory _modelFactory;
    private readonly IJobSupportProfileQueryService _queryService;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;
    private readonly JobSupportSettings _settings;

    public JobSupportProfileController(ICustomerService customerService,
        IRepository<JobSupportProfile> profileRepository,
        IRepository<JobSupportProfileAttributeOption> attributeOptionRepository,
        IJobSupportProfileModelFactory modelFactory,
        IJobSupportProfileQueryService queryService,
        IStoreContext storeContext,
        IWorkContext workContext,
        JobSupportSettings settings)
    {
        _customerService = customerService;
        _profileRepository = profileRepository;
        _attributeOptionRepository = attributeOptionRepository;
        _modelFactory = modelFactory;
        _queryService = queryService;
        _storeContext = storeContext;
        _workContext = workContext;
        _settings = settings;
    }

    public async Task<IActionResult> List(ProfileFilterModel filter)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var isGuest = await _customerService.IsGuestAsync(customer);
        if (isGuest && !_settings.AllowGuestProfileBrowsing)
            return Challenge();

        filter.PageNumber = Math.Max(1, filter.PageNumber);
        var request = new ProfileSearchRequest
        {
            CustomerId = customer.Id,
            StoreId = (await _storeContext.GetCurrentStoreAsync()).Id,
            ProfileTypeId = filter.ProfileTypeId ?? await GetOppositeProfileTypeIdAsync(customer.Id),
            PrimarySkillIds = filter.PrimaryTechnologyId.HasValue ? new[] { filter.PrimaryTechnologyId.Value } : Array.Empty<int>(),
            SecondarySkillIds = filter.SecondaryTechnologyId.HasValue ? new[] { filter.SecondaryTechnologyId.Value } : Array.Empty<int>(),
            Availability = filter.AvailabilityId.HasValue
                ? (await _attributeOptionRepository.Table.FirstOrDefaultAsync(option =>
                    option.Id == filter.AvailabilityId.Value ||
                    option.LegacyCustomerAttributeValueId == filter.AvailabilityId.Value ||
                    option.LegacyOptionId == filter.AvailabilityId.Value))?.Name
                : null,
            PageIndex = filter.PageNumber - 1,
            PageSize = Math.Max(1, _settings.DefaultPageSize),
            SortOrder = filter.SortOrder
        };
        var result = await _queryService.SearchProfilesAsync(request);
        return View(LIST_VIEW, await _modelFactory.PrepareProfileListAsync(filter, result, customer, isGuest));
    }

    public async Task<IActionResult> Detail(string slug)
    {
        var profile = await _profileRepository.Table.FirstOrDefaultAsync(item => item.Slug == slug && item.IsPublished);
        if (profile == null)
            return NotFound();
        var customer = await _workContext.GetCurrentCustomerAsync();
        var isGuest = await _customerService.IsGuestAsync(customer);
        if (isGuest && !_settings.AllowGuestProfileBrowsing)
            return Challenge();

        var result = await _queryService.SearchProfilesAsync(new ProfileSearchRequest
        {
            ProfileIds = new[] { profile.Id },
            CustomerId = customer.Id,
            StoreId = (await _storeContext.GetCurrentStoreAsync()).Id,
            ExcludeOwnProfile = false,
            PageIndex = 0,
            PageSize = 1
        });
        var row = result.Items.FirstOrDefault(item => item.Id == profile.Id);
        if (row == null)
            return NotFound();
        return View(DETAIL_VIEW, await _modelFactory.PrepareProfileDetailAsync(profile, row, customer, isGuest,
            $"{Request.PathBase}{Request.Path}{Request.QueryString}"));
    }

    private async Task<int?> GetOppositeProfileTypeIdAsync(int customerId)
    {
        var currentProfileType = await _profileRepository.Table
            .Where(profile => profile.CustomerId == customerId && profile.IsPublished)
            .Select(profile => profile.ProfileType)
            .FirstOrDefaultAsync();
        if (currentProfileType <= 0)
            return null;
        var oppositeProfileType = await _profileRepository.Table
            .Where(profile => profile.IsPublished && profile.ProfileType > 0 && profile.ProfileType != currentProfileType)
            .Select(profile => profile.ProfileType)
            .Distinct()
            .OrderBy(profileType => profileType)
            .FirstOrDefaultAsync();
        return oppositeProfileType > 0 ? oppositeProfileType : null;
    }
}
