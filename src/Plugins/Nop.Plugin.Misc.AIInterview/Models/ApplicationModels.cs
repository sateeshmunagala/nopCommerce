using Microsoft.AspNetCore.Http;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

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
    public int TotalCount { get; set; }

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
    public IList<ApplicationModel> RecentApplications { get; set; } = new List<ApplicationModel>();
}

public record VendorJobModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.Name")]
    public string Name { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.ShortDescription")]
    public string ShortDescription { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.FullDescription")]
    public string FullDescription { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.Sku")]
    public string Sku { get; set; }

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

    [NopResourceDisplayName("Plugins.Misc.AIInterview.VendorJobCreation.ApplyUntilUtc")]
    public DateTime? ApplyUntilUtc { get; set; }

    public IList<SelectListItem> AvailableExperienceLevels { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableWorkModes { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableEmploymentTypes { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableJobLocations { get; set; } = new List<SelectListItem>();
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
}
