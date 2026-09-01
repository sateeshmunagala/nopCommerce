using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Services;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.Seo;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.JobSupport.Controllers;

[Authorize]
public class JobSupportRelationshipController : BasePluginController
{
    private readonly IJobSupportProfileQueryService _queryService;
    private readonly IJobSupportRelationshipService _relationshipService;
    private readonly IJobSupportSubscriptionService _subscriptionService;
    private readonly ILocalizationService _localizationService;
    private readonly IProductService _productService;
    private readonly IStoreContext _storeContext;
    private readonly IUrlRecordService _urlRecordService;
    private readonly IWorkContext _workContext;

    public JobSupportRelationshipController(IJobSupportProfileQueryService queryService,
        IJobSupportRelationshipService relationshipService,
        IJobSupportSubscriptionService subscriptionService,
        ILocalizationService localizationService,
        IProductService productService,
        IStoreContext storeContext,
        IUrlRecordService urlRecordService,
        IWorkContext workContext)
    {
        _queryService = queryService;
        _relationshipService = relationshipService;
        _subscriptionService = subscriptionService;
        _localizationService = localizationService;
        _productService = productService;
        _storeContext = storeContext;
        _urlRecordService = urlRecordService;
        _workContext = workContext;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Shortlist(string slug) => ApplyProfileAsync(slug, _relationshipService.ShortlistProfileAsync);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> RemoveShortlist(string slug) => ApplyProfileAsync(slug, _relationshipService.RemoveShortlistAsync);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Interest(string slug) => ApplyProfileAsync(slug, _relationshipService.SendInterestAsync);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Block(string slug) => ApplyProfileAsync(slug, _relationshipService.BlockProfileAsync);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Accept(string slug) => RespondToInterestAsync(slug, true);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Decline(string slug) => RespondToInterestAsync(slug, false);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RevealContact(string slug)
    {
        var profile = await ResolveProfileAsync(slug);
        if (profile == null)
            return await ErrorAsync("Plugins.Misc.JobSupport.Contact.Errors.NotFound");
        var customer = await _workContext.GetCurrentCustomerAsync();
        var decision = await _subscriptionService.RevealContactAsync(customer.Id,
            profile.Id,
            (await _storeContext.GetCurrentStoreAsync()).Id);
        if (!decision.Succeeded)
        {
            return Json(new
            {
                success = false,
                alreadyApplied = false,
                message = await _localizationService.GetResourceAsync(decision.MessageKey),
                remainingCredits = decision.RemainingCredits
            });
        }
        return Json(new
        {
            success = true,
            alreadyApplied = decision.AlreadyRevealed,
            message = await _localizationService.GetResourceAsync(decision.MessageKey),
            email = decision.Email,
            phone = decision.Phone,
            remainingCredits = decision.RemainingCredits
        });
    }

    private async Task<IActionResult> ApplyProfileAsync(string slug,
        Func<int, int, Task<RelationshipActionResult>> action)
    {
        var profile = await ResolveProfileAsync(slug);
        if (profile == null)
            return await ErrorAsync("Plugins.Misc.JobSupport.Relationship.Errors.ProfileNotFound");
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (profile.VendorId == customer.Id)
            return await ErrorAsync("Plugins.Misc.JobSupport.Relationship.Errors.SelfRelationship");
        return await ResultAsync(await action(customer.Id, profile.Id));
    }

    private async Task<IActionResult> RespondToInterestAsync(string slug, bool accept)
    {
        var profile = await ResolveProfileAsync(slug);
        if (profile == null)
            return await ErrorAsync("Plugins.Misc.JobSupport.Relationship.Errors.ProfileNotFound");
        var customer = await _workContext.GetCurrentCustomerAsync();
        var incoming = await _queryService.GetProfilesByRelationshipAsync(new ProfileSearchRequest
        {
            ProfileIds = new[] { profile.Id },
            CustomerId = customer.Id,
            StoreId = (await _storeContext.GetCurrentStoreAsync()).Id,
            RelationshipType = RelationshipType.InterestReceived,
            PageIndex = 0,
            PageSize = 1
        });
        if (!incoming.Succeeded || incoming.Items.All(item => item.Id != profile.Id))
            return await ErrorAsync("Plugins.Misc.JobSupport.Relationship.Errors.InterestNotFound");
        var result = accept
            ? await _relationshipService.AcceptInterestAsync(customer.Id, profile.VendorId)
            : await _relationshipService.DeclineInterestAsync(customer.Id, profile.VendorId);
        return await ResultAsync(result);
    }

    private async Task<Nop.Core.Domain.Catalog.Product> ResolveProfileAsync(string slug)
    {
        var record = await _urlRecordService.GetBySlugAsync(slug);
        if (record == null || !record.IsActive || !record.EntityName.Equals(nameof(Nop.Core.Domain.Catalog.Product), StringComparison.OrdinalIgnoreCase))
            return null;
        var profile = await _productService.GetProductByIdAsync(record.EntityId);
        return profile == null || profile.Deleted ? null : profile;
    }

    private async Task<IActionResult> ResultAsync(RelationshipActionResult result) => Json(new
    {
        success = result.Succeeded,
        alreadyApplied = result.AlreadyApplied,
        message = await _localizationService.GetResourceAsync(result.UserMessageKey)
    });

    private async Task<IActionResult> ErrorAsync(string key) => Json(new
    {
        success = false,
        alreadyApplied = false,
        message = await _localizationService.GetResourceAsync(key)
    });
}
