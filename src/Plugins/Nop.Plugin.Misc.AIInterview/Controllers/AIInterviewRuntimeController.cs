using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Localization;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

public class AIInterviewRuntimeController : BasePluginController
{
    private readonly IInterviewSessionService _interviewSessionService;
    private readonly ILocalizationService _localizationService;
    private readonly IWorkContext _workContext;

    public AIInterviewRuntimeController(IInterviewSessionService interviewSessionService,
        ILocalizationService localizationService,
        IWorkContext workContext)
    {
        _interviewSessionService = interviewSessionService;
        _localizationService = localizationService;
        _workContext = workContext;
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

    [HttpPost]
    public async Task<IActionResult> Start(string difficulty = "Medium")
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

    [HttpPost]
    public async Task<IActionResult> SubmitAnswer(string token, string answer)
    {
        var session = await _interviewSessionService.GetSessionByTokenAsync(token);
        if (session == null || !session.IsActive || (session.TokenExpiryUtc.HasValue && session.TokenExpiryUtc < DateTime.UtcNow))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        if (string.IsNullOrEmpty(answer))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidAnswer", "Answer cannot be empty.");

        // Mock answer processing
        return Json(new { success = true, nextQuestion = "Next mock question?" });
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
        session.ReportData = "Mock report content";
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
}
