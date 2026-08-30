using Nop.Web.Framework.Models;
using Nop.Plugin.Misc.AIInterview;

namespace Nop.Plugin.Misc.AIInterview.Models;

public record InterviewRuntimeModel : BaseNopModel
{
    public int SessionId { get; set; }
    public int ProductId { get; set; }
    public int QuestionCount { get; set; }
    public string Token { get; set; }
    public string SessionKey { get; set; }
    public string ProductName { get; set; }
    public string Difficulty { get; set; }
    public string CandidateName { get; set; }
    public bool IsPracticeInterview { get; set; }
    public string PracticeSkill { get; set; }
    public string RuntimeTopic { get; set; }
    public string CurrentQuestion { get; set; }
    public string ReportUrl { get; set; }
    public DateTime? TokenExpiryUtc { get; set; }
    public decimal Score { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsMockMode { get; set; }
    public string SupportPhoneNumber { get; set; }
    public IEnumerable<InterviewTurnViewModel> Turns { get; set; } = new List<InterviewTurnViewModel>();
    public RuntimeClientSettingsModel ClientSettings { get; set; } = new();
}

public record InterviewTurnViewModel
{
    public int TurnId { get; set; }
    public int SequenceNumber { get; set; }
    public string QuestionText { get; set; }
    public string AnswerText { get; set; }
    public decimal? Score { get; set; }
    public decimal? TechnicalScore { get; set; }
    public decimal? CommunicationScore { get; set; }
    public decimal? ProfessionalismScore { get; set; }
    public decimal? PositiveAttitudeScore { get; set; }
    public string Feedback { get; set; }
    public DateTime AskedOnUtc { get; set; }
    public DateTime? AnsweredOnUtc { get; set; }
}

public record RuntimeClientSettingsModel
{
    public int QuestionCount { get; set; }
    public string SubmitAnswerUrl { get; set; }
    public string CompleteInterviewUrl { get; set; }
    public string RefreshTokenUrl { get; set; }
    public string StopInterviewUrl { get; set; }
    public string FeedbackUrl { get; set; }
    public string TranscriptUrl { get; set; }
    public string SpeechTokenUrl { get; set; }
    public string SpeechUsageUrl { get; set; }
    public string RecordingUploadUrl { get; set; }
    public string BeginInterviewUrl { get; set; }
    public string PrepareInterviewUrl { get; set; }
    public string CompletionStatusUrl { get; set; }
    public string AcknowledgeGuidelinesUrl { get; set; }
    public string RuntimeClientEventUrl { get; set; }
    public bool CreditEligible { get; set; } = true;
    public string CreditWarningMessage { get; set; }
    public string PricingUrl { get; set; } = "/pricing";
    public string SpeechRegion { get; set; }
    public string SpeechVoiceName { get; set; }
    public string ProductName { get; set; }
    public string FinalCompletionSpeech { get; set; }
    public string Token { get; set; }
    public string ReportUrl { get; set; }
    public DateTime? TokenExpiryUtc { get; set; }
    public bool SpeechAvailable { get; set; }
    public bool RecordingAvailable { get; set; }
    public int RecordingUploadMaxMb { get; set; } = AIInterviewDefaults.DefaultRecordingUploadMaxMb;
    public int RecordingVideoBitsPerSecond { get; set; } = AIInterviewDefaults.DefaultRecordingVideoBitsPerSecond;
    public int RecordingAudioBitsPerSecond { get; set; } = AIInterviewDefaults.DefaultRecordingAudioBitsPerSecond;
    public string RecordingSourceMode { get; set; } = AIInterviewDefaults.DefaultRecordingSourceMode;
    public int RecordingUploadTimeoutMs { get; set; } = AIInterviewDefaults.DefaultRecordingUploadTimeoutMs;
    public int FinalizationWaitTimeoutMs { get; set; } = AIInterviewDefaults.DefaultFinalizationWaitTimeoutMs;
}

public record StartInterviewResponseModel
{
    public bool Success { get; init; }
    public string Message { get; init; }
    public string RuntimeUrl { get; init; }
    public string SessionKey { get; init; }
    public string Token { get; init; }
}

public record InterviewCreditEligibilityResult
{
    public bool Eligible { get; init; }
    public bool AlreadyCharged { get; init; }
    public int ChargeCustomerId { get; init; }
    public string LedgerSource { get; init; }
}

public record InterviewCreditChargeResult : InterviewCreditEligibilityResult
{
    public bool ChargedNow { get; init; }
}

public record PrepareInterviewResponseModel
{
    public bool Success { get; init; }
    public bool Ready { get; init; }
    public string Message { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public int ExpectedQuestionCount { get; init; }
    public int PersistedQuestionCount { get; init; }
}

public record SubmitInterviewAnswerRequest
{
    public string Token { get; init; }
    public string Answer { get; init; }
    public int? TurnId { get; init; }
    public int? SequenceNumber { get; init; }
    public int SpeechRecognitionCharacters { get; init; }
    public long SpeechRecognitionDurationMs { get; init; }
    public string SpeechRecognitionClientEventId { get; init; }
}

public record SubmitInterviewAnswerResponse
{
    public bool Success { get; init; }
    public bool IsTerminated { get; init; }
    public string Completion { get; init; }
    public string ReportUrl { get; init; }
    public bool ReportReady { get; init; }
    public string Question { get; init; }
    public InterviewTurnViewModel Turn { get; init; }
    public bool Interrupted { get; init; }
    public decimal Score { get; init; }
    public string Feedback { get; init; }
    public string Message { get; init; }
    public bool ReportGenerationInProgress { get; init; }
    public bool ReportGenerationFailed { get; init; }
    public int EstimatedWaitSeconds { get; init; }
    public IList<InterviewTurnViewModel> Turns { get; init; } = new List<InterviewTurnViewModel>();
}

public record CompleteInterviewRequest
{
    public string Token { get; init; }
    public string Reason { get; init; }
}

public record CompleteInterviewResponse
{
    public bool Success { get; init; }
    public bool IsTerminated { get; init; }
    public decimal Score { get; init; }
    public string Feedback { get; init; }
    public string Message { get; init; }
    public string Completion { get; init; }
    public string ReportUrl { get; init; }
    public bool ReportReady { get; init; }
    public bool ReportGenerationInProgress { get; init; }
    public bool ReportGenerationFailed { get; init; }
    public int EstimatedWaitSeconds { get; init; }
    public IList<InterviewTurnViewModel> Turns { get; init; } = new List<InterviewTurnViewModel>();
}

public record CompletionStatusResponseModel
{
    public bool Success { get; init; }
    public string Message { get; init; }
    public bool ReportReady { get; init; }
    public bool ReportGenerationInProgress { get; init; }
    public bool ReportGenerationFailed { get; init; }
    public string ReportUrl { get; init; }
    public int EstimatedWaitSeconds { get; init; }
}

public record RecordingUploadResponseModel
{
    public bool Success { get; init; }
    public string Message { get; init; }
    public string RecordingUrl { get; init; }
    public string ReasonCode { get; init; }
}

public record SpeechTokenResponseModel
{
    public bool Success { get; init; } = true;
    public string Message { get; init; }
    public string FailureKind { get; init; }
    public int? AzureStatusCode { get; init; }
    public string AzureReasonPhrase { get; init; }
    public string DiagnosticMessage { get; init; }
    public string Token { get; init; }
    public string Region { get; init; }
    public int ExpiresInSeconds { get; init; }
}

public record SpeechSynthesisUsageRequest
{
    public string Token { get; init; }
    public int? TurnId { get; init; }
    public int? SequenceNumber { get; init; }
    public string Purpose { get; init; }
    public int SpeechSynthesisCharacters { get; init; }
    public string ClientEventId { get; init; }
}

public record InterviewReportModel : BaseNopModel
{
    public int SessionId { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string SessionKey { get; set; }
    public string Token { get; set; }
    public string ProductName { get; set; }
    public string JobTitle { get; set; }
    public string Difficulty { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime ReportDateUtc { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
    public decimal Score { get; set; }
    public bool IsCompleted { get; set; }
    public string QuestionScores { get; set; }
    public IList<decimal> ParsedQuestionScores { get; set; } = new List<decimal>();
    public string ReportData { get; set; }
    public string RecordingUrl { get; set; }
    public string RecordingShareUrl { get; set; }
    public string ReportShareUrl { get; set; }
    public IList<InterviewTurnViewModel> Turns { get; set; } = new List<InterviewTurnViewModel>();
    public string CandidateName { get; set; }
    public string InterviewType { get; set; }
    public string SkillContext { get; set; }
    public bool ResumeUsed { get; set; }
}
