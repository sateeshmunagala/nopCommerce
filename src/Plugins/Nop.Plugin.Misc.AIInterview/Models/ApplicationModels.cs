using Microsoft.AspNetCore.Http;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.AIInterview.Models;

public record ApplicationListModel : BaseNopModel
{
    public ApplicationListModel()
    {
        Applications = new List<ApplicationModel>();
    }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Employer.Applications.Candidate")]
    public string CandidateNameOrEmail { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Employer.Applications.Status")]
    public string Status { get; set; }

    public decimal? MinScore { get; set; }
    public decimal? MaxScore { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool SortByScore { get; set; }
    public string SortOrder { get; set; }

    public IList<ApplicationModel> Applications { get; set; }
}

public record ApplicationModel : BaseNopModel
{
    public int Id { get; set; }
    public string JobTitle { get; set; }
    public string CandidateName { get; set; }
    public string CandidateEmail { get; set; }
    public string Status { get; set; }
    public string StatusComment { get; set; }
    public decimal? InterviewScore { get; set; }
    public string QuestionScores { get; set; }
    public string InterviewReportUrl { get; set; }
    public DateTime CreatedOn { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LatestScoreDate { get; set; }
    public string ChargeMode { get; set; }
    public string PromptSource { get; set; }
    public string RawStatus { get; set; }
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
}

public record RuntimeErrorModel : BaseNopModel
{
    public string Message { get; set; }
    public int StatusCode { get; set; }
}
