using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Services.Migration;

public partial interface IJobSupportReconciliationService
{
    Task<IReadOnlyList<JobSupportMigrationCheckpoint>> GetCheckpointsAsync(CancellationToken cancellationToken);
    Task<ReconciliationResult> ReconcileAsync(CancellationToken cancellationToken);
    Task<string> ExportSanitizedMismatchesAsync(CancellationToken cancellationToken);
}

public sealed record ReconciliationResult
{
    public DateTime ExecutedOnUtc { get; init; }
    public long MismatchCount { get; init; }
    public IReadOnlyList<string> SanitizedMismatchIdentifiers { get; init; } = Array.Empty<string>();
}
