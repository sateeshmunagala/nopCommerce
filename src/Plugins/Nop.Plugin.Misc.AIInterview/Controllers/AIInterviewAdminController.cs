using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class AIInterviewAdminController : BasePluginController
{
    private readonly ICreditService _creditService;
    private readonly ISponsorInviteService _inviteService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IWorkContext _workContext;

    public AIInterviewAdminController(ICreditService creditService,
        ISponsorInviteService inviteService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IWorkContext workContext)
    {
        _creditService = creditService;
        _inviteService = inviteService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _workContext = workContext;
    }

    public IActionResult Configure()
    {
        return View("~/Plugins/Misc.AIInterview/Views/Configure.cshtml");
    }

    [HttpPost]
    public async Task<IActionResult> TopUpCredits(int customerId, decimal amount)
    {
        if (amount <= 0)
        {
            return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.TopUp.InvalidAmount") });
        }

        await _creditService.AddCreditAsync(customerId, amount, "Admin top-up");

        return Json(new { success = true, message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.TopUp.Success") });
    }

    [HttpPost]
    public async Task<IActionResult> CreateSponsorInvite(string email, int productId, int maxAttempts, DateTime? expiryDateUtc)
    {
        try
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            await _inviteService.CreateInviteAsync(customer.Id, email, productId, maxAttempts, expiryDateUtc);

            return Json(new { success = true, message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.Success") });
        }
        catch (NopException ex)
        {
            return Json(new { error = ex.Message });
        }
        catch (Exception)
        {
            return Json(new { error = "An unexpected error occurred." });
        }
    }
}
