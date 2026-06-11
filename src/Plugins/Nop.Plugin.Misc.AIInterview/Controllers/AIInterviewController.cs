using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http;
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
using Nop.Services.Vendors;
using Nop.Services.Orders;
using Nop.Services.Stores;
using Nop.Services.Common;
using Nop.Web.Framework.Controllers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Rendering;

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
    private readonly IJobInterviewExperienceService _jobInterviewExperienceService;
    private readonly IJobRequirementService _jobRequirementService;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IInterviewTurnService _interviewTurnService;
    private readonly IHttpClientFactory _httpClientFactory;

    public AIInterviewController(IApplicationService applicationService,
        IInterviewSessionService interviewSessionService,
        AIInterviewSettings aiInterviewSettings,
        IWorkContext workContext,
        INotificationService notificationService,
        ILocalizationService localizationService,
        IDownloadService downloadService,
        ICustomerService customerService,
        IProductService productService,
        IJobRequirementService jobRequirementService,
        IProductTemplateService productTemplateService = null,
        IUrlRecordService urlRecordService = null,
        IJobInterviewExperienceService jobInterviewExperienceService = null,
        ISpecificationAttributeService specificationAttributeService = null,
        IInterviewTurnService interviewTurnService = null,
        IHttpClientFactory httpClientFactory = null)
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
        _jobInterviewExperienceService = jobInterviewExperienceService;
        _jobRequirementService = jobRequirementService;
        _specificationAttributeService = specificationAttributeService;
        _interviewTurnService = interviewTurnService;
        _httpClientFactory = httpClientFactory;
    }

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
        IUrlRecordService urlRecordService = null,
        IJobInterviewExperienceService jobInterviewExperienceService = null,
        ISpecificationAttributeService specificationAttributeService = null)
        : this(applicationService,
            interviewSessionService,
            aiInterviewSettings,
            workContext,
            notificationService,
            localizationService,
            downloadService,
            customerService,
            productService,
            null,
            productTemplateService,
            urlRecordService,
            jobInterviewExperienceService,
            specificationAttributeService,
            null,
            null)
    {
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

    protected static IList<decimal> ParseQuestionScores(string questionScores)
    {
        if (string.IsNullOrWhiteSpace(questionScores))
            return new List<decimal>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<decimal>>(questionScores);
            return parsed ?? new List<decimal>();
        }
        catch
        {
            return new List<decimal>();
        }
    }

    protected static (string Summary, string Feedback) SplitReportSections(string reportData)
    {
        if (string.IsNullOrWhiteSpace(reportData))
            return (string.Empty, string.Empty);

        var lines = reportData
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (!lines.Any())
            return (reportData, string.Empty);

        return (lines.FirstOrDefault() ?? string.Empty, lines.Skip(1).FirstOrDefault() ?? string.Empty);
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
            MinimumScore = _aiInterviewSettings.MinimumScore
        };

        return View("~/Plugins/Misc.AIInterview/Views/Index.cshtml", model);
    }

    public async Task<IActionResult> MyApplications(string sortOrder, string status = null, decimal? minScore = null, decimal? maxScore = null)
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
            var questionScores = ParseQuestionScores(latestSession?.QuestionScores);
            var reportSections = SplitReportSections(latestSession?.ReportData);

            return new ApplicationModel
            {
                Id = a.Id,
                JobTitle = a.JobTitle,
                InterviewScore = latestSession?.Score,
                InterviewReportUrl = latestSession != null ? Url.Action("Report", "AIInterview", new { sessionId = latestSession.Id }) : null,
                RecordingUrl = latestSession != null && !string.IsNullOrWhiteSpace(latestSession.RecordingUrl) ? Url.Action("Recording", "AIInterview", new { sessionId = latestSession.Id }) : null,
                Status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{normalizedStatus}"),
                RawStatus = normalizedStatus,
                CreatedOn = a.CreatedOnUtc,
                AttemptCount = appSessions.Count,
                LatestScoreDate = latestSession?.CompletedOnUtc,
                CompletedOn = latestSession?.CompletedOnUtc,
                QuestionScores = latestSession?.QuestionScores,
                QuestionScoreValues = questionScores,
                ReportSummary = reportSections.Summary,
                FeedbackSummary = reportSections.Feedback
            };
        }));

        var query = applicationModels.AsQueryable();
        var normalizedSortOrder = string.IsNullOrEmpty(sortOrder) ? "LatestApplied" : sortOrder;

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(application => string.Equals(application.RawStatus ?? JobApplicationStatuses.Normalize(application.Status), JobApplicationStatuses.Normalize(status), StringComparison.OrdinalIgnoreCase)
                || string.Equals(application.Status, status, StringComparison.OrdinalIgnoreCase));

        if (minScore.HasValue)
            query = query.Where(application => (application.InterviewScore ?? 0) >= minScore.Value);

        if (maxScore.HasValue)
            query = query.Where(application => (application.InterviewScore ?? 0) <= maxScore.Value);

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
            SortOrder = normalizedSortOrder,
            Status = status,
            MinScore = minScore,
            MaxScore = maxScore,
            TotalCount = query.Count()
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

        var turns = new List<InterviewTurn>();
        if (_interviewTurnService != null)
        {
            try
            {
                turns = (await _interviewTurnService.GetTurnsBySessionIdAsync(sessionId)).ToList();
            }
            catch
            {
                turns = new List<InterviewTurn>();
            }
        }

        var model = new InterviewReportModel
        {
            SessionId = session.Id,
            CustomerId = session.CustomerId,
            ProductId = session.ProductId,
            ProductName = session.ProductId > 0 ? (await _productService.GetProductByIdAsync(session.ProductId))?.Name : string.Empty,
            JobTitle = session.ProductId > 0 ? (await _productService.GetProductByIdAsync(session.ProductId))?.Name : string.Empty,
            Difficulty = session.Difficulty,
            Score = session.Score,
            QuestionScores = session.QuestionScores,
            ParsedQuestionScores = ParseQuestionScores(session.QuestionScores),
            ReportData = session.ReportData,
            RecordingUrl = Url?.Action("Recording", "AIInterview", new { sessionId = session.Id }),
            CreatedOnUtc = session.CreatedOnUtc,
            CompletedOnUtc = session.CompletedOnUtc,
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
            }).ToList()
        };

        return View("~/Plugins/Misc.AIInterview/Views/Report.cshtml", model);
    }

    [HttpGet]
    public async Task<IActionResult> Recording(int sessionId)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        if (!await _interviewSessionService.CanAccessReportAsync(customer.Id, sessionId))
            return Challenge();

        var session = await _interviewSessionService.GetInterviewSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrWhiteSpace(session.RecordingUrl))
            return NotFound();

        var playbackUrl = BuildRecordingPlaybackUrl(session.RecordingUrl);
        if (string.IsNullOrWhiteSpace(playbackUrl))
            return NotFound();

        using var client = _httpClientFactory?.CreateClient(nameof(AIInterviewController)) ?? new HttpClient();
        var response = await client.GetAsync(playbackUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return NotFound();
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "video/webm";
        var stream = await response.Content.ReadAsStreamAsync();
        return File(new ProxyResponseStream(stream, response), contentType);
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

        return RedirectToRoute(AIInterviewDefaults.MockRuntimeRouteName, new { token = session.Token });
    }

    protected virtual string BuildRecordingPlaybackUrl(string recordingUrl)
    {
        if (string.IsNullOrWhiteSpace(recordingUrl) ||
            string.IsNullOrWhiteSpace(_aiInterviewSettings.AzureBlobStorageSasToken))
            return null;

        var sasToken = _aiInterviewSettings.AzureBlobStorageSasToken.Trim();
        if (!sasToken.StartsWith("?", StringComparison.Ordinal))
            sasToken = sasToken.StartsWith("&", StringComparison.Ordinal) ? "?" + sasToken[1..] : "?" + sasToken;

        return $"{recordingUrl.TrimEnd('/')}{sasToken}";
    }

    private sealed class ProxyResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;

        public ProxyResponseStream(Stream inner, HttpResponseMessage response)
        {
            _inner = inner;
            _response = response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    }

    public async Task<IActionResult> Apply(string jobTitle, int productId = 0)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        if (productId > 0 && _urlRecordService != null)
        {
            var product = await _productService.GetProductByIdAsync(productId);
            if (product != null)
                return RedirectToRoute("Product", new { SeName = await _urlRecordService.GetSeNameAsync(product) });
        }

        return RedirectToRoute("Homepage");
    }

    [HttpPost]
    public async Task<IActionResult> Apply(ApplyModel model)
    {
        var result = await SubmitApplicationAsync(model);
        if (!result.Success)
            return View("~/Plugins/Misc.AIInterview/Views/Apply.cshtml", model);

        _notificationService.SuccessNotification(result.Message);
        return RedirectToRoute(AIInterviewDefaults.MyApplicationsRouteName);
    }

    [HttpPost]
    public async Task<IActionResult> ApplyInline(ApplyModel model)
    {
        var result = await SubmitApplicationAsync(model);
        if (!result.Success)
            return Json(new { success = false, error = result.Message });

        return Json(new { success = true, message = result.Message });
    }

    protected virtual async Task<ApplySubmissionResult> SubmitApplicationAsync(ApplyModel model)
    {
        if (!_aiInterviewSettings.Enabled)
            return new ApplySubmissionResult
            {
                Success = false,
                Message = await _localizationService.GetResourceAsync("Common.NotAvailable")
            };

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return new ApplySubmissionResult
            {
                Success = false,
                Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.Error.Unauthorized")
            };

        var applications = await GetApplicationsForJobAsync(customer.Id, model.ProductId, model.JobTitle);
        if (applications.Any(a => !JobApplicationStatuses.CanReapply(a.Status)))
        {
            var message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.AlreadyApplied");
            _notificationService.WarningNotification(message);
            return new ApplySubmissionResult { Success = false, Message = message };
        }

        if (string.IsNullOrWhiteSpace(model.JobTitle))
            ModelState.AddModelError(nameof(model.JobTitle), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.JobTitle.Required"));

        var allApplications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id) ?? new List<JobApplication>();
        var reusableResumeDownloadId = allApplications
            .OrderByDescending(a => a.CreatedOnUtc)
            .Where(a => a.ResumeDownloadId > 0)
            .Select(a => a.ResumeDownloadId)
            .FirstOrDefault();

        var jobRequirements = _jobRequirementService == null
            ? new JobRequirementsModel()
            : await _jobRequirementService.GetRequirementsAsync(model.ProductId);

        if (model.ResumeFile == null)
        {
            if (reusableResumeDownloadId > 0)
            {
                ModelState.Remove(nameof(model.ResumeFile));
            }
            else if (jobRequirements.ResumeRequired && !ModelState.ContainsKey(nameof(model.ResumeFile)))
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
            return new ApplySubmissionResult
            {
                Success = false,
                Message = ModelState.Values.SelectMany(value => value.Errors)
                    .Select(error => error.ErrorMessage)
                    .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
                    ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.Error")
            };

        if (jobRequirements.InterviewRequired)
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
                var message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.InterviewRequired");
                _notificationService.ErrorNotification(message);
                return new ApplySubmissionResult { Success = false, Message = message };
            }

            var highestScore = await _interviewSessionService.GetHighestScoreByCustomerIdAndProductIdAsync(customer.Id, model.ProductId);
            if (highestScore < jobRequirements.MinimumScore)
            {
                var message = string.Format(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.MinimumScoreNotReached"), jobRequirements.MinimumScore);
                _notificationService.ErrorNotification(message);
                return new ApplySubmissionResult { Success = false, Message = message };
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
        await _applicationService.SendApplicationSubmittedNotificationAsync(jobApplication, (await _workContext.GetWorkingLanguageAsync()).Id);
        return new ApplySubmissionResult
        {
            Success = true,
            Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.Success")
        };
    }

    protected async Task<bool> IsAuthorizedForEmployerActionsAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        return customer != null && (await _customerService.IsAdminAsync(customer) || customer.VendorId > 0);
    }

    protected async Task<SpecificationAttribute> GetSpecificationAttributeByNameAsync(params string[] names)
    {
        if (_specificationAttributeService == null)
            return null;

        foreach (var name in names.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var attributes = await _specificationAttributeService.GetSpecificationAttributesByNameAsync(name, 0, 1);
            var attribute = attributes.FirstOrDefault(specificationAttribute =>
                string.Equals(specificationAttribute.Name, name, StringComparison.OrdinalIgnoreCase));
            if (attribute != null)
                return attribute;
        }

        return null;
    }

    protected async Task PrepareVendorJobModelAsync(VendorJobModel model)
    {
        if (model == null || _specificationAttributeService == null)
            return;

        static IList<SelectListItem> BuildSelectList(IEnumerable<SpecificationAttributeOption> options, int? selectedId)
        {
            var items = new List<SelectListItem>
            {
                new() { Text = "Select", Value = string.Empty }
            };

            items.AddRange(options.Select(option => new SelectListItem
            {
                Text = option.Name,
                Value = option.Id.ToString(),
                Selected = selectedId.HasValue && option.Id == selectedId.Value
            }));

            return items;
        }

        var experienceAttribute = await GetSpecificationAttributeByNameAsync("Experience Level", "Experience");
        if (experienceAttribute != null)
            model.AvailableExperienceLevels = BuildSelectList(
                await _specificationAttributeService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(experienceAttribute.Id),
                model.ExperienceLevelOptionId);

        var workModeAttribute = await GetSpecificationAttributeByNameAsync("Work Mode", "Work Arrangement", "Work Type");
        if (workModeAttribute != null)
            model.AvailableWorkModes = BuildSelectList(
                await _specificationAttributeService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(workModeAttribute.Id),
                model.WorkModeOptionId);

        var employmentTypeAttribute = await GetSpecificationAttributeByNameAsync("Employment Type");
        if (employmentTypeAttribute != null)
            model.AvailableEmploymentTypes = BuildSelectList(
                await _specificationAttributeService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(employmentTypeAttribute.Id),
                model.EmploymentTypeOptionId);
    }

    protected async Task<int> ResolveCustomTextSpecificationOptionIdAsync(params string[] attributeNames)
    {
        var attribute = await GetSpecificationAttributeByNameAsync(attributeNames);
        if (attribute == null)
            return 0;

        var options = await _specificationAttributeService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(attribute.Id);
        return options.FirstOrDefault(option => string.Equals(option.Name, "Value", StringComparison.OrdinalIgnoreCase))?.Id
            ?? options.FirstOrDefault()?.Id
            ?? 0;
    }

    protected async Task InsertProductSpecificationAttributeAsync(int productId, int optionId, SpecificationAttributeType attributeType = SpecificationAttributeType.Option, string customValue = null, int displayOrder = 0)
    {
        if (_specificationAttributeService == null || optionId <= 0)
            return;

        await _specificationAttributeService.InsertProductSpecificationAttributeAsync(new ProductSpecificationAttribute
        {
            ProductId = productId,
            SpecificationAttributeOptionId = optionId,
            AttributeType = attributeType,
            CustomValue = customValue,
            ShowOnProductPage = true,
            AllowFiltering = false,
            DisplayOrder = displayOrder
        });
    }

    public async Task<IActionResult> EmployerApplications(ApplicationListModel model, int pageIndex = 0, int pageSize = 10)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var isEmployer = !await _customerService.IsAdminAsync(customer) && customer.VendorId > 0;

        pageSize = model.PageSize > 0 ? model.PageSize : pageSize;

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
            var questionScores = ParseQuestionScores(session?.QuestionScores);
            var reportSections = SplitReportSections(session?.ReportData);

            return new ApplicationModel
            {
                Id = a.Id,
                JobTitle = a.JobTitle,
                CandidateName = appCustomer != null ? (appCustomer.FirstName + " " + appCustomer.LastName).Trim() : await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown"),
                CandidateEmail = appCustomer?.Email ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown"),
                CandidatePhone = string.IsNullOrWhiteSpace(appCustomer?.Phone) ? "+1 555 201 001" : appCustomer.Phone,
                Status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{normalizedStatus}"),
                RawStatus = normalizedStatus,
                StatusComment = a.StatusComment,
                InterviewScore = session?.Score,
                InterviewReportUrl = session != null ? Url.Action("Report", "AIInterview", new { sessionId = session.Id }) : null,
                CreatedOn = a.CreatedOnUtc,
                AttemptCount = appSessions.Count,
                CompletedOn = session?.CompletedOnUtc,
                ChargeMode = session != null && session.SponsorInviteId > 0 ? "Company Sponsored" : "Candidate Paid",
                PromptSource = string.IsNullOrWhiteSpace(_aiInterviewSettings.Provider) && string.IsNullOrWhiteSpace(_aiInterviewSettings.Model)
                    ? "Resume-backed template"
                    : $"Provider: {_aiInterviewSettings.Provider}, Model: {_aiInterviewSettings.Model}",
                CoverMessage = !string.IsNullOrWhiteSpace(a.StatusComment)
                    ? a.StatusComment
                    : $"Applied for {a.JobTitle} with a resume-backed profile and interview history.",
                QuestionScores = session?.QuestionScores,
                QuestionScoreValues = questionScores,
                ReportSummary = reportSections.Summary,
                FeedbackSummary = reportSections.Feedback
            };
        }));

        if (model.OnlyWithInterviewScore)
            model.Applications = model.Applications.Where(application => application.InterviewScore.HasValue).ToList();

        model.Applications = (model.InterviewSort ?? "TopScorersFirst") switch
        {
            "LowestScorersFirst" => model.Applications.OrderBy(application => application.InterviewScore ?? decimal.MaxValue).ToList(),
            "LatestApplied" => model.Applications.OrderByDescending(application => application.CreatedOn).ToList(),
            _ => model.Applications.OrderByDescending(application => application.InterviewScore ?? decimal.MinValue).ToList()
        };

        if (!string.IsNullOrWhiteSpace(model.JobTitleOrKeyword))
            model.Applications = model.Applications
                .Where(application => (application.JobTitle ?? string.Empty).Contains(model.JobTitleOrKeyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

        model.TotalCount = model.Applications.Count;

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

        var model = new VendorJobModel();
        await PrepareVendorJobModelAsync(model);
        return View("~/Plugins/Misc.AIInterview/Views/VendorJobCreation.cshtml", model);
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
        {
            await PrepareVendorJobModelAsync(model);
            return View("~/Plugins/Misc.AIInterview/Views/VendorJobCreation.cshtml", model);
        }

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
            AvailableEndDateTimeUtc = model.ApplyUntilUtc?.Date,
            CreatedOnUtc = now,
            UpdatedOnUtc = now
        };

        await _productService.InsertProductAsync(product);

        await InsertProductSpecificationAttributeAsync(product.Id, model.ExperienceLevelOptionId ?? 0, displayOrder: 0);
        await InsertProductSpecificationAttributeAsync(product.Id, model.WorkModeOptionId ?? 0, displayOrder: 1);
        await InsertProductSpecificationAttributeAsync(product.Id, model.EmploymentTypeOptionId ?? 0, displayOrder: 2);

        var jobLocationOptionId = await ResolveCustomTextSpecificationOptionIdAsync("Job Location", "Location");
        if (!string.IsNullOrWhiteSpace(model.JobLocation))
        {
            await InsertProductSpecificationAttributeAsync(product.Id, jobLocationOptionId,
                SpecificationAttributeType.CustomText, model.JobLocation.Trim(), 3);
        }

        var salaryRangeOptionId = await ResolveCustomTextSpecificationOptionIdAsync("Salary Range", "Compensation");
        if (!string.IsNullOrWhiteSpace(model.SalaryRange))
        {
            await InsertProductSpecificationAttributeAsync(product.Id, salaryRangeOptionId,
                SpecificationAttributeType.CustomText, model.SalaryRange.Trim(), 4);
        }

        if (_jobInterviewExperienceService != null)
            await _jobInterviewExperienceService.EnsureInterviewDifficultyAttributeAsync(product);
        if (_jobRequirementService != null)
            await _jobRequirementService.SaveRequirementsAsync(product, model.ResumeRequired, model.InterviewRequired, model.MinimumScore);
        var seName = await _urlRecordService.ValidateSeNameAsync(product, string.Empty, product.Name, true);
        await _urlRecordService.SaveSlugAsync(product, seName, 0);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Success"));
        return RedirectToRoute(AIInterviewDefaults.VendorScoreboardRouteName);
    }
}
