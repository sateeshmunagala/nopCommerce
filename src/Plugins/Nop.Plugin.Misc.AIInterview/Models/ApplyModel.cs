using Microsoft.AspNetCore.Http;
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
}
