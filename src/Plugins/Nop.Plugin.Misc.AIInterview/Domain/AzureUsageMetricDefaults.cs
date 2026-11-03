namespace Nop.Plugin.Misc.AIInterview.Domain;

public static class AzureUsageMetricDefaults
{
    public const string ProviderAzureOpenAi = "AzureOpenAI";
    public const string ProviderAzureSpeech = "AzureSpeech";

    public const string UsageKindOpenAiQuestionGeneration = "OpenAI.QuestionGeneration";
    public const string UsageKindOpenAiAnswerScoring = "OpenAI.AnswerScoring";
    public const string UsageKindOpenAiResumeAnalysis = "OpenAI.ResumeAnalysis";
    public const string UsageKindOpenAiQuestionPlanning = "OpenAI.QuestionPlanning";
    public const string UsageKindSpeechRecognition = "Speech.Recognition";
    public const string UsageKindSpeechSynthesis = "Speech.Synthesis";
}
