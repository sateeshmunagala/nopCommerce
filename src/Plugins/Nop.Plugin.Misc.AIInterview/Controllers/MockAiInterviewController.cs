using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Core.Events;
using Nop.Plugin.Misc.AIInterview.Events;
using Nop.Services.Vendors;
using Nop.Services.Seo;
using Nop.Web.Framework.Controllers;
using Microsoft.Extensions.Logging;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

public class MockAiInterviewController : BasePluginController
{
    private readonly IInterviewSessionService _interviewSessionService;
    private readonly ILocalizationService _localizationService;
    private readonly IWorkContext _workContext;
    private readonly ISponsorInviteService _inviteService;
    private readonly ICreditService _creditService;
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;
    private readonly IVendorService _vendorService;
    private readonly IApplicationService _applicationService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IJobInterviewExperienceService _jobInterviewExperienceService;
    private readonly IUrlRecordService _urlRecordService;
    private readonly IInterviewTurnService _turnService;
    private readonly IInterviewRuntimeService _interviewRuntimeService;
    private readonly ILogger<MockAiInterviewController> _logger;

    public MockAiInterviewController(IInterviewSessionService interviewSessionService,
        ILocalizationService localizationService,
        IWorkContext workContext,
        ISponsorInviteService inviteService,
        ICreditService creditService,
        ICustomerService customerService,
        IProductService productService,
        IVendorService vendorService,
        IApplicationService applicationService,
        IEventPublisher eventPublisher = null,
        IJobInterviewExperienceService jobInterviewExperienceService = null,
        IUrlRecordService urlRecordService = null,
        IInterviewTurnService turnService = null,
        IInterviewRuntimeService interviewRuntimeService = null,
        ILogger<MockAiInterviewController> logger = null)
    {
        _interviewSessionService = interviewSessionService;
        _localizationService = localizationService;
        _workContext = workContext;
        _inviteService = inviteService;
        _creditService = creditService;
        _customerService = customerService;
        _productService = productService;
        _vendorService = vendorService;
        _applicationService = applicationService;
        _eventPublisher = eventPublisher;
        _jobInterviewExperienceService = jobInterviewExperienceService;
        _urlRecordService = urlRecordService;
        _turnService = turnService;
        _interviewRuntimeService = interviewRuntimeService;
        _logger = logger;
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

    protected virtual bool IsSessionExpired(InterviewSession session, DateTime? currentUtc = null)
    {
        var now = currentUtc ?? DateTime.UtcNow;
        return session != null &&
               session.TokenExpiryUtc.HasValue &&
               session.TokenExpiryUtc.Value <= now;
    }

    protected virtual bool IsSessionUsable(InterviewSession session, DateTime? currentUtc = null)
    {
        if (session == null)
            return false;

        if (!session.IsActive || session.CompletedOnUtc.HasValue)
            return false;

        return !IsSessionExpired(session, currentUtc);
    }

    public async Task<IActionResult> Start(int productId = 0, string sponsorToken = null)
    {
        if (productId > 0 && _urlRecordService != null)
        {
            var product = await _productService.GetProductByIdAsync(productId);
            if (product != null)
                return RedirectToRoute("Product", new { SeName = await _urlRecordService.GetSeNameAsync(product), sponsorToken });
        }

        return RedirectToRoute("Homepage");
    }

    [HttpPost]
    [ActionName("Start")]
    public async Task<IActionResult> StartPost(Microsoft.AspNetCore.Http.IFormCollection form, int productId = 0, string difficulty = AIInterviewDefaults.DefaultInterviewDifficulty, string sponsorToken = null)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unauthorized", "Unauthorized runtime request.", 401);

        var product = productId > 0 ? await _productService.GetProductByIdAsync(productId) : null;
        if (product != null && _jobInterviewExperienceService != null)
            difficulty = await _jobInterviewExperienceService.ResolveInterviewDifficultyAsync(product, form) ?? AIInterviewDefaults.DefaultInterviewDifficulty;
        else
            difficulty = !string.IsNullOrWhiteSpace(form["difficulty"]) ? form["difficulty"] : difficulty ?? AIInterviewDefaults.DefaultInterviewDifficulty;

        var customerSessions = (await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id) ?? new List<InterviewSession>())
            .Where(s => s.ProductId == productId)
            .OrderByDescending(s => s.CreatedOnUtc)
            .ThenByDescending(s => s.Id)
            .ToList();

