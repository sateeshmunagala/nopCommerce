using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Public;

public record ProfileFilterModel : BaseNopModel
{
    public int? ProfileTypeId { get; set; }
    public int? PrimaryTechnologyId { get; set; }
    public int? SecondaryTechnologyId { get; set; }
    public int? AvailabilityId { get; set; }
    public int? RelevantExperienceId { get; set; }
    public int? MotherTongueId { get; set; }
    public int SortOrder { get; set; }
    public int PageNumber { get; set; } = 1;
    public IList<SelectListItem> ProfileTypes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> PrimaryTechnologies { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> SecondaryTechnologies { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> Availabilities { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> RelevantExperiences { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> MotherTongues { get; set; } = new List<SelectListItem>();
}
