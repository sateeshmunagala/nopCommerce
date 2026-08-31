using Nop.Core;

namespace Nop.Plugin.Misc.JobSupport.Domain;

public partial class JobSupportMigrationCheckpoint : BaseEntity
{
    public string MigrationName { get; set; }
    public int LastProcessedId { get; set; }
    public long ProcessedCount { get; set; }
    public long SkippedCount { get; set; }
    public long FailedCount { get; set; }
    public long MismatchCount { get; set; }
    public string Status { get; set; }
    public string ErrorLog { get; set; }
    public DateTime? LastExecutedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