        var now = DateTime.UtcNow;
        var reusableSession = customerSessions.FirstOrDefault(s =>
            s.IsActive &&
            !s.CompletedOnUtc.HasValue &&
            (!s.TokenExpiryUtc.HasValue || s.TokenExpiryUtc > now));

        var staleActiveSessions = customerSessions.Where(s =>
            s.IsActive &&
            s.TokenExpiryUtc.HasValue &&
            s.TokenExpiryUtc <= now).ToList();

        foreach (var staleSession in staleActiveSessions)
        {
            staleSession.IsActive = false;
            if (!staleSession.CompletedOnUtc.HasValue)
                staleSession.CompletedOnUtc = now;

            await _interviewSessionService.UpdateInterviewSessionAsync(staleSession);
            _logger?.LogInformation("AIInterview stale session auto-healed for customer {CustomerId}, product {ProductId}, session {SessionId}.",
                customer.Id, productId, staleSession.Id);
        }

        if (reusableSession != null && IsSessionUsable(reusableSession, now))
        {
            return Json(new
            {
                sessionKey = reusableSession.SessionKey,
                token = reusableSession.Token,
                runtimeUrl = Url?.RouteUrl(AIInterviewDefaults.MockRuntimeRouteName, new { token = reusableSession.Token })
            });
        }

