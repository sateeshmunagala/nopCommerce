using Nop.Plugin.Misc.JobSupport.Domain.Enums;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Admin.Migration;

public partial record MigrationStatusModel : BaseNopModel
{
    public string SchemaVersion { get; set; }
    public DataAccessMode ReadMode { get; set; }
    public DataAccessMode WriteMode { get; set; }
    public DateTime? LastExecutionOnUtc { get; set; }
    public long MismatchCount { get; set; }
    public IList<MigrationStepStatusModel> Steps { get; set; } = new List<MigrationStepStatusModel>();
}

public partial record MigrationStepStatusModel : BaseNopModel
{
    public string Name { get; set; }
    public string Status { get; set; }
    public int LastProcessedId { get; set; }
    public long ProcessedCount { get; set; }
    public long SkippedCount { get; set; }
    public long FailedCount { get; set; }
    public long MismatchCount { get; set; }
    public DateTime? LastExecutionOnUtc { get; set; }
}
