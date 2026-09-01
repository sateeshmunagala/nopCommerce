namespace Nop.Plugin.Misc.JobSupport.Services.Migration;

public partial interface IJobSupportBackfillService
{
    Task<BackfillStepResult> BackfillProfilesAsync(int batchSize, CancellationToken cancellationToken);
    Task<BackfillStepResult> BackfillSkillsAsync(int batchSize, CancellationToken cancellationToken);
    Task<BackfillStepResult> BackfillRelationshipsAsync(int batchSize, CancellationToken cancellationToken);
    Task<BackfillStepResult> BackfillViewsAndRevealsAsync(int batchSize, CancellationToken cancellationToken);
    Task<BackfillStepResult> BackfillSubscriptionsAsync(int batchSize, CancellationToken cancellationToken);
}

public sealed record BackfillStepResult
{
    public string MigrationName { get; init; }
    public int LastProcessedId { get; init; }
    public long ProcessedCount { get; init; }
    public long SkippedCount { get; init; }
    public long FailedCount { get; init; }
    public bool Completed { get; init; }
    public IReadOnlyList<string> ErrorLog { get; init; } = Array.Empty<string>();
}
