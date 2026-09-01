using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Admin;

public partial record WorkflowDiagnosticsModel : BaseNopModel
{
    public IList<WorkflowDiagnosticItemModel> Configuration { get; set; } = new List<WorkflowDiagnosticItemModel>();
    public IList<WorkflowActivityModel> Activities { get; set; } = new List<WorkflowActivityModel>();
}

public partial record WorkflowDiagnosticItemModel : BaseNopModel
{
    public string Name { get; set; }
    public string Value { get; set; }
}

public partial record WorkflowActivityModel : BaseNopModel
{
    public DateTime CreatedOnUtc { get; set; }
    public string ActivityType { get; set; }
    public string EntityName { get; set; }
    public int? EntityId { get; set; }
}
