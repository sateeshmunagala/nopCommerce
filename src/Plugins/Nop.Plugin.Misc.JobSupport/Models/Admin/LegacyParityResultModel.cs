using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Admin;

public partial record LegacyParityResultModel : BaseNopModel
{
    public ProfileQueryDiagnosticResult Diagnostic { get; set; } = new();
    public IList<LegacyParityProfilePresenceModel> Profiles { get; set; } = new List<LegacyParityProfilePresenceModel>();
}

public partial record LegacyParityProfilePresenceModel : BaseNopModel
{
    public int ProfileId { get; set; }
    public bool HasPhone { get; set; }
    public bool HasEmail { get; set; }
}
