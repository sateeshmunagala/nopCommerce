using Microsoft.AspNetCore.Http;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Models.Catalog;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Models;

public record ApplicationListModel : BaseNopModel
{
    public ApplicationListModel()
    {
        Applications = new List<ApplicationModel>();
    }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Employer.Applications.Candidate")]
    public string CandidateNameOrEmail { get; set; }

    public string JobTitleOrKeyword { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Employer.Applications.Status")]
    public string Status { get; set; }

    public decimal? MinScore { get; set; }
    public decimal? MaxScore { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool SortByScore { get; set; }
    public string SortOrder { get; set; }
    public string InterviewSort { get; set; } = "TopScorersFirst";
    public bool OnlyWithInterviewScore { get; set; }
    public int PageSize { get; set; } = 20;
    public int Page { get; set; } = 1;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }

    public IList<ApplicationModel> Applications { get; set; }
}

public record ApplicationModel : BaseNopModel
{
    public int Id { get; set; }
    public int InterviewSessionId { get; set; }
    public string JobTitle { get; set; }
    public string CandidateName { get; set; }
    public string CandidateEmail { get; set; }
    public string CandidatePhone { get; set; }
    public string Status { get; set; }
    public string StatusComment { get; set; }
    public decimal? InterviewScore { get; set; }
    public string QuestionScores { get; set; }
    public IList<decimal> QuestionScoreValues { get; set; } = new List<decimal>();
    public string InterviewReportUrl { get; set; }
    public string InterviewReportPanelUrl { get; set; }
    public int ResumeDownloadId { get; set; }
    public bool HasResume { get; set; }
    public string ResumeDownloadUrl { get; set; }
    public string RecordingUrl { get; set; }
    public string RecordingShareUrl { get; set; }
    public string ProductUrl { get; set; }
    public DateTime CreatedOn { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LatestScoreDate { get; set; }
    public DateTime? CompletedOn { get; set; }
    public string ChargeMode { get; set; }
    public string PromptSource { get; set; }
    public string RawStatus { get; set; }
    public string CoverMessage { get; set; }
    public string ReportSummary { get; set; }
    public string FeedbackSummary { get; set; }
    public IList<InterviewTurnViewModel> Turns { get; set; } = new List<InterviewTurnViewModel>();
}

public record InterviewHistoryItemModel : BaseNopModel
{
    public int SessionId { get; set; }
    public string JobTitle { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
    public string Status { get; set; }
    public decimal Score { get; set; }
    public string InterviewReportUrl { get; set; }
    public string InterviewReportPanelUrl { get; set; }
    public string RecordingUrl { get; set; }
    public string RecordingShareUrl { get; set; }
}

public record SavedJobsListModel : BaseNopModel
{
    public SavedJobsListModel()
    {
        Products = new List<ProductOverviewModel>();
    }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 5;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public IList<ProductOverviewModel> Products { get; set; }
}

public record MockInterviewHistoryListModel : BaseNopModel
{
    public MockInterviewHistoryListModel()
    {
        Items = new List<InterviewHistoryItemModel>();
    }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 5;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public IList<InterviewHistoryItemModel> Items { get; set; }
}

public record MyActivityCreditLedgerRowModel : BaseNopModel
{
    public DateTime CreatedOnUtc { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedOnDisplay { get; set; }
    public string Type { get; set; }
    public decimal Credits { get; set; }
    public decimal BalanceAfter { get; set; }
    public string JobProduct { get; set; }
    public string Source { get; set; }
    public string Description { get; set; }
    public string CreditsDisplay { get; set; }
    public string BalanceAfterDisplay { get; set; }
}

public record CreditActivityModel : BaseNopModel
{
    public decimal CurrentBalance { get; set; }
    public decimal TotalDeposited { get; set; }
    public decimal TotalWithdrawn { get; set; }
    public string CurrentBalanceDisplay { get; set; }
    public string TotalDepositedDisplay { get; set; }
    public string TotalWithdrawnDisplay { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 5;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public IList<MyActivityCreditLedgerRowModel> Entries { get; set; } = new List<MyActivityCreditLedgerRowModel>();
}

public record MyActivityPagerModel : BaseNopModel
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public string PageQueryParameterName { get; set; } = "page";
    public string PageSizeQueryParameterName { get; set; } = "pageSize";
    public IDictionary<string, string> RouteValues { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public record MyActivityPageModel : BaseNopModel
{
    public MyActivityPageModel()
    {
        AppliedJobs = new ApplicationListModel();
        SavedJobs = new SavedJobsListModel();
        MockInterviews = new MockInterviewHistoryListModel();
        Credits = new CreditActivityModel();
    }

    public string ActiveTab { get; set; } = AIInterviewDefaults.MyActivityAppliedJobsTabKey;
    public ApplicationListModel AppliedJobs { get; set; }
    public SavedJobsListModel SavedJobs { get; set; }
    public MockInterviewHistoryListModel MockInterviews { get; set; }
    public CreditActivityModel Credits { get; set; }
    public decimal WalletBalance { get; set; }
}

public record UpdateStatusModel : BaseNopModel
{
    public int Id { get; set; }
    public string Status { get; set; }
    public string StatusComment { get; set; }
}

public record VendorScoreboardModel : BaseNopModel
{
    public int TotalJobs { get; set; }
    public int TotalApplications { get; set; }
    public int CompletedInterviews { get; set; }
    public int ShortlistedApplications { get; set; }
    public int ActiveFlaggedViolations { get; set; }
    public decimal? AverageScore { get; set; }
    public decimal? HighestScore { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public IList<ApplicationModel> RecentApplications { get; set; } = new List<ApplicationModel>();
}

public record VendorJobModel : BaseNopModel
{
    public int Id { get; set; }

    public bool IsEditMode { get; set; }

    public string PublicJobUrl { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.Name")]
    public string Name { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.ShortDescription")]
    public string ShortDescription { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.FullDescription")]
    public string FullDescription { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.Sku")]
    public string Sku { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.NumberOfPositions")]
    public int? NumberOfPositions { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.Published")]
    public bool Published { get; set; } = true;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.ResumeRequired")]
    public bool ResumeRequired { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.InterviewRequired")]
    public bool InterviewRequired { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.MinimumScore")]
    public decimal MinimumScore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.QuestionCount")]
    public int QuestionCount { get; set; } = 3;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.InterviewMode")]
    public string InterviewMode { get; set; } = AIInterviewDefaults.InterviewModeAiResumeBased;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.QuestionSet")]
    public int QuestionSetId { get; set; }

    public string QuestionSetWorkflow { get; set; } = AIInterviewDefaults.QuestionSetWorkflowExisting;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.QuestionSetName")]
    public string QuestionSetName { get; set; }

    public IList<FixedQuestionItemModel> QuestionItems { get; set; } = new List<FixedQuestionItemModel>();

    public IList<FixedQuestionSetModel> AvailableQuestionSets { get; set; } = new List<FixedQuestionSetModel>();

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.ExperienceLevel")]
    public int? ExperienceLevelOptionId { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.WorkMode")]
    public int? WorkModeOptionId { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.EmploymentType")]
    public int? EmploymentTypeOptionId { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.JobLocation")]
    public int? JobLocationOptionId { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.SalaryRange")]
    public string SalaryRange { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.SalaryMinCtcPa")]
    public decimal? SalaryMinCtcPa { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.SalaryMaxCtcPa")]
    public decimal? SalaryMaxCtcPa { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.ApplyUntilUtc")]
    public DateTime? ApplyUntilUtc { get; set; }

    public IList<SelectListItem> AvailableSalaryLpaOptions { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableExperienceLevels { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableWorkModes { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableEmploymentTypes { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableJobLocations { get; set; } = new List<SelectListItem>();
}

public record FixedQuestionSetModel : BaseNopEntityModel
{
    public string Name { get; set; }
    public bool IsActive { get; set; }
    public IList<FixedQuestionItemModel> Items { get; set; } = new List<FixedQuestionItemModel>();
}

public record FixedQuestionItemModel : BaseNopEntityModel
{
    public int SequenceNumber { get; set; }
    public string QuestionText { get; set; }
    public string RubricHint { get; set; }
    public string ExpectedSignalNotes { get; set; }
    public bool IsActive { get; set; } = true;
}

public record RuntimeErrorModel : BaseNopModel
{
    public string Message { get; set; }
    public int StatusCode { get; set; }
    public string RestartUrl { get; set; }
}

public record ApplySubmissionResult
{
    public bool Success { get; init; }
    public string Message { get; init; }
    public bool RequiresLogin { get; init; }
    public string RedirectUrl { get; init; }
    public int StatusCode { get; init; }
}

public record EmployerDashboardJobModel : BaseNopModel
{
    public int ProductId { get; set; }
    public string JobTitle { get; set; }
    public bool Published { get; set; }
    public string SalaryRange { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public int ApplicationCount { get; set; }
}

public record EmployerDashboardJobsTabModel : BaseNopModel
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public IList<EmployerDashboardJobModel> Jobs { get; set; } = new List<EmployerDashboardJobModel>();
}

public record EmployerDashboardInvitesTabModel : BaseNopModel
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public IList<SponsorInvite> Invites { get; set; } = new List<SponsorInvite>();
    public IDictionary<int, string> InviteStatuses { get; set; } = new Dictionary<int, string>();
    public IList<SelectListItem> AvailableProducts { get; set; } = new List<SelectListItem>();
    public DateTime? ExpiryDateUtc { get; set; }
    public decimal CreditBalance { get; set; }
    public string CreditBalanceDisplay { get; set; }
}

public record EmployerDashboardPageModel : BaseNopModel
{
    public string ActiveTab { get; set; } = AIInterviewDefaults.EmployerDashboardOverviewTabKey;
    public VendorScoreboardModel Overview { get; set; } = new();
    public EmployerDashboardJobsTabModel Jobs { get; set; } = new();
    public ApplicationListModel Applications { get; set; } = new();
    public EmployerDashboardInvitesTabModel Invites { get; set; } = new();
}
