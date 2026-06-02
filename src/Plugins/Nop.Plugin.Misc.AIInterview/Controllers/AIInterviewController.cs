using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Media;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

public class AIInterviewController : BasePluginController
{
    private readonly IApplicationService _applicationService;
    private readonly IInterviewSessionService _interviewSessionService;
    private readonly AIInterviewSettings _aiInterviewSettings;
    private readonly IWorkContext _workContext;
    private readonly INotificationService _notificationService;
    private readonly ILocalizationService _localizationService;
    private readonly IDownloadService _downloadService;

    public AIInterviewController(IApplicationService applicationService,
        IInterviewSessionService interviewSessionService,
        AIInterviewSettings aiInterviewSettings,
        IWorkContext workContext,
        INotificationService notificationService,
        ILocalizationService localizationService,
        IDownloadService downloadService)
    {
        _applicationService = applicationService;
        _interviewSessionService = interviewSessionService;
        _aiInterviewSettings = aiInterviewSettings;
        _workContext = workContext;
        _notificationService = notificationService;
        _localizationService = localizationService;
        _downloadService = downloadService;
    }

    public async Task<IActionResult> Index()
    {
        return View("~/Plugins/Misc.AIInterview/Views/Index.cshtml");
    }

    public async Task<IActionResult> Apply()
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        var applications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id);
        if (applications.Any())
        {
            _notificationService.WarningNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.AlreadyApplied"));
            return RedirectToRoute(AIInterviewDefaults.IndexRouteName);
        }

        var model = new ApplyModel();
        return View("~/Plugins/Misc.AIInterview/Views/Apply.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> Apply(ApplyModel model)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        // 1. Already applied check
        var applications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id);
        if (applications.Any())
        {
            _notificationService.WarningNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.AlreadyApplied"));
            return RedirectToRoute(AIInterviewDefaults.IndexRouteName);
        }

        // 2. Localized validation via ModelState (ApplyModelValidator handles JobTitle and ResumeRequired)
        if (!ModelState.IsValid)
        {
            return View("~/Plugins/Misc.AIInterview/Views/Apply.cshtml", model);
        }

        // 3. Interview gating rules
        if (_aiInterviewSettings.InterviewRequired)
        {
            var latestSession = await _interviewSessionService.GetLatestCompletedSessionByCustomerIdAsync(customer.Id);
            if (latestSession == null)
            {
                _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.InterviewRequired"));
                return View("~/Plugins/Misc.AIInterview/Views/Apply.cshtml", model);
            }

            if (latestSession.Score < _aiInterviewSettings.MinimumScore)
            {
                _notificationService.ErrorNotification(string.Format(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.MinimumScoreNotReached"), _aiInterviewSettings.MinimumScore, latestSession.Score));
                return View("~/Plugins/Misc.AIInterview/Views/Apply.cshtml", model);
            }
        }

        // 4. Handle Resume Upload
        int resumeDownloadId = 0;
        if (model.ResumeFile != null)
        {
            var download = new Download
            {
                DownloadGuid = Guid.NewGuid(),
                UseDownloadUrl = false,
                DownloadBinary = await _downloadService.GetDownloadBitsAsync(model.ResumeFile),
                ContentType = model.ResumeFile.ContentType,
                Filename = model.ResumeFile.FileName,
                Extension = Path.GetExtension(model.ResumeFile.FileName),
                IsNew = true
            };
            await _downloadService.InsertDownloadAsync(download);
            resumeDownloadId = download.Id;
        }

        // 5. Save application
        var jobApplication = new JobApplication
        {
            CustomerId = customer.Id,
            JobTitle = model.JobTitle,
            ResumeDownloadId = resumeDownloadId,
            Status = "Applied",
            CreatedOnUtc = DateTime.UtcNow
        };
        await _applicationService.InsertJobApplicationAsync(jobApplication);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.Success"));

        return RedirectToRoute(AIInterviewDefaults.IndexRouteName);
    }
}
