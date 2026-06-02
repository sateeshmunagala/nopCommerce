using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class InterviewSession : BaseEntity
{
    public int CustomerId { get; set; }
    public int JobApplicationId { get; set; }
    public string SessionKey { get; set; }
    public string ReportData { get; set; }
    public decimal Score { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
}
