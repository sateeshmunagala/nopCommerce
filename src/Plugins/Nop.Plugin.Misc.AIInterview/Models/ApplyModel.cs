using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.AIInterview.Models;

public record ApplyModel : BaseNopModel
{
    public int ProductId { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Apply.JobTitle")]
    public string JobTitle { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Apply.ResumeFile")]
    public IFormFile ResumeFile { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Apply.PreviousResume")]
    public int SelectedResumeDownloadId { get; set; }

    public bool ResumeRequired { get; set; }

    public IList<SelectListItem> AvailableResumes { get; set; } = new List<SelectListItem>();

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Apply.InterviewRecord")]
    public int SelectedInterviewSessionId { get; set; }

    public bool InterviewRecordRequired { get; set; }

    public IList<SelectListItem> AvailableInterviewSessions { get; set; } = new List<SelectListItem>();
}
