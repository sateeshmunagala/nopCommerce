using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Events;

public class MockAiInterviewCompletedEvent
{
    public InterviewSession Session { get; }
    public int LanguageId { get; }

    public MockAiInterviewCompletedEvent(InterviewSession session, int languageId = 0)
    {
        Session = session;
        LanguageId = languageId;
    }
}
