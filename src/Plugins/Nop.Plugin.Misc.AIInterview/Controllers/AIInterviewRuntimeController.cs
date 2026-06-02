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
    public async Task<IActionResult> Start()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unauthorized", "Unauthorized runtime request.", 401);

        // Logic to start session (simplified for task scope)
        var session = new InterviewSession
        {
            CustomerId = customer.Id,
            SessionKey = Guid.NewGuid().ToString("N")
        };
        await _interviewSessionService.InsertInterviewSessionAsync(session);

        return Json(new { sessionKey = session.SessionKey });
    }

    [HttpPost]
    public async Task<IActionResult> SubmitAnswer(string sessionKey, string answer)
    {
        if (string.IsNullOrEmpty(sessionKey))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> Stop(string sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> RefreshToken(string sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        return Json(new { newToken = Guid.NewGuid().ToString("N") });
    }
}
