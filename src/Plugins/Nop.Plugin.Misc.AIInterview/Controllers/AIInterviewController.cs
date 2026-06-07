using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Seo;
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
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly IUrlRecordService _urlRecordService;

    public AIInterviewController(IApplicationService applicationService,
        IInterviewSessionService interviewSessionService,
        AIInterviewSettings aiInterviewSettings,
        IWorkContext workContext,
        INotificationService notificationService,
        ILocalizationService localizationService,
        IDownloadService downloadService,
        ICustomerService customerService,
        IProductService productService,
        IProductTemplateService productTemplateService = null,
        IUrlRecordService urlRecordService = null)
    {
        _applicationService = applicationService;
        _interviewSessionService = interviewSessionService;
        _aiInterviewSettings = aiInterviewSettings;
        _workContext = workContext;
        _notificationService = notificationService;
        _localizationService = localizationService;
        _downloadService = downloadService;
        _customerService = customerService;
        _productService = productService;
        _productTemplateService = productTemplateService;
        _urlRecordService = urlRecordService;
    }

    protected static bool SessionMatchesApplication(InterviewSession session, JobApplication application)
    {
        return session.JobApplicationId == application.Id ||
            (session.JobApplicationId == 0 && application.ProductId > 0 && session.ProductId == application.ProductId);
    }

    protected async Task<IList<JobApplication>> GetApplicationsForJobAsync(int customerId, int productId, string jobTitle)
    {
        if (productId <= 0)
            return await _applicationService.GetJobApplicationsByCustomerIdAndJobTitleAsync(customerId, jobTitle)
                ?? new List<JobApplication>();

        var applications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customerId);
        if (applications != null)
            return applications.Where(application => application.ProductId == productId).ToList();

        return await _applicationService.GetJobApplicationsByCustomerIdAndJobTitleAsync(customerId, jobTitle)
            ?? new List<JobApplication>();
    }

    public async Task<IActionResult> Index()
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        var model = new PublicInfoModel
        {
            InterviewRequired = _aiInterviewSettings.InterviewRequired,
            MinimumScore = _aiInterviewSettings.MinimumScore
        };

        return View("~/Plugins/Misc.AIInterview/Views/Index.cshtml", model);
    }

    public async Task<IActionResult> MyApplications(string sortOrder)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        var applications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id);
        var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id);

        var applicationModels = await Task.WhenAll(applications.Select(async a =>
        {
            var appSessions = sessions.Where(s => SessionMatchesApplication(s, a)).ToList();
            var latestSession = appSessions
                .Where(s => s.CompletedOnUtc.HasValue)
                .OrderByDescending(s => s.CompletedOnUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefault();
            var normalizedStatus = JobApplicationStatuses.Normalize(a.Status);

            return new ApplicationModel
            {
                Id = a.Id,
                JobTitle = a.JobTitle,
                InterviewScore = latestSession?.Score,
                InterviewReportUrl = latestSession != null ? Url.Action("Report", "AIInterview", new { sessionId = latestSession.Id }) : null,
                Status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{normalizedStatus}"),
                CreatedOn = a.CreatedOnUtc,
                AttemptCount = appSessions.Count,
                LatestScoreDate = latestSession?.CompletedOnUtc
            };
        }));

        var query = applicationModels.AsQueryable();
        var normalizedSortOrder = string.IsNullOrEmpty(sortOrder) ? "LatestApplied" : sortOrder;

        query = normalizedSortOrder switch
        {
            "OldestApplied" => query.OrderBy(a => a.CreatedOn),
            "HighestScore" => query.OrderByDescending(a => a.InterviewScore ?? 0),
            "LowestScore" => query.OrderBy(a => a.InterviewScore ?? 0),
            "LatestInterviewDate" => query.OrderByDescending(a => a.LatestScoreDate ?? DateTime.MinValue),
            _ => query.OrderByDescending(a => a.CreatedOn)
        };

        var model = new ApplicationListModel
        {
            Applications = query.ToList(),
            SortOrder = normalizedSortOrder
        };

        return View("~/Plugins/Misc.AIInterview/Views/MyApplications.cshtml", model);
    }

    public async Task<IActionResult> Report(int sessionId)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        if (!await _interviewSessionService.CanAccessReportAsync(customer.Id, sessionId))
            return Challenge();

        var session = await _interviewSessionService.GetInterviewSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrEmpty(session.ReportData))
        {
            _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Report.NotFound"));
            return RedirectToRoute(AIInterviewDefaults.MyApplicationsRouteName);
        }

        var model = new ApplicationModel
        {
            Id = session.Id,
            InterviewScore = session.Score,
            QuestionScores = session.QuestionScores,
            StatusComment = session.ReportData,
            CreatedOn = session.CreatedOnUtc
        };

        return View("~/Plugins/Misc.AIInterview/Views/Report.cshtml", model);
    }

    public async Task<IActionResult> Interview(string sessionKey)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        var session = await _interviewSessionService.GetSessionBySessionKeyAsync(sessionKey);
        if (session == null || session.CustomerId != customer.Id || session.CompletedOnUtc.HasValue)
            return RedirectToRoute(AIInterviewDefaults.IndexRouteName);

        ViewBag.SessionKey = sessionKey;
        ViewBag.Difficulty = session.Difficulty;

        return View("~/Plugins/Misc.AIInterview/Views/Interview.cshtml");
    }

    public async Task<IActionResult> Apply(string jobTitle, int productId = 0)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        if (!string.IsNullOrEmpty(jobTitle))
        {
            var applications = await GetApplicationsForJobAsync(customer.Id, productId, jobTitle);
            if (applications.Any(a => !JobApplicationStatuses.CanReapply(a.Status)))
            {
                _notificationService.WarningNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.AlreadyApplied"));
                return RedirectToRoute(AIInterviewDefaults.IndexRouteName);
            }
        }

        var model = new ApplyModel { JobTitle = jobTitle, ProductId = productId };
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

        var applications = await GetApplicationsForJobAsync(customer.Id, model.ProductId, model.JobTitle);
        if (applications.Any(a => !JobApplicationStatuses.CanReapply(a.Status)))
        {
            _notificationService.WarningNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.AlreadyApplied"));
            return RedirectToRoute(AIInterviewDefaults.IndexRouteName);
        }

        if (string.IsNullOrWhiteSpace(model.JobTitle))
            ModelState.AddModelError(nameof(model.JobTitle), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.JobTitle.Required"));

        var allApplications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id) ?? new List<JobApplication>();
        var reusableResumeDownloadId = allApplications
            .OrderByDescending(a => a.CreatedOnUtc)
            .Where(a => a.ResumeDownloadId > 0)
            .Select(a => a.ResumeDownloadId)
            .FirstOrDefault();

        if (model.ResumeFile == null)
        {
            if (reusableResumeDownloadId > 0)
            {
                ModelState.Remove(nameof(model.ResumeFile));
            }
            else if (_aiInterviewSettings.ResumeRequired && !ModelState.ContainsKey(nameof(model.ResumeFile)))
            {
                ModelState.AddModelError(nameof(model.ResumeFile), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Required"));
            }
        }
        else
        {
            var extension = Path.GetExtension(model.ResumeFile.FileName);
            var validExtension = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);
            if (!validExtension || model.ResumeFile.Length > 5 * 1024 * 1024)
                ModelState.AddModelError(nameof(model.ResumeFile), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid"));
        }

        if (!ModelState.IsValid)
            return View("~/Plugins/Misc.AIInterview/Views/Apply.cshtml", model);

        if (_aiInterviewSettings.InterviewRequired)
        {
            var legacyApplicationIds = allApplications
                .Where(application => application.ProductId == model.ProductId)
                .Select(application => application.Id)
                .ToList();
            var completedSessions = ((await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id)) ?? new List<InterviewSession>())
                .Where(s => s.CompletedOnUtc.HasValue &&
                    (s.ProductId == model.ProductId ||
                        (s.ProductId == 0 && legacyApplicationIds.Contains(s.JobApplicationId))))
                .ToList();
            if (!completedSessions.Any())
            {
                _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.InterviewRequired"));
                return View("~/Plugins/Misc.AIInterview/Views/Apply.cshtml", model);
            }

            var highestScore = await _interviewSessionService.GetHighestScoreByCustomerIdAndProductIdAsync(customer.Id, model.ProductId);
            if (highestScore < _aiInterviewSettings.MinimumScore)
            {
                _notificationService.ErrorNotification(string.Format(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.MinimumScoreNotReached"), _aiInterviewSettings.MinimumScore));
                return View("~/Plugins/Misc.AIInterview/Views/Apply.cshtml", model);
            }
        }

        var resumeDownloadId = reusableResumeDownloadId;
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
        var jobApplication = new JobApplication
        {
            CustomerId = customer.Id,
            ProductId = model.ProductId,
            JobTitle = model.JobTitle,
            ResumeDownloadId = resumeDownloadId,
            Status = JobApplicationStatuses.Applied,
            CreatedOnUtc = DateTime.UtcNow
        };
        await _applicationService.InsertJobApplicationAsync(jobApplication);

        // Send "Application Submitted" email
        await _applicationService.SendApplicationSubmittedNotificationAsync(jobApplication, (await _workContext.GetWorkingLanguageAsync()).Id);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.Success"));

        return RedirectToRoute(AIInterviewDefaults.IndexRouteName);
    }

    protected async Task<bool> IsAuthorizedForEmployerActionsAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        return customer != null && (await _customerService.IsAdminAsync(customer) || customer.VendorId > 0);
    }

    public async Task<IActionResult> EmployerApplications(ApplicationListModel model, int pageIndex = 0, int pageSize = 10)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var isEmployer = !await _customerService.IsAdminAsync(customer) && customer.VendorId > 0;

        var applications = await _applicationService.GetApplicationsAsync(
            candidateNameOrEmail: model.CandidateNameOrEmail,
            status: model.Status,
            minScore: model.MinScore,
            maxScore: model.MaxScore,
            startDate: model.StartDate,
            endDate: model.EndDate,
            vendorId: isEmployer ? customer.VendorId : 0,
            pageIndex: pageIndex,
            pageSize: pageSize,
            sortByScore: model.SortByScore);

        var customerIds = applications.Select(a => a.CustomerId).Distinct().ToList();
        var customers = await _customerService.GetCustomersByIdsAsync(customerIds.ToArray());

        // Optimize session fetching by getting all sessions for these applications
        // Note: IInterviewSessionService doesn't have GetByApplicationIds, so we'll just fetch by customer if needed,
        // or just leave as is if we want to avoid modifying service for now.
        // Actually, for a small page size, fetching per item is acceptable but let's try to be better.

        model.Applications = await Task.WhenAll(applications.Select(async a =>
        {
            var appCustomer = customers.FirstOrDefault(c => c.Id == a.CustomerId);
            var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(a.CustomerId);
            var appSessions = sessions.Where(s => SessionMatchesApplication(s, a)).ToList();
            var session = appSessions
                .Where(s => s.CompletedOnUtc.HasValue)
                .OrderByDescending(s => s.CompletedOnUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefault();
            var normalizedStatus = JobApplicationStatuses.Normalize(a.Status);

            return new ApplicationModel
            {
                Id = a.Id,
                JobTitle = a.JobTitle,
                CandidateName = appCustomer != null ? (appCustomer.FirstName + " " + appCustomer.LastName).Trim() : await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown"),
                CandidateEmail = appCustomer?.Email ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown"),
                Status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{normalizedStatus}"),
                RawStatus = normalizedStatus,
                StatusComment = a.StatusComment,
                InterviewScore = session?.Score,
                InterviewReportUrl = session != null ? Url.Action("Report", "AIInterview", new { sessionId = session.Id }) : null,
                CreatedOn = a.CreatedOnUtc,
                AttemptCount = appSessions.Count,
                ChargeMode = session != null && session.SponsorInviteId > 0 ? "Sponsor" : "Self-Paid",
                PromptSource = $"Provider: {_aiInterviewSettings.Provider}, Model: {_aiInterviewSettings.Model}"
            };
        }));

        return View("~/Plugins/Misc.AIInterview/Views/EmployerApplications.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(UpdateStatusModel model)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var application = await _applicationService.GetJobApplicationByIdAsync(model.Id);
        if (application == null)
            return NotFound();

        var normalizedStatus = JobApplicationStatuses.Normalize(model.Status);
        if (!JobApplicationStatuses.IsValid(normalizedStatus))
        {
            _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.UpdateStatus.Invalid"));
            return RedirectToAction("EmployerApplications");
        }

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _customerService.IsAdminAsync(customer) && customer.VendorId > 0)
        {
            var product = await _productService.GetProductByIdAsync(application.ProductId);
            if (product == null || product.VendorId != customer.VendorId)
                return Challenge();
        }

        application.Status = normalizedStatus;
        application.StatusComment = model.StatusComment;

        await _applicationService.UpdateJobApplicationAsync(application);

        // Send "Application Status Update" email
        await _applicationService.SendApplicationStatusUpdateNotificationAsync(application, (await _workContext.GetWorkingLanguageAsync()).Id);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.UpdateStatus.Success"));

        return RedirectToAction("EmployerApplications");
    }

    public async Task<IActionResult> ExportCsv(ApplicationListModel model)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var isEmployer = !await _customerService.IsAdminAsync(customer) && customer.VendorId > 0;

        var applications = await _applicationService.GetApplicationsAsync(
            candidateNameOrEmail: model.CandidateNameOrEmail,
            status: model.Status,
            minScore: model.MinScore,
            maxScore: model.MaxScore,
            startDate: model.StartDate,
            endDate: model.EndDate,
            vendorId: isEmployer ? customer.VendorId : 0,
            sortByScore: model.SortByScore);

        var customerIds = applications.Select(a => a.CustomerId).Distinct().ToList();
        var customers = await _customerService.GetCustomersByIdsAsync(customerIds.ToArray());

        var sb = new StringBuilder();
        var idHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ID");
        var candidateHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Candidate");
        var emailHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Email");
        var statusHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.History.Status");
        var scoreHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.History.Score");
        var dateHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.History.Date");
        var jobTitleHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.JobTitle");
        var chargeModeHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ChargeMode");
        var attemptsHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Attempts");
        var promptSourceHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.PromptSource");
        sb.AppendLine($"{idHeader},{candidateHeader},{emailHeader},{statusHeader},{scoreHeader},{dateHeader},{jobTitleHeader},{chargeModeHeader},{attemptsHeader},{promptSourceHeader}");

        foreach (var a in applications)
        {
            var appCustomer = customers.FirstOrDefault(c => c.Id == a.CustomerId);
            var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(a.CustomerId);
            var appSessions = sessions.Where(s => SessionMatchesApplication(s, a)).ToList();
            var session = appSessions.OrderByDescending(s => s.CompletedOnUtc).FirstOrDefault(s => s.CompletedOnUtc.HasValue);

            var candidateName = appCustomer != null ? (appCustomer.FirstName + " " + appCustomer.LastName).Trim() : await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown");
            var email = appCustomer?.Email ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown");
            var status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{JobApplicationStatuses.Normalize(a.Status)}");
            var score = session?.Score.ToString() ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.None");

            var jobTitle = a.JobTitle ?? string.Empty;
            var attempts = appSessions.Count.ToString();
            var chargeMode = session != null && session.SponsorInviteId > 0 ? "Sponsor" : "Self-Paid";
            var promptSource = $"Provider: {_aiInterviewSettings.Provider}, Model: {_aiInterviewSettings.Model}";

            var candidateNameCsv = $"\"{candidateName.Replace("\"", "\"\"")}\"";
            var emailCsv = $"\"{email.Replace("\"", "\"\"")}\"";
            var statusCsv = $"\"{status?.Replace("\"", "\"\"")}\"";
            var jobTitleCsv = $"\"{jobTitle.Replace("\"", "\"\"")}\"";
            var chargeModeCsv = $"\"{chargeMode.Replace("\"", "\"\"")}\"";
            var promptSourceCsv = $"\"{promptSource.Replace("\"", "\"\"")}\"";

            sb.AppendLine($"{a.Id},{candidateNameCsv},{emailCsv},{statusCsv},{score},{a.CreatedOnUtc:yyyy-MM-dd HH:mm:ss},{jobTitleCsv},{chargeModeCsv},{attempts},{promptSourceCsv}");
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "applications.csv");
    }

    public async Task<IActionResult> VendorScoreboard()
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var isAdmin = await _customerService.IsAdminAsync(customer);
        var vendorId = isAdmin ? 0 : customer.VendorId;
        var applications = await _applicationService.GetApplicationsAsync(vendorId: vendorId);
        var products = await _productService.SearchProductsAsync(vendorId: vendorId, showHidden: true);
        var completedScores = new List<decimal>();

        foreach (var application in applications)
        {
            var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(application.CustomerId);
            completedScores.AddRange(sessions
                .Where(s => SessionMatchesApplication(s, application) && s.CompletedOnUtc.HasValue)
                .Select(s => s.Score));
        }

        var recentApplications = applications
            .OrderByDescending(application => application.CreatedOnUtc)
            .Take(5)
            .Select(application => new ApplicationModel
            {
                Id = application.Id,
                JobTitle = application.JobTitle,
                RawStatus = JobApplicationStatuses.Normalize(application.Status),
                Status = application.Status,
                CreatedOn = application.CreatedOnUtc
            })
            .ToList();

        var model = new VendorScoreboardModel
        {
            TotalJobs = products?.Count ?? 0,
            TotalApplications = applications.Count,
            CompletedInterviews = completedScores.Count,
            ShortlistedApplications = applications.Count(application =>
                string.Equals(JobApplicationStatuses.Normalize(application.Status), JobApplicationStatuses.Shortlisted, StringComparison.OrdinalIgnoreCase)),
            AverageScore = completedScores.Any() ? completedScores.Average() : null,
            HighestScore = completedScores.Any() ? completedScores.Max() : null,
            RecentApplications = recentApplications
        };

        return View("~/Plugins/Misc.AIInterview/Views/VendorScoreboard.cshtml", model);
    }

    public async Task<IActionResult> VendorJobCreation()
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        return View("~/Plugins/Misc.AIInterview/Views/VendorJobCreation.cshtml", new VendorJobModel());
    }

    [HttpPost]
    public async Task<IActionResult> VendorJobCreation(VendorJobModel model)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer.VendorId <= 0)
            return Challenge();

        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError(nameof(model.Name), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Name.Required"));

        if (_productTemplateService == null || _urlRecordService == null)
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Unavailable"));

        var productTemplate = _productTemplateService == null
            ? null
            : (await _productTemplateService.GetAllProductTemplatesAsync()).FirstOrDefault(template =>
                string.Equals(template.ViewPath, AIInterviewDefaults.JobProductTemplateViewPath, StringComparison.OrdinalIgnoreCase));
        if (productTemplate == null)
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Unavailable"));

        if (!ModelState.IsValid)
            return View("~/Plugins/Misc.AIInterview/Views/VendorJobCreation.cshtml", model);

        var now = DateTime.UtcNow;
        var product = new Product
        {
            ProductType = ProductType.SimpleProduct,
            ProductTemplateId = productTemplate.Id,
            VendorId = customer.VendorId,
            Name = model.Name.Trim(),
            ShortDescription = model.ShortDescription,
            FullDescription = model.FullDescription,
            Sku = model.Sku,
            VisibleIndividually = true,
            Published = model.Published,
            DisableBuyButton = true,
            IsShipEnabled = false,
            ManageInventoryMethod = ManageInventoryMethod.DontManageStock,
            OrderMinimumQuantity = 1,
            OrderMaximumQuantity = 1,
            CreatedOnUtc = now,
            UpdatedOnUtc = now
        };

        await _productService.InsertProductAsync(product);
        var seName = await _urlRecordService.ValidateSeNameAsync(product, string.Empty, product.Name, true);
        await _urlRecordService.SaveSlugAsync(product, seName, 0);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Success"));
        return RedirectToRoute(AIInterviewDefaults.VendorScoreboardRouteName);
    }
}
