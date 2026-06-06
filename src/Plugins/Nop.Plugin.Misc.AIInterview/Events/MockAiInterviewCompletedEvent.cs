using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Events;

public class MockAiInterviewCompletedEvent
{
    public InterviewSession Session { get; }

    public MockAiInterviewCompletedEvent(InterviewSession session)
    {
        Session = session;
    }
}
