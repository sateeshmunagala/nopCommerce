using Microsoft.AspNetCore.WebUtilities;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Models.Public;
using Nop.Services.Catalog;
using Nop.Services.Media;

namespace Nop.Plugin.Misc.JobSupport.Factories;

public class JobSupportProfileModelFactory : IJobSupportProfileModelFactory
{
    private readonly IPictureService _pictureService;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IWebHelper _webHelper;
    private readonly JobSupportSettings _settings;

    public JobSupportProfileModelFactory(IPictureService pictureService,
        ISpecificationAttributeService specificationAttributeService,
        IWebHelper webHelper,
        JobSupportSettings settings)
    {
        _pictureService = pictureService;
        _specificationAttributeService = specificationAttributeService;
        _webHelper = webHelper;
        _settings = settings;
    }

    public async Task PrepareFilterAsync(ProfileFilterModel filter)
    {
        await AddOptionsAsync(filter.ProfileTypes, _settings.ProfileTypeSpecificationAttributeId, filter.ProfileTypeId);
        await AddOptionsAsync(filter.PrimaryTechnologies, _settings.PrimaryTechnologySpecificationAttributeId, filter.PrimaryTechnologyId);
        await AddOptionsAsync(filter.SecondaryTechnologies, _settings.SecondaryTechnologySpecificationAttributeId, filter.SecondaryTechnologyId);
        await AddOptionsAsync(filter.Availabilities, _settings.CurrentAvailabilitySpecificationAttributeId, filter.AvailabilityId);
        await AddOptionsAsync(filter.RelevantExperiences, _settings.RelevantExperienceSpecificationAttributeId, filter.RelevantExperienceId);
        await AddOptionsAsync(filter.MotherTongues, _settings.MotherTongueSpecificationAttributeId, filter.MotherTongueId);
    }

    public async Task<ProfileListModel> PrepareProfileListAsync(ProfileFilterModel filter,
        PagedProfileSearchResult result,
        Customer currentCustomer,
        bool isGuest)
    {
        await PrepareFilterAsync(filter);
        var filtered = await ApplyConfiguredFiltersAsync(result.Items, filter);
        var model = new ProfileListModel
        {
            Filter = filter,
            QuerySucceeded = result.Succeeded,
            TotalRecords = result.TotalRecords,
            TotalPages = result.PageSize <= 0 ? 0 : (int)Math.Ceiling(result.TotalRecords / (double)result.PageSize)
        };
        foreach (var item in filtered.Where(item => item.Id > 0 && item.Id != currentCustomer?.VendorId))
            model.Profiles.Add(await PrepareProfileCardAsync(item, currentCustomer, isGuest));
        return model;
    }

    public async Task<ProfileDetailModel> PrepareProfileDetailAsync(Product profile,
        ProfileSearchResult result,
        Customer currentCustomer,
        bool isGuest,
        string returnUrl)
    {
        var card = await PrepareProfileCardAsync(result, currentCustomer, isGuest);
        var ownProfile = currentCustomer != null && profile.VendorId == currentCustomer.Id;
        return new ProfileDetailModel
        {
            Id = card.Id,
            FirstName = card.FirstName,
            AvatarUrl = card.AvatarUrl,
            City = card.City,
            Country = card.Country,
            ProfileType = card.ProfileType,
            PrimaryTechnology = card.PrimaryTechnology,
            SecondaryTechnology = card.SecondaryTechnology,
            Availability = card.Availability,
            RelevantExperience = card.RelevantExperience,
            MotherTongue = card.MotherTongue,
            Gender = card.Gender,
            ShowGender = card.ShowGender,
            IsPremium = card.IsPremium,
            IsShortlisted = card.IsShortlisted,
            InterestSent = card.InterestSent,
            CanAct = card.CanAct && !ownProfile,
            DetailUrl = card.DetailUrl,
            ShortlistUrl = card.ShortlistUrl,
            RemoveShortlistUrl = card.RemoveShortlistUrl,
            InterestUrl = card.InterestUrl,
            AcceptUrl = card.AcceptUrl,
            DeclineUrl = card.DeclineUrl,
            Description = profile.FullDescription,
            ReviewSummary = string.Empty,
            CanRevealContact = !isGuest && !ownProfile,
            IsOwnProfile = ownProfile,
            IsGuest = isGuest,
            LoginUrl = QueryHelpers.AddQueryString($"{_webHelper.GetStoreLocation()}login", "returnUrl", returnUrl),
            RevealContactUrl = ActionUrl(result.Slug, "RevealContact"),
            BlockUrl = ActionUrl(result.Slug, "Block")
        };
    }

