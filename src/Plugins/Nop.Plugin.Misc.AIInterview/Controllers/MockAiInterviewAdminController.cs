using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class MockAiInterviewAdminController : BasePluginController
{
    private readonly ICreditService _creditService;
    private readonly ISponsorInviteService _inviteService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IWorkContext _workContext;
    private readonly ISettingService _settingService;
    private readonly AIInterviewSettings _aiInterviewSettings;
    private readonly MockAIInterviewSettings _mockAIInterviewSettings;

    public MockAiInterviewAdminController(ICreditService creditService,
        ISponsorInviteService inviteService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IWorkContext workContext,
        ISettingService settingService,
        AIInterviewSettings aiInterviewSettings,
        MockAIInterviewSettings mockAIInterviewSettings)
    {
        _creditService = creditService;
        _inviteService = inviteService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _workContext = workContext;
        _settingService = settingService;
        _aiInterviewSettings = aiInterviewSettings;
        _mockAIInterviewSettings = mockAIInterviewSettings;
    }

    protected async Task<string> GetLocalizedTextAsync(string resourceKey, string defaultValue)
    {
        var text = await _localizationService.GetResourceAsync(resourceKey);
        return string.IsNullOrEmpty(text) ? defaultValue : text;
    }

    protected async Task<IActionResult> LocalizedErrorAsync(string resourceKey, string defaultValue, int statusCode = 400)
    {
        return new JsonResult(new { error = await GetLocalizedTextAsync(resourceKey, defaultValue) })
        {
            StatusCode = statusCode
        };
    }

    public IActionResult Configure()
    {
        var model = new ConfigurationModel
        {
            Enabled = _aiInterviewSettings.Enabled
        };

        return View("~/Plugins/Misc.AIInterview/Views/Configure.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return View("~/Plugins/Misc.AIInterview/Views/Configure.cshtml", model);

        _aiInterviewSettings.Enabled = model.Enabled;
        await _settingService.SaveSettingAsync(_aiInterviewSettings);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return Configure();
    }

    public IActionResult MockConfigure()
    {
        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterviewAdmin/Configure.cshtml");
    }

    public IActionResult Report()
    {
        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterviewAdmin/Report.cshtml");
    }

    [HttpPost]
    public async Task<IActionResult> TopUpCredits(int customerId, decimal amount)
    {
        if (amount <= 0)
        {
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Admin.TopUp.InvalidAmount", "Invalid top-up amount.");
        }

        await _creditService.AddCreditAsync(customerId, amount, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.TopUp.Remarks"));

        return Json(new { success = true, message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.TopUp.Success") });
    }

    [HttpPost]
    public async Task<IActionResult> CreateSponsorInvite(string email, int productId, int maxAttempts, DateTime? expiryDateUtc, int? sponsorId)
    {
        try
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var effectiveSponsorId = sponsorId ?? customer.Id;
            await _inviteService.CreateInviteAsync(effectiveSponsorId, email, productId, maxAttempts, expiryDateUtc);

            return Json(new { success = true, message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.Success") });
        }
        catch (NopException ex)
        {
            return Json(new { error = ex.Message });
        }
        catch (Exception)
        {
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Admin.Invite.UnexpectedError", "An unexpected error occurred.");
        }
    }
}
