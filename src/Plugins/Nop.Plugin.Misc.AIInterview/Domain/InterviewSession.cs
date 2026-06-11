using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class InterviewSession : BaseEntity
{
    public int CustomerId { get; set; }
    public int JobApplicationId { get; set; }
    public string SessionKey { get; set; }
    public int ProductId { get; set; }
    public string Difficulty { get; set; }
    public string Token { get; set; }
    public DateTime? TokenExpiryUtc { get; set; }
    public bool IsActive { get; set; }
    public string RecordingUrl { get; set; }
    public string ReportData { get; set; }
    public string QuestionScores { get; set; }
    public decimal Score { get; set; }
    public int SponsorInviteId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? StartedOnUtc { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
}
