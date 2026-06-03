using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

public class MockAiInterviewController : BasePluginController
{
    private readonly IInterviewSessionService _interviewSessionService;
    private readonly ILocalizationService _localizationService;
    private readonly IWorkContext _workContext;
    private readonly ISponsorInviteService _inviteService;
    private readonly ICreditService _creditService;
    private readonly ICustomerService _customerService;

    public MockAiInterviewController(IInterviewSessionService interviewSessionService,
        ILocalizationService localizationService,
        IWorkContext workContext,
        ISponsorInviteService inviteService,
        ICreditService creditService,
        ICustomerService customerService)
    {
        _interviewSessionService = interviewSessionService;
        _localizationService = localizationService;
        _workContext = workContext;
        _inviteService = inviteService;
        _creditService = creditService;
        _customerService = customerService;
    }

    protected async Task<string> GetLocalizedTextAsync(string resourceKey, string defaultValue)
    {
        var text = await _localizationService.GetResourceAsync(resourceKey);
        return string.IsNullOrEmpty(text) ? defaultValue : text;
    }

    protected async Task<IActionResult> LocalizedErrorAsync(string resourceKey, string defaultValue, int statusCode = 400)
    {
        return Json(new { error = await GetLocalizedTextAsync(resourceKey, defaultValue) });
    }

    public async Task<IActionResult> Start()
    {
        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterview/Start.cshtml");
    }

    [HttpPost]
    [ActionName("Start")]
    public async Task<IActionResult> StartPost(string difficulty = "Medium")
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unauthorized", "Unauthorized runtime request.", 401);

        // Idempotency: check for active session
        var activeSession = (await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id))
            .FirstOrDefault(s => s.IsActive && !s.CompletedOnUtc.HasValue);

        if (activeSession != null)
        {
            return Json(new
            {
                sessionKey = activeSession.SessionKey,
                token = activeSession.Token
            });
        }

        var session = new InterviewSession
        {
            CustomerId = customer.Id,
            SessionKey = Guid.NewGuid().ToString("N"),
            Difficulty = difficulty,
            Token = Guid.NewGuid().ToString("N"),
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(30),
            IsActive = true,
            StartedOnUtc = DateTime.UtcNow,
            CreatedOnUtc = DateTime.UtcNow
        };
        await _interviewSessionService.InsertInterviewSessionAsync(session);

        return Json(new
        {
            sessionKey = session.SessionKey,
            token = session.Token
        });
    }

    public async Task<IActionResult> Runtime()
    {
        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterview/Runtime.cshtml");
    }

    [HttpPost]
    public async Task<IActionResult> SubmitAnswer(string token, string answer)
    {
        var session = await _interviewSessionService.GetSessionByTokenAsync(token);
        if (session == null || !session.IsActive || (session.TokenExpiryUtc.HasValue && session.TokenExpiryUtc < DateTime.UtcNow))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        if (string.IsNullOrEmpty(answer))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidAnswer", "Answer cannot be empty.");

        // Mock answer processing
        return Json(new { success = true, nextQuestion = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.NextQuestionMock") });
    }

    [HttpPost]
    public async Task<IActionResult> Stop(string token)
    {
        var session = await _interviewSessionService.GetSessionByTokenAsync(token);
        if (session == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        session.IsActive = false;
        session.CompletedOnUtc = DateTime.UtcNow;
        session.Score = 85; // Mock score
        session.ReportData = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.ReportContentMock");
        await _interviewSessionService.UpdateInterviewSessionAsync(session);

        return Json(new { success = true, score = session.Score });
    }

    [HttpPost]
    public async Task<IActionResult> RefreshToken(string token)
    {
        var session = await _interviewSessionService.GetSessionByTokenAsync(token);
        if (session == null || !session.IsActive)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        // Simulate token service failure if needed for tests
        if (token == "fail-me")
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.TokenServiceFailure", "Token service failure.");

        session.Token = Guid.NewGuid().ToString("N");
        session.TokenExpiryUtc = DateTime.UtcNow.AddMinutes(30);
        await _interviewSessionService.UpdateInterviewSessionAsync(session);

        return Json(new { newToken = session.Token });
    }

    public async Task<IActionResult> History()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id);
        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterview/History.cshtml", sessions);
    }

    public async Task<IActionResult> Report(int sessionId)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        if (!await _interviewSessionService.CanAccessReportAsync(customer.Id, sessionId))
            return Challenge();

        var session = await _interviewSessionService.GetInterviewSessionByIdAsync(sessionId);
        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterview/Report.cshtml", session);
    }

    protected async Task<bool> IsAuthorizedAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        return customer != null && (await _customerService.IsAdminAsync(customer) || customer.VendorId > 0);
    }

    public async Task<IActionResult> EmployerManage()
    {
        if (!await IsAuthorizedAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var invites = await _inviteService.GetSponsorInvitesAsync(customer.Id);
        var wallet = await _creditService.GetOrCreateWalletAsync(customer.Id);

        ViewBag.CreditBalance = wallet.Balance;

        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterview/EmployerManage.cshtml", invites);
    }

    [HttpPost]
    public async Task<IActionResult> CreateInvite(string email, int productId, int maxAttempts, DateTime? expiryDateUtc)
    {
        if (!await IsAuthorizedAsync())
            return Challenge();

        try
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            await _inviteService.CreateInviteAsync(customer.Id, email, productId, maxAttempts, expiryDateUtc);
            return Json(new { success = true, message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.Success") });
        }
        catch (NopException ex)
        {
            return Json(new { error = ex.Message });
        }
        catch (Exception)
        {
            return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.Error") });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeactivateInvite(int id)
    {
        if (!await IsAuthorizedAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        await _inviteService.DeactivateInviteAsync(id, customer.Id);
        return Json(new { success = true, message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.Deactivated") });
    }
}