        int sponsorInviteId = 0;
        bool validSponsorInvite = false;
        if (!string.IsNullOrEmpty(sponsorToken))
        {
            var invite = await _inviteService.GetSponsorInviteByCodeAsync(sponsorToken);
            var sponsoredAttempts = invite == null
                ? 0
                : ((await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id)) ?? new List<InterviewSession>())
                    .Count(session => session.SponsorInviteId == invite.Id);
            if (invite != null &&
                !invite.IsAccepted &&
                (!invite.ExpiryDateUtc.HasValue || invite.ExpiryDateUtc > DateTime.UtcNow) &&
                string.Equals(invite.Email, customer.Email, StringComparison.OrdinalIgnoreCase) &&
                sponsoredAttempts < invite.MaxAttempts)
            {
                // Sponsor validation logic: check if sponsor wallet has credits
                var sponsorWallet = await _creditService.GetOrCreateWalletAsync(invite.SponsorId);
                if (sponsorWallet.Balance >= 1)
                {
                    var chargedSponsor = await _creditService.AuthorizeAndChargeAsync(invite.SponsorId, 1, $"Sponsored Interview Start Charge for {customer.Email}");
                    if (chargedSponsor)
                    {
                        validSponsorInvite = true;
                        sponsorInviteId = invite.Id;
                    }
                }
            }
        }

        if (!validSponsorInvite)
        {
            var charged = await _creditService.AuthorizeAndChargeAsync(customer.Id, 1, "Interview Start Charge");
            if (!charged)
                return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.NoCredits", "Insufficient credits. Please purchase credits to start the interview.");
        }

        var application = ((await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id)) ?? new List<JobApplication>())
            .Where(a => a.ProductId == productId)
            .OrderByDescending(a => a.CreatedOnUtc)
            .FirstOrDefault();

        var session = new InterviewSession
        {
            CustomerId = customer.Id,
            ProductId = productId,
            JobApplicationId = application?.Id ?? 0,
            SessionKey = Guid.NewGuid().ToString("N"),
            Difficulty = difficulty,
            Token = Guid.NewGuid().ToString("N"),
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(30),
            IsActive = true,
            SponsorInviteId = sponsorInviteId,
            StartedOnUtc = DateTime.UtcNow,
            CreatedOnUtc = DateTime.UtcNow
        };
        await _interviewSessionService.InsertInterviewSessionAsync(session);
        _logger?.LogInformation("AIInterview new session created for customer {CustomerId}, product {ProductId}, session {SessionId}.",
            customer.Id, productId, session.Id);

        return Json(new
        {
            sessionKey = session.SessionKey,
            token = session.Token,
            runtimeUrl = Url?.RouteUrl(AIInterviewDefaults.MockRuntimeRouteName, new { token = session.Token })
        });
    }

    public async Task<IActionResult> Runtime(string token)
    {
        var session = await _interviewSessionService.GetSessionByTokenAsync(token);
        if (session == null || !session.IsActive || (session.TokenExpiryUtc.HasValue && session.TokenExpiryUtc <= DateTime.UtcNow))
            return Redirect(await GetRestartUrlAsync(session));

        var model = _interviewRuntimeService == null
            ? new Nop.Plugin.Misc.AIInterview.Models.InterviewRuntimeModel
            {
                SessionId = session.Id,
                ProductId = session.ProductId,
                SessionKey = session.SessionKey,
                Token = session.Token,
                Difficulty = session.Difficulty,
                ProductName = (await _productService.GetProductByIdAsync(session.ProductId))?.Name ?? "Interview",
                CurrentQuestion = "Tell me about your background for this role."
            }
            : await _interviewRuntimeService.EnsureInterviewStartedAsync(session);

        ApplyRuntimeClientSettings(model, session);

        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterview/Runtime.cshtml", model);
    }

    protected virtual void ApplyRuntimeClientSettings(Nop.Plugin.Misc.AIInterview.Models.InterviewRuntimeModel model, InterviewSession session)
    {
        if (model == null)
            return;

        model.ClientSettings ??= new Nop.Plugin.Misc.AIInterview.Models.RuntimeClientSettingsModel();
        model.ClientSettings.SubmitAnswerUrl = Url?.RouteUrl(AIInterviewDefaults.MockSubmitAnswerRouteName);
        model.ClientSettings.CompleteInterviewUrl = Url?.RouteUrl(AIInterviewDefaults.MockStopRouteName);
        model.ClientSettings.RefreshTokenUrl = Url?.RouteUrl(AIInterviewDefaults.MockRefreshTokenRouteName);
        model.ClientSettings.StopInterviewUrl = Url?.RouteUrl(AIInterviewDefaults.MockStopRouteName);
        model.ClientSettings.SpeechTokenUrl = Url?.RouteUrl(AIInterviewDefaults.MockSpeechTokenRouteName);
        model.ClientSettings.AgoraTokenUrl = Url?.RouteUrl(AIInterviewDefaults.MockAgoraTokenRouteName);
        model.ClientSettings.ProductName = model.ProductName;
        model.ClientSettings.Token = session?.Token;
        model.ClientSettings.ReportUrl = model.ReportUrl;
        model.ClientSettings.TokenExpiryUtc = session?.TokenExpiryUtc;
        model.ClientSettings.SpeechAvailable = model.ClientSettings.SpeechAvailable && !string.IsNullOrWhiteSpace(model.ClientSettings.SpeechTokenUrl);
        model.ClientSettings.AgoraAvailable = model.ClientSettings.AgoraAvailable && !string.IsNullOrWhiteSpace(model.ClientSettings.AgoraTokenUrl);
    }

    protected async Task<string> GetRestartUrlAsync(InterviewSession session)
    {
        var restartUrl = Url?.RouteUrl("Homepage") ?? "/";
        if (session?.ProductId > 0 && _urlRecordService != null)
        {
                var product = await _productService.GetProductByIdAsync(session.ProductId);
                if (product != null)
                {
                    var seName = await _urlRecordService.GetSeNameAsync(product);
                    if (!string.IsNullOrWhiteSpace(seName))
                        restartUrl = $"/{seName}?interviewError=expired";
                }
            }

        return restartUrl;
    }

    protected async Task<IActionResult> RuntimeErrorAsync(string resourceKey, string defaultValue, int statusCode, InterviewSession session = null)
    {
        if (HttpContext != null)
            Response.StatusCode = statusCode;

        var restartUrl = await GetRestartUrlAsync(session);

        return View("~/Plugins/Misc.AIInterview/Views/RuntimeError.cshtml", new Models.RuntimeErrorModel
        {
            Message = await GetLocalizedTextAsync(resourceKey, defaultValue),
            StatusCode = statusCode,
            RestartUrl = restartUrl
        });
    }

    [HttpPost]
    public async Task<IActionResult> SubmitAnswer(string token, string answer)
    {
        if (_interviewRuntimeService != null)
            return Json(await _interviewRuntimeService.SubmitAnswerAsync(token, answer));

        var session = await _interviewSessionService.GetSessionByTokenAsync(token);
        if (!IsSessionUsable(session))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        if (string.IsNullOrEmpty(answer))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidAnswer", "Answer cannot be empty.");

        // Mock answer processing
        return Json(new { success = true, nextQuestion = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.NextQuestionMock") });
    }

    [HttpPost]
    public async Task<IActionResult> Stop(string token)
    {
        if (_interviewRuntimeService != null)
        {
            var response = await _interviewRuntimeService.CompleteInterviewAsync(token, "Stopped by user");
            return Json(response);
        }

        var session = await _interviewSessionService.GetSessionByTokenAsync(token);
        if (!IsSessionUsable(session))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        session.IsActive = false;
        session.CompletedOnUtc = DateTime.UtcNow;

        // Mock question scores
        var questionScores = new[] { 80, 90, 85 };
        session.QuestionScores = System.Text.Json.JsonSerializer.Serialize(questionScores);
        session.Score = (decimal)questionScores.Average();

        session.ReportData = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.ReportContentMock");
        await _interviewSessionService.UpdateInterviewSessionAsync(session);

        await PublishCompletionAsync(session);

        return Json(new { success = true, score = session.Score });
    }

    protected async Task PublishCompletionAsync(InterviewSession session)
    {
        var languageId = (await _workContext.GetWorkingLanguageAsync()).Id;
        if (_eventPublisher != null)
            await _eventPublisher.PublishAsync(new MockAiInterviewCompletedEvent(session, languageId));
        else
            await _interviewSessionService.SendInterviewCompletionNotificationAsync(session, languageId);
    }

    [HttpPost]
    public async Task<IActionResult> RefreshToken(string token)
    {
        var session = await _interviewSessionService.GetSessionByTokenAsync(token);
        if (!IsSessionUsable(session))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        session.Token = Guid.NewGuid().ToString("N");
        session.TokenExpiryUtc = DateTime.UtcNow.AddMinutes(30);
        await _interviewSessionService.UpdateInterviewSessionAsync(session);

        return Json(new { newToken = session.Token, tokenExpiryUtc = session.TokenExpiryUtc });
    }

    [HttpPost]
    public async Task<IActionResult> SpeechToken(string token)
    {
        if (_interviewRuntimeService == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unavailable", "Speech token service is unavailable.");

        var result = await _interviewRuntimeService.GetSpeechTokenAsync(token);
        if (result == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unavailable", "Speech token service is unavailable.");

        return Json(new { success = true, token = result.Token, region = result.Region, expiresInSeconds = result.ExpiresInSeconds });
    }

    [HttpPost]
    public async Task<IActionResult> AgoraToken(string token)
    {
        if (_interviewRuntimeService == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unavailable", "Agora token service is unavailable.");

        var result = await _interviewRuntimeService.GetAgoraTokenAsync(token);
        if (result == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unavailable", "Agora token service is unavailable.");

        return Json(new { success = true, appId = result.AppId, channel = result.Channel, token = result.Token, uid = result.Uid, expiresInSeconds = result.ExpiresInSeconds });
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
            return await RuntimeErrorAsync("Plugins.Misc.AIInterview.Report.AccessDenied", "You do not have access to this interview report.", 403);

        var session = await _interviewSessionService.GetInterviewSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrEmpty(session.ReportData))
            return await RuntimeErrorAsync("Plugins.Misc.AIInterview.Report.NotFound", "Interview report not found.", 404);

        var turns = _turnService == null ? new List<InterviewTurn>() : (await _turnService.GetTurnsBySessionIdAsync(session.Id))?.ToList() ?? new List<InterviewTurn>();
        var model = new InterviewReportModel
        {
            SessionId = session.Id,
            ProductId = session.ProductId,
            SessionKey = session.SessionKey,
            Token = session.Token,
            Difficulty = session.Difficulty,
            ProductName = (await _productService.GetProductByIdAsync(session.ProductId))?.Name ?? "Interview",
            JobTitle = (await _productService.GetProductByIdAsync(session.ProductId))?.Name ?? "Interview",
            Score = session.Score,
            IsCompleted = session.CompletedOnUtc.HasValue,
            QuestionScores = session.QuestionScores,
            ParsedQuestionScores = ParseQuestionScores(session.QuestionScores),
            ReportData = session.ReportData,
            Turns = turns.Select(turn => new InterviewTurnViewModel
            {
                TurnId = turn.Id,
                SequenceNumber = turn.SequenceNumber,
                QuestionText = turn.QuestionText,
                AnswerText = turn.AnswerText,
                Score = turn.Score,
                Feedback = turn.Feedback,
                AskedOnUtc = turn.AskedOnUtc,
                AnsweredOnUtc = turn.AnsweredOnUtc
            }).ToList(),
            CreatedOnUtc = session.CreatedOnUtc,
            CompletedOnUtc = session.CompletedOnUtc
        };

        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterview/Report.cshtml", model);
    }

    protected static IList<decimal> ParseQuestionScores(string questionScores)
    {
        if (string.IsNullOrWhiteSpace(questionScores))
            return new List<decimal>();

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<decimal>>(questionScores);
            return parsed ?? new List<decimal>();
        }
        catch
        {
            return new List<decimal>();
        }
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

        if (string.IsNullOrWhiteSpace(email))
            return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.EmailRequired") });

        if (maxAttempts <= 0)
            return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.InvalidAttempts") });

        if (expiryDateUtc.HasValue && expiryDateUtc.Value <= DateTime.UtcNow)
            return Json(new { error = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.InvalidExpiry") });

        try
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var emails = email.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(e => e.Trim())
                              .Where(e => !string.IsNullOrEmpty(e))
                              .Distinct()
                              .ToList();

            int createdCount = 0;
            int invalidCount = 0;
            var failureMessages = new List<string>();

            foreach (var e in emails)
            {
                if (!CommonHelper.IsValidEmail(e))
                {
                    invalidCount++;
                    continue;
                }

                try
                {
                    await _inviteService.CreateInviteAsync(customer.Id, e, productId, maxAttempts, expiryDateUtc);
                    createdCount++;
                }
                catch (NopException ex)
                {
                    failureMessages.Add(ex.Message);
                }
                catch
                {
                    failureMessages.Add(await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Employer.Invite.Error", "Error creating invite."));
                }
            }

            if (createdCount == 0)
            {
                if (failureMessages.Count == 1)
                    return Json(new { success = false, error = failureMessages[0] });

                if (invalidCount > 0 && failureMessages.Count == 0)
                    return Json(new
                    {
                        success = false,
                        error = await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.Invite.EmailInvalid", "Enter a valid email address.")
                    });

                var noInvitesMessage = failureMessages.FirstOrDefault()
                    ?? $"No invites were created. {invalidCount} email(s) were invalid.";
                return Json(new { success = false, error = noInvitesMessage });
            }

            var bulkSuccessMessageFormat = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.BulkSuccess");
            var message = string.Format(bulkSuccessMessageFormat ?? "Successfully created {0} invites. {1} emails were invalid.", createdCount, invalidCount);
            if (failureMessages.Count > 0)
                message = $"{message} {failureMessages.Count} invite(s) failed: {string.Join("; ", failureMessages.Distinct())}";

            return Json(new { success = true, message = message });
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
