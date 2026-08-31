using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Factories;
using Nop.Plugin.Misc.JobSupport.Models.Account;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.JobSupport.Controllers;

[Authorize]
public class JobSupportAccountController : BasePluginController
{
    private const string PROFILE_VIEW = "~/Plugins/Misc.JobSupport/Views/Account/Profile.cshtml";
    private const string RELATIONSHIPS_VIEW = "~/Plugins/Misc.JobSupport/Views/Account/Relationships.cshtml";
    private const string AFFILIATIONS_VIEW = "~/Plugins/Misc.JobSupport/Views/Account/Affiliations.cshtml";
    private readonly IJobSupportAccountModelFactory _modelFactory;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IWorkContext _workContext;

    public JobSupportAccountController(IJobSupportAccountModelFactory modelFactory,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IWorkContext workContext)
    {
        _modelFactory = modelFactory;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _workContext = workContext;
    }

    public async Task<IActionResult> Profile() => View(PROFILE_VIEW,
        await _modelFactory.PrepareProfileEditAsync(await _workContext.GetCurrentCustomerAsync()));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileEditModel model)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!ModelState.IsValid)
            return View(PROFILE_VIEW, await _modelFactory.PrepareProfileEditAsync(customer, model));
        await _modelFactory.SaveProfileAsync(customer, model);
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.JobSupport.Account.Profile.Saved"));
        return RedirectToRoute("Plugin.Misc.JobSupport.AccountProfile");
    }

    public async Task<IActionResult> Relationships(RelationshipType relationshipType = RelationshipType.InterestReceived) =>
        View(RELATIONSHIPS_VIEW, await _modelFactory.PrepareRelationshipsAsync(
            await _workContext.GetCurrentCustomerAsync(), relationshipType));

    public Task<IActionResult> Shortlisted() => Relationships(RelationshipType.ShortlistedByMe);

    public async Task<IActionResult> Affiliations() => View(AFFILIATIONS_VIEW,
        await _modelFactory.PrepareAffiliationsAsync(await _workContext.GetCurrentCustomerAsync()));
}
