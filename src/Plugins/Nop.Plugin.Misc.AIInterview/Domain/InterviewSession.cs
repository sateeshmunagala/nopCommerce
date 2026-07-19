using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class InterviewSession : BaseEntity
{
    public int CustomerId { get; set; }
    public int JobApplicationId { get; set; }
    public string InterviewType { get; set; }
    public string SessionKey { get; set; }
    public int ProductId { get; set; }
    public int SourceProductId { get; set; }
    public string Difficulty { get; set; }
    public int ResumeDownloadId { get; set; }
    public string ResumeProfileJson { get; set; }
    public DateTime? ResumeProfileGeneratedOnUtc { get; set; }
    public string ResumeProfileError { get; set; }
    public string SelectedProductAttributesJson { get; set; }
    public string Token { get; set; }
    public DateTime? TokenExpiryUtc { get; set; }
    public bool IsActive { get; set; }
    public string RecordingUrl { get; set; }
    public string RecordingShareToken { get; set; }
    public bool RecordingShareEnabled { get; set; }
    public DateTime? RecordingShareCreatedOnUtc { get; set; }
    public string ReportData { get; set; }
    public string QuestionScores { get; set; }
    public decimal Score { get; set; }
    public int QuestionCount { get; set; }
    public int TotalPromptTokens { get; set; }
    public int TotalCompletionTokens { get; set; }
    public decimal TotalOpenAiCostUsd { get; set; }
    public int TotalSpeechRecognitionCharacters { get; set; }
    public int TotalSpeechSynthesisCharacters { get; set; }
    public long TotalSpeechDurationMs { get; set; }
    public decimal TotalSpeechCostUsd { get; set; }
    public decimal TotalAzureCostUsd { get; set; }
    public int SponsorInviteId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? StartedOnUtc { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
}
