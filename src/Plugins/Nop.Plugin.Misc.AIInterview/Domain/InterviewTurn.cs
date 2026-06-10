using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class InterviewTurn : BaseEntity
{
    public int InterviewSessionId { get; set; }
    public int SequenceNumber { get; set; }
    public int QuestionId { get; set; }
    public string QuestionText { get; set; }
    public string AnswerText { get; set; }
    public decimal? Score { get; set; }
    public string Feedback { get; set; }
    public string RubricJson { get; set; }
    public string RawAIResponseJson { get; set; }
    public DateTime AskedOnUtc { get; set; }
    public DateTime? AnsweredOnUtc { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}
