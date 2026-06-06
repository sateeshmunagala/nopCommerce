using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Events;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Events;

namespace Nop.Plugin.Misc.AIInterview.Services.Events;

public class MockAiInterviewCompletedEventConsumer : IConsumer<MockAiInterviewCompletedEvent>
{
    private readonly IInterviewSessionService _interviewSessionService;
    private readonly IWorkContext _workContext;

    public MockAiInterviewCompletedEventConsumer(
        IInterviewSessionService interviewSessionService,
        IWorkContext workContext)
    {
        _interviewSessionService = interviewSessionService;
        _workContext = workContext;
    }

    public async Task HandleEventAsync(MockAiInterviewCompletedEvent eventMessage)
    {
        await _interviewSessionService.SendInterviewCompletionNotificationAsync(eventMessage.Session, (await _workContext.GetWorkingLanguageAsync()).Id);
    }
}