    public async Task<ProfileCardModel> PrepareProfileCardAsync(ProfileSearchResult result,
        Customer currentCustomer,
        bool isGuest)
    {
        var pictureId = int.TryParse(result.AvatarPictureId, out var id) ? id : 0;
        var slug = Uri.EscapeDataString(result.Slug ?? string.Empty);
        var ownProfile = currentCustomer != null && result.Id == currentCustomer.VendorId;
        return new ProfileCardModel
        {
            Id = result.Id,
            FirstName = result.FirstName,
            AvatarUrl = await _pictureService.GetPictureUrlAsync(pictureId, 144),
            City = result.City,
            Country = result.Country,
            ProfileType = result.ProfileType,
            PrimaryTechnology = result.PrimaryTechnology,
            SecondaryTechnology = result.SecondaryTechnology,
            Availability = result.CurrentAvailability,
            RelevantExperience = result.WorkExperience,
            MotherTongue = result.MotherTongue,
            Gender = result.Gender,
            ShowGender = _settings.ShowGender,
            IsPremium = result.PremiumCustomer,
            IsShortlisted = result.ProfileShortListed,
            InterestSent = result.InterestSent,
            CanAct = !isGuest && !ownProfile,
            DetailUrl = $"{_webHelper.GetStoreLocation()}job-support/profile/{slug}",
            ShortlistUrl = ActionUrl(result.Slug, "Shortlist"),
            RemoveShortlistUrl = ActionUrl(result.Slug, "RemoveShortlist"),
            InterestUrl = ActionUrl(result.Slug, "Interest"),
            AcceptUrl = ActionUrl(result.Slug, "Accept"),
            DeclineUrl = ActionUrl(result.Slug, "Decline")
        };
    }

    private string ActionUrl(string slug, string action) =>
        $"{_webHelper.GetStoreLocation()}job-support/profile/{Uri.EscapeDataString(slug ?? string.Empty)}/{action}";

    private async Task AddOptionsAsync(IList<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> target,
        int attributeId,
        int? selectedId)
    {
        if (attributeId <= 0)
            return;
        foreach (var option in await _specificationAttributeService
                     .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(attributeId))
        {
            target.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(option.Name,
                option.Id.ToString(),
                option.Id == selectedId));
        }
    }

    private async Task<IList<ProfileSearchResult>> ApplyConfiguredFiltersAsync(IList<ProfileSearchResult> source,
        ProfileFilterModel filter)
    {
        var primary = await GetOptionNameAsync(filter.PrimaryTechnologyId);
        var secondary = await GetOptionNameAsync(filter.SecondaryTechnologyId);
        var availability = await GetOptionNameAsync(filter.AvailabilityId);
        var experience = await GetOptionNameAsync(filter.RelevantExperienceId);
        var language = await GetOptionNameAsync(filter.MotherTongueId);
        return source.Where(item => Contains(item.PrimaryTechnology, primary) &&
            Contains(item.SecondaryTechnology, secondary) &&
            Contains(item.CurrentAvailability, availability) &&
            Contains(item.WorkExperience, experience) &&
            Contains(item.MotherTongue, language)).ToList();
    }

    private async Task<string> GetOptionNameAsync(int? optionId) => optionId.GetValueOrDefault() <= 0
        ? null
        : (await _specificationAttributeService.GetSpecificationAttributeOptionByIdAsync(optionId.Value))?.Name;

    private static bool Contains(string value, string expected) => string.IsNullOrWhiteSpace(expected) ||
        (value?.Contains(expected, StringComparison.OrdinalIgnoreCase) ?? false);
}
