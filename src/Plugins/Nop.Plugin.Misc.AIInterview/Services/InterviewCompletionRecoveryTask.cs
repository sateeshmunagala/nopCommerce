using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class InterviewCompletionRecoveryTask : IScheduleTask
{
    public static readonly TimeSpan ProcessingLeaseTimeout = TimeSpan.FromMinutes(10);
    public const int BatchSize = 20;

    private readonly IInterviewSessionService _sessionService;
    private readonly IInterviewRuntimeService _runtimeService;

    public InterviewCompletionRecoveryTask(
        IInterviewSessionService sessionService,
        IInterviewRuntimeService runtimeService)
    {
        _sessionService = sessionService;
        _runtimeService = runtimeService;
    }

    public async Task ExecuteAsync()
    {
        var staleProcessingBeforeUtc = DateTime.UtcNow.Subtract(ProcessingLeaseTimeout);
        var sessions = await _sessionService.GetCompletionWorkSessionsAsync(staleProcessingBeforeUtc, BatchSize);

        foreach (var session in sessions)
            await _runtimeService.ProcessCompletionWorkAsync(session.Id);
    }
}
