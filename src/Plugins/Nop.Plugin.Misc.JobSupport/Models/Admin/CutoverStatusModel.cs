using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Plugin.Misc.JobSupport.Domain.Enums;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Admin;

public partial record CutoverStatusModel : BaseNopModel
{
    public DataAccessMode ReadMode { get; set; }
    public DataAccessMode WriteMode { get; set; }
    public string ProviderStatus { get; set; }
    public bool LegacyProfileSearchPresent { get; set; }
    public bool LegacyRelationshipProcedurePresent { get; set; }
    public bool PluginProfileSearchPresent { get; set; }
    public bool PluginRelationshipProcedurePresent { get; set; }
    public string PluginProcedureVersion { get; set; }
    public long MismatchCount { get; set; }
    public DateTime? LastComparisonOnUtc { get; set; }
    public long? LegacyDurationMilliseconds { get; set; }
    public long? PluginDurationMilliseconds { get; set; }
    public bool RollbackReady { get; set; }
    public IList<SelectListItem> ReadModes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> WriteModes { get; set; } = new List<SelectListItem>();
}
