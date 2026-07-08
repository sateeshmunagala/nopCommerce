using Nop.Web.Framework.Models;

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
    public string CurrentQuestion { get; set; }
    public string ReportUrl { get; set; }
    public DateTime? TokenExpiryUtc { get; set; }
    public decimal Score { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsMockMode { get; set; }
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
    public string TranscriptUrl { get; set; }
    public string SpeechTokenUrl { get; set; }
    public string RecordingUploadUrl { get; set; }
    public string BeginInterviewUrl { get; set; }
    public string AcknowledgeGuidelinesUrl { get; set; }
    public string SpeechRegion { get; set; }
    public string SpeechVoiceName { get; set; }
    public string ProductName { get; set; }
    public string Token { get; set; }
    public string ReportUrl { get; set; }
    public DateTime? TokenExpiryUtc { get; set; }
    public bool SpeechAvailable { get; set; }
    public bool RecordingAvailable { get; set; }
}

public record StartInterviewResponseModel
{
    public bool Success { get; init; }
    public string Message { get; init; }
    public string RuntimeUrl { get; init; }
    public string SessionKey { get; init; }
    public string Token { get; init; }
}

public record SubmitInterviewAnswerRequest
{
    public string Token { get; init; }
    public string Answer { get; init; }
}

public record SubmitInterviewAnswerResponse
{
    public bool Success { get; init; }
    public bool IsTerminated { get; init; }
    public string Completion { get; init; }
    public string ReportUrl { get; init; }
    public string Question { get; init; }
    public InterviewTurnViewModel Turn { get; init; }
    public bool Interrupted { get; init; }
    public decimal Score { get; init; }
    public string Feedback { get; init; }
    public string Message { get; init; }
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
    public IList<InterviewTurnViewModel> Turns { get; init; } = new List<InterviewTurnViewModel>();
}

public record RecordingUploadResponseModel
{
    public bool Success { get; init; }
    public string Message { get; init; }
    public string RecordingUrl { get; init; }
}

public record SpeechTokenResponseModel
{
    public string Token { get; init; }
    public string Region { get; init; }
    public int ExpiresInSeconds { get; init; }
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
    public IList<InterviewTurnViewModel> Turns { get; set; } = new List<InterviewTurnViewModel>();
}
