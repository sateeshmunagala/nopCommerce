using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.Extensions.Logging;
using NopLogLevel = Nop.Core.Domain.Logging.LogLevel;

namespace Nop.Plugin.Misc.AIInterview.Services;

public partial class InterviewAiClient
{
    private static readonly JsonSerializerOptions ResumePlanSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly string[] KnownSkillKeywords =
    {
        "c#", ".net", "asp.net", "java", "spring", "python", "javascript", "typescript", "react", "angular",
        "node", "sql", "azure", "aws", "docker", "kubernetes", "microservices", "rest", "api", "selenium",
        "testing", "automation", "git", "linux", "html", "css"
    };

    private static readonly HashSet<string> AllowedPlanCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "skill",
        "project_scenario",
        "job_fit",
        "behavioral"
    };

    public async Task<AIResumeProfileResponse> AnalyzeResumeAsync(AIResumeProfileRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockResumeProfile(request);

        var result = await CallAzureContentAsync(
            "resume-profile",
            "Return JSON only. Extract only facts supported by the resume text. Do not invent companies, projects, dates, skills, tools, metrics, or responsibilities. If project names are unclear, use a short descriptive label based on the resume text. If no projects are present, return an empty projects array. Keep each string concise.",
            BuildResumeProfilePrompt(request),
            1400);

        if (!result.Success)
            return new AIResumeProfileResponse { Success = false, ErrorMessage = result.ErrorMessage, UsageInfo = result.UsageInfo };

        var parsed = ParseResumeProfileResponse(result.Content);
        if (parsed != null)
            return parsed with { RawJson = TruncateSafe(result.Content, 4000), UsageInfo = result.UsageInfo };

        var contractReason = BuildStructuredResponseFailureLog(result.Content, "resume-profile");
        _logger?.LogWarning("Azure OpenAI resume profile call failed contract validation.");
        await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview resume profile contract failure", contractReason);
        return new AIResumeProfileResponse { Success = false, ErrorMessage = "Resume profiling is unavailable.", UsageInfo = result.UsageInfo };
    }

    public async Task<AIInterviewQuestionPlanResponse> GenerateQuestionPlanAsync(AIInterviewQuestionPlanRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockQuestionPlan(request);

        var result = await CallAzureContentAsync(
            "question-plan",
            "Return JSON only. Return exactly the requested number of questions. Ask one clear question per item. Do not include answers. Do not ask duplicate questions. Do not invent resume facts. Use resumeEvidence only for facts present in the resume profile. Project-scenario questions must be tied to a real project or responsibility from the resume profile when available. Skill questions must prioritize resume profile primary skills and job-required skills. Keep questions concise enough to read aloud in an interview runtime.",
            BuildQuestionPlanPrompt(request),
            2200);

        if (!result.Success)
        {
            return new AIInterviewQuestionPlanResponse
            {
                Success = false,
                ErrorMessage = result.ErrorMessage,
                UsageInfo = result.UsageInfo
            };
        }

        var parsed = ParseQuestionPlanResponse(result.Content, request.QuestionCount);
        if (parsed != null)
            return parsed with { RawJson = TruncateSafe(result.Content, 4000), UsageInfo = result.UsageInfo };

        var contractReason = BuildStructuredResponseFailureLog(result.Content, "question-plan");
        _logger?.LogWarning("Azure OpenAI question plan call failed contract validation.");
        await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview question plan contract failure", contractReason);
        return new AIInterviewQuestionPlanResponse
        {
            Success = false,
            ErrorMessage = "Question plan generation is unavailable.",
            UsageInfo = result.UsageInfo
        };
    }

    public async Task<AIInterviewFinalScoringResponse> ScoreInterviewAtCompletionAsync(AIInterviewFinalScoringRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockFinalScoring(request);

        var answeredCount = request?.Turns?.Count ?? 0;
        var result = await CallAzureContentAsync(
            "final-score",
            "Return JSON only. Final scoring mode contract: turns array with sequenceNumber, technicalScore, communicationScore, professionalismScore, positiveAttitudeScore, score, feedback, optional answerQuality, optional nonSubstantiveReason, optional rubricJson; plus overallScore and completion. Score every supplied answered turn exactly once. Do not add, remove, or renumber turns. All scores must be numeric 0-100. score must be the average of the four category scores. Reserve score 0 only for empty, copied, refusal, AI-persona, or unrelated answers.",
            BuildFinalScoringPrompt(request),
            Math.Clamp(1200 + answeredCount * 650, 2000, 8000));

        if (!result.Success)
        {
            return new AIInterviewFinalScoringResponse
            {
                Success = false,
                ErrorMessage = result.ErrorMessage,
                UsageInfo = result.UsageInfo
            };
        }

        var parsed = ParseFinalScoringResponse(result.Content, request?.Turns);
        if (parsed != null)
            return parsed with { RawJson = TruncateSafe(result.Content, 6000), UsageInfo = result.UsageInfo };

        var contractReason = BuildStructuredResponseFailureLog(result.Content, "final-score");
        _logger?.LogWarning("Azure OpenAI final scoring call failed contract validation.");
        await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview final scoring contract failure", contractReason);
        return new AIInterviewFinalScoringResponse
        {
            Success = false,
            ErrorMessage = "Final scoring is unavailable.",
            UsageInfo = result.UsageInfo
        };
    }

    public async Task<AIInterviewStrengthsSummaryResponse> GenerateStrengthsSummaryAsync(AIInterviewStrengthsSummaryRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockStrengthsSummary(request);

        var prompt = BuildStrengthsSummaryPrompt(request);
        var maxCompletionTokens = NormalizeStrengthsSummaryMaxCompletionTokens(_settings?.StrengthsSummaryMaxCompletionTokens ?? 0);
        var result = await CallAzureContentAsync(
            "strengths-summary",
            "Return JSON only. Strengths summary mode contract: strengthsText string, optional confidence string, optional evidenceTurnNumbers integer array. strengthsText must be 200 to 300 characters, plain text, no markdown, no bullets, and grounded only in the submitted answered turns.",
            prompt,
            maxCompletionTokens,
            allowEmptySuccessfulContent: true);

        if (ShouldRetryTruncatedEmptyStrengthsSummary(result))
        {
            var retryMaxCompletionTokens = Math.Min(maxCompletionTokens + 600, AIInterviewDefaults.MaxStrengthsSummaryMaxCompletionTokens);
            await LogAiClientIssueAsync(
                NopLogLevel.Information,
                "AI Interview strengths summary truncation retry initiated",
                $"Mode=strengths-summary; Reason=empty truncated response; InitialMaxCompletionTokens={maxCompletionTokens}; RetryMaxCompletionTokens={retryMaxCompletionTokens}; FinishReason={BuildSafeValue(result.FinishReason)}.");

            result = await CallAzureContentAsync(
                "strengths-summary",
                "Return JSON only. Strengths summary mode contract: strengthsText string, optional confidence string, optional evidenceTurnNumbers integer array. strengthsText must be 200 to 300 characters, plain text, no markdown, no bullets, and grounded only in the submitted answered turns. Strict JSON-first retry: start the response with { and output only one complete JSON object. No markdown fences, preface, trailing prose, or partial JSON.",
                prompt,
                retryMaxCompletionTokens);
        }

        if (!result.Success)
        {
            return new AIInterviewStrengthsSummaryResponse
            {
                Success = false,
                ErrorMessage = result.ErrorMessage,
                UsageInfo = result.UsageInfo
            };
        }

        var parsed = ParseStrengthsSummaryResponse(result.Content);
        if (parsed != null)
            return parsed with { RawJson = TruncateSafe(result.Content, 2000), UsageInfo = result.UsageInfo };

        var contractReason = BuildStructuredResponseFailureLog(result.Content, "strengths-summary");
        _logger?.LogWarning("Azure OpenAI strengths summary call failed contract validation.");
        await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview strengths summary contract failure", contractReason);
        return new AIInterviewStrengthsSummaryResponse
        {
            Success = false,
            ErrorMessage = "Strengths summary generation is unavailable.",
            UsageInfo = result.UsageInfo
        };
    }

    private async Task<AzureContentCallResult> CallAzureContentAsync(string mode, string systemPrompt, string prompt, int maxTokens, bool allowEmptySuccessfulContent = false)
    {
        var endpointConfigured = !string.IsNullOrWhiteSpace(_settings?.AzureOpenAiEndpointUrl);
        var apiKeyConfigured = !string.IsNullOrWhiteSpace(_settings?.AzureOpenAiApiKey);
        var deploymentConfigured = !string.IsNullOrWhiteSpace(_settings?.AzureOpenAiDeploymentOrModel);
        if (!endpointConfigured || !apiKeyConfigured || !deploymentConfigured)
        {
            var detail = BuildConfigurationIncompleteLog(mode, endpointConfigured, apiKeyConfigured, deploymentConfigured);
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI unavailable", detail);
            return new AzureContentCallResult(false, string.Empty, $"AI service unavailable. {detail}", null);
        }

        try
        {
            var result = await _azureOpenAiChatCompletionAdapter.CompleteChatAsync(new AzureOpenAiChatCompletionRequest
            {
                Mode = mode,
                OperationName = BuildAzureOperationName(mode),
                SystemPrompt = systemPrompt,
                UserPrompt = prompt,
                MaxCompletionTokens = maxTokens
            });

            if (!result.Success)
            {
                var detail = BuildAzureAdapterFailureLog(mode, result);
                var shortMessage = string.Equals(result.FailureKind, "azure-openai-http-failure", StringComparison.OrdinalIgnoreCase)
                    ? "AI Interview Azure OpenAI HTTP failure"
                    : "AI Interview Azure OpenAI exception";
                await LogAiClientIssueAsync(NopLogLevel.Warning, shortMessage, detail);
                return new AzureContentCallResult(false, string.Empty, $"AI service unavailable. {detail}", null);
            }

            var usageInfo = result.UsageInfo;
            if (string.IsNullOrWhiteSpace(result.Content))
            {
                if (allowEmptySuccessfulContent && result.IsLengthTruncated)
                    return new AzureContentCallResult(true, string.Empty, string.Empty, usageInfo, result.IsLengthTruncated, result.FinishReason);

                var detail = BuildAzureContractFailureLog(mode, result.Endpoint, "empty response content", result.ResponseBody);
                await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI contract failure", detail);
                return new AzureContentCallResult(false, string.Empty, $"AI service unavailable. {detail}", usageInfo, result.IsLengthTruncated, result.FinishReason);
            }

            return new AzureContentCallResult(true, result.Content, string.Empty, usageInfo, result.IsLengthTruncated, result.FinishReason);
        }
        catch (JsonException ex)
        {
            var detail = BuildAzureExceptionLog(mode, "azure-openai-json-failure", "invalid JSON format", ex);
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI JSON failure", detail);
            return new AzureContentCallResult(false, string.Empty, $"AI service unavailable. {detail}", null);
        }
        catch (Exception ex)
        {
            var detail = BuildAzureExceptionLog(mode, "azure-openai-exception", ex.GetType().Name, ex);
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI exception", detail);
            return new AzureContentCallResult(false, string.Empty, $"AI service unavailable. {detail}", null);
        }
    }

    private static string BuildResumeProfilePrompt(AIResumeProfileRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Resume analysis mode: resume-profile");
        builder.AppendLine($"Job title: {request.JobTitle}");
        builder.AppendLine($"Job context: {TruncateSafe(request.JobContext, 2000)}");
        builder.AppendLine("Resume text:");
        builder.AppendLine(TruncateSafe(request.ResumeText, 12000));
        builder.AppendLine("Response contract:");
        builder.Append("""
{
  "skills": ["string"],
  "primarySkills": ["string"],
  "tools": ["string"],
  "projects": [
    {
      "name": "string",
      "domain": "string",
      "technologies": ["string"],
      "responsibilities": ["string"],
      "impact": "string"
    }
  ],
  "experienceSummary": "string",
  "senioritySignals": ["string"],
  "missingOrUnclearAreas": ["string"]
}
""");
        return builder.ToString();
    }

    private static string BuildQuestionPlanPrompt(AIInterviewQuestionPlanRequest request)
    {
        var totalQuestionCount = request.TotalQuestionCount > 0 ? request.TotalQuestionCount : request.QuestionCount;
        var builder = new StringBuilder();
        builder.AppendLine("Interview mode: question-plan");
        builder.AppendLine($"Job title: {request.JobTitle}");
        builder.AppendLine($"Job context: {TruncateSafe(request.JobContext, 2500)}");
        builder.AppendLine($"Difficulty: {request.Difficulty}");
        builder.AppendLine($"Question count to return now: {request.QuestionCount}");
        builder.AppendLine($"Total interview question count: {totalQuestionCount}");
        builder.AppendLine($"Global prompt: {request.Prompt}");
        builder.AppendLine("Resume profile JSON:");
        builder.AppendLine(TruncateSafe(request.ResumeProfileJson, 4000));
        builder.AppendLine("Sequence 1 is reserved by the runtime for the candidate introduction and project-experience question.");
        builder.AppendLine("Generate exactly the requested remaining questions for this call; do not duplicate the introduction/project-experience question.");
        builder.AppendLine("Use remaining sequence numbers only. If sequence 1 already exists, begin generated questions at sequence 2 and continue from there.");
        builder.AppendLine("Remaining questions should build on resume and job context, cover role-relevant technical depth, feel natural and conversational, and ask one clear question at a time.");
        builder.AppendLine("Allowed categories: skill, project_scenario, job_fit, behavioral");
        if (request.ExistingQuestions?.Any() == true)
        {
            builder.AppendLine("Existing planned questions that must not be duplicated:");
            foreach (var question in request.ExistingQuestions.Where(question => !string.IsNullOrWhiteSpace(question)))
                builder.AppendLine($"- {TruncateSafe(question, 220)}");
        }

        if (request.ExistingCategories?.Any() == true)
            builder.AppendLine($"Existing category usage: {string.Join(", ", request.ExistingCategories.Where(category => !string.IsNullOrWhiteSpace(category)).Select(NormalizePlanCategory))}");

        builder.AppendLine("Response contract:");
        builder.Append("""
{
  "questions": [
    {
      "sequenceNumber": 2,
      "category": "skill",
      "question": "string",
      "resumeEvidence": "string",
      "expectedSignals": ["string"],
      "rubric": {
        "technical": "string",
        "communication": "string",
        "professionalism": "string",
        "positiveAttitude": "string"
      }
    }
  ]
}
""");
        return builder.ToString();
    }

    private static string BuildFinalScoringPrompt(AIInterviewFinalScoringRequest request)
    {
        var turns = (request?.Turns ?? new List<AIInterviewFinalScoringTurnRequest>())
            .OrderBy(turn => turn.SequenceNumber)
            .Select(turn => new
            {
                turn.SequenceNumber,
                Question = TruncateSafe(turn.Question, 1200),
                Answer = TruncateSafe(turn.Answer, 2500),
                CurrentTurnRubricJson = TruncateSafe(turn.CurrentTurnRubricJson, 1600)
            })
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("Interview mode: final-score");
        builder.AppendLine($"Job title: {request?.JobTitle}");
        builder.AppendLine($"Job context: {TruncateSafe(request?.JobContext, 2500)}");
        builder.AppendLine($"Difficulty: {request?.Difficulty}");
        builder.AppendLine($"Global prompt: {request?.Prompt}");
        builder.AppendLine("Resume profile JSON:");
        builder.AppendLine(TruncateSafe(request?.ResumeProfileJson, 4000));
        builder.AppendLine("Answered turns to score, in order:");
        builder.AppendLine(JsonSerializer.Serialize(turns, ResumePlanSerializerOptions));
        builder.AppendLine("Response contract:");
        builder.Append("""
{
  "turns": [
    {
      "sequenceNumber": 1,
      "technicalScore": 0,
      "communicationScore": 0,
      "professionalismScore": 0,
      "positiveAttitudeScore": 0,
      "score": 0,
      "feedback": "string",
      "answerQuality": "non_substantive|weak|substantive",
      "nonSubstantiveReason": "optional string",
      "rubricJson": {}
    }
  ],
  "overallScore": 0,
  "completion": "short final report summary"
}
""");
        return builder.ToString();
    }

    private static string BuildStrengthsSummaryPrompt(AIInterviewStrengthsSummaryRequest request)
    {
        var turns = (request?.Turns ?? new List<AIInterviewStrengthsSummaryTurnRequest>())
            .OrderBy(turn => turn.SequenceNumber)
            .Select(turn => new
            {
                turn.SequenceNumber,
                Question = TruncateSafe(turn.Question, 900),
                Answer = TruncateSafe(turn.Answer, 1800),
                Score = turn.Score,
                Feedback = TruncateSafe(turn.Feedback, 600)
            })
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("Interview mode: strengths-summary");
        builder.AppendLine($"Job title: {request?.JobTitle}");
        builder.AppendLine($"Job context: {TruncateSafe(request?.JobContext, 1800)}");
        builder.AppendLine($"Difficulty: {request?.Difficulty}");
        builder.AppendLine("Resume profile JSON:");
        builder.AppendLine(TruncateSafe(request?.ResumeProfileJson, 3000));
        builder.AppendLine("Answered turns to summarize, in order:");
        builder.AppendLine(JsonSerializer.Serialize(turns, ResumePlanSerializerOptions));
        builder.AppendLine("Write one concise evidence-based strengths paragraph. Reflect the actual submitted answers and scored feedback. Avoid generic boilerplate.");
        builder.AppendLine("Response contract:");
        builder.Append("""
{
  "strengthsText": "200 to 300 character evidence-based strengths paragraph",
  "confidence": "optional string",
  "evidenceTurnNumbers": [1]
}
""");
        return builder.ToString();
    }

    private static int NormalizeStrengthsSummaryMaxCompletionTokens(int maxCompletionTokens)
    {
        return Math.Clamp(
            maxCompletionTokens <= 0 ? AIInterviewDefaults.DefaultStrengthsSummaryMaxCompletionTokens : maxCompletionTokens,
            AIInterviewDefaults.MinStrengthsSummaryMaxCompletionTokens,
            AIInterviewDefaults.MaxStrengthsSummaryMaxCompletionTokens);
    }

    private static bool ShouldRetryTruncatedEmptyStrengthsSummary(AzureContentCallResult result)
    {
        return result?.Success == true &&
            string.IsNullOrWhiteSpace(result.Content) &&
            result.IsLengthTruncated;
    }

    private static AIResumeProfileResponse ParseResumeProfileResponse(string content)
    {
        try
        {
            var normalized = ExtractJsonObjectPayload(content);
            using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;

            return new AIResumeProfileResponse
            {
                Success = true,
                Skills = ParseStringArray(root, "skills", 20, 80),
                PrimarySkills = ParseStringArray(root, "primarySkills", 10, 80),
                Tools = ParseStringArray(root, "tools", 15, 80),
                Projects = root.TryGetProperty("projects", out var projectsElement) && projectsElement.ValueKind == JsonValueKind.Array
                    ? projectsElement.EnumerateArray().Select(ParseProject).Where(project => project != null).ToList()
                    : new List<AIResumeProjectProfile>(),
                ExperienceSummary = TruncateSafe(TryGetString(root, "experienceSummary"), 280),
                SenioritySignals = ParseStringArray(root, "senioritySignals", 8, 120),
                MissingOrUnclearAreas = ParseStringArray(root, "missingOrUnclearAreas", 8, 160),
                RawJson = TruncateSafe(content, 4000)
            };
        }
        catch
        {
            return null;
        }
    }

    private static AIResumeProjectProfile ParseProject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return new AIResumeProjectProfile
        {
            Name = TruncateSafe(TryGetString(element, "name"), 120),
            Domain = TruncateSafe(TryGetString(element, "domain"), 120),
            Technologies = ParseStringArray(element, "technologies", 10, 80),
            Responsibilities = ParseStringArray(element, "responsibilities", 8, 160),
            Impact = TruncateSafe(TryGetString(element, "impact"), 200)
        };
    }

    private static AIInterviewFinalScoringResponse ParseFinalScoringResponse(string content, IList<AIInterviewFinalScoringTurnRequest> expectedTurns)
    {
        try
        {
            var normalized = ExtractJsonObjectPayload(content);
            using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;
            if (!root.TryGetProperty("turns", out var turnsElement) || turnsElement.ValueKind != JsonValueKind.Array)
                return null;

            var expectedSequenceNumbers = (expectedTurns ?? new List<AIInterviewFinalScoringTurnRequest>())
                .Select(turn => turn.SequenceNumber)
                .ToHashSet();
            var turns = turnsElement.EnumerateArray()
                .Select(ParseFinalScoringTurnResult)
                .Where(turn => turn != null)
                .ToList();

            if (turns.Count != expectedSequenceNumbers.Count ||
                turns.Any(turn => !expectedSequenceNumbers.Contains(turn.SequenceNumber)) ||
                turns.Any(turn => !turn.Score.HasValue ||
                    !turn.TechnicalScore.HasValue ||
                    !turn.CommunicationScore.HasValue ||
                    !turn.ProfessionalismScore.HasValue ||
                    !turn.PositiveAttitudeScore.HasValue ||
                    string.IsNullOrWhiteSpace(turn.Feedback)))
            {
                return new AIInterviewFinalScoringResponse
                {
                    Success = false,
                    ErrorMessage = "Final scoring response did not score every answered turn.",
                    RawJson = TruncateSafe(content, 6000)
                };
            }

            var overallScore = TryGetFinalScoringDecimal(root, "overallScore")
                ?? turns.Select(turn => turn.Score.Value).DefaultIfEmpty(0).Average();
            return new AIInterviewFinalScoringResponse
            {
                Success = true,
                Turns = turns,
                Score = Math.Clamp(overallScore, 0, 100),
                Completion = TruncateSafe(TryGetString(root, "completion"), 2000),
                RawJson = TruncateSafe(content, 6000)
            };
        }
        catch
        {
            return null;
        }
    }

    private static AIInterviewStrengthsSummaryResponse ParseStrengthsSummaryResponse(string content)
    {
        try
        {
            var normalized = ExtractJsonObjectPayload(content);
            using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;
            var strengthsText = NormalizeWhitespace(TryGetString(root, "strengthsText"));
            if (strengthsText.Length < 200 || strengthsText.Length > 300)
                return null;

            return new AIInterviewStrengthsSummaryResponse
            {
                Success = true,
                StrengthsText = strengthsText,
                Confidence = TruncateSafe(TryGetString(root, "confidence"), 80),
                EvidenceTurnNumbers = ParseIntArray(root, "evidenceTurnNumbers", 10),
                RawJson = TruncateSafe(content, 2000)
            };
        }
        catch
        {
            return null;
        }
    }

    private static AIInterviewFinalScoringTurnResult ParseFinalScoringTurnResult(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty("sequenceNumber", out var sequenceElement) || !sequenceElement.TryGetInt32(out var sequenceNumber))
            return null;

        var rubricJson = string.Empty;
        if (element.TryGetProperty("rubricJson", out var rubricElement) && rubricElement.ValueKind != JsonValueKind.Undefined && rubricElement.ValueKind != JsonValueKind.Null)
            rubricJson = rubricElement.GetRawText();

        return new AIInterviewFinalScoringTurnResult
        {
            SequenceNumber = sequenceNumber,
            TechnicalScore = TryGetFinalScoringDecimal(element, "technicalScore"),
            CommunicationScore = TryGetFinalScoringDecimal(element, "communicationScore"),
            ProfessionalismScore = TryGetFinalScoringDecimal(element, "professionalismScore"),
            PositiveAttitudeScore = TryGetFinalScoringDecimal(element, "positiveAttitudeScore"),
            Score = TryGetFinalScoringDecimal(element, "score"),
            Feedback = TruncateSafe(TryGetString(element, "feedback"), 1000),
            AnswerQuality = TruncateSafe(TryGetString(element, "answerQuality"), 80),
            NonSubstantiveReason = TruncateSafe(TryGetString(element, "nonSubstantiveReason"), 240),
            RubricJson = rubricJson
        };
    }

    private static AIInterviewQuestionPlanResponse ParseQuestionPlanResponse(string content, int expectedCount)
    {
        try
        {
            var normalized = ExtractJsonObjectPayload(content);
            using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;
            if (!root.TryGetProperty("questions", out var questionsElement) || questionsElement.ValueKind != JsonValueKind.Array)
                return null;

            var questions = questionsElement.EnumerateArray()
                .Select((question, index) => ParseQuestionPlanItem(question, index + 1))
                .Where(question => question != null)
                .ToList();

            if (questions.Count != expectedCount)
                return new AIInterviewQuestionPlanResponse
                {
                    Success = false,
                    ErrorMessage = "Question plan did not return the configured number of questions.",
                    RawJson = TruncateSafe(content, 4000)
                };

            return new AIInterviewQuestionPlanResponse
            {
                Success = true,
                Questions = questions,
                RawJson = TruncateSafe(content, 4000)
            };
        }
        catch
        {
            return null;
        }
    }

    private static AIInterviewQuestionPlanItem ParseQuestionPlanItem(JsonElement element, int fallbackSequenceNumber)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var category = NormalizePlanCategory(TryGetString(element, "category"));
        return new AIInterviewQuestionPlanItem
        {
            SequenceNumber = element.TryGetProperty("sequenceNumber", out var sequenceElement) && sequenceElement.TryGetInt32(out var parsedSequence)
                ? parsedSequence
                : fallbackSequenceNumber,
            Category = category,
            Question = TruncateSafe(TryGetString(element, "question"), 240),
            ResumeEvidence = TruncateSafe(TryGetString(element, "resumeEvidence"), 200),
            ExpectedSignals = ParseStringArray(element, "expectedSignals", 6, 120),
            Rubric = ParseQuestionRubric(element)
        };
    }

    private static AIInterviewQuestionRubric ParseQuestionRubric(JsonElement element)
    {
        if (!element.TryGetProperty("rubric", out var rubricElement) || rubricElement.ValueKind != JsonValueKind.Object)
            return new AIInterviewQuestionRubric();

        return new AIInterviewQuestionRubric
        {
            Technical = TruncateSafe(TryGetString(rubricElement, "technical"), 160),
            Communication = TruncateSafe(TryGetString(rubricElement, "communication"), 160),
            Professionalism = TruncateSafe(TryGetString(rubricElement, "professionalism"), 160),
            PositiveAttitude = TruncateSafe(TryGetString(rubricElement, "positiveAttitude"), 160)
        };
    }

    private static IList<string> ParseStringArray(JsonElement element, string propertyName, int maxItems, int maxLength)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => TruncateSafe(item.GetString(), maxLength))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();
    }

    private static IList<int> ParseIntArray(JsonElement element, string propertyName, int maxItems)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return new List<int>();

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var value) ? value : 0)
            .Where(value => value > 0)
            .Distinct()
            .Take(maxItems)
            .ToList();
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : Regex.Replace(text.Trim(), "\\s+", " ");
    }

    private static string TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : string.Empty;
    }

    private static decimal? TryGetFinalScoringDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var numeric))
            return Math.Clamp(numeric, 0, 100);

        if (property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return Math.Clamp(parsed, 0, 100);

        return null;
    }

    private static string NormalizePlanCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return "job_fit";

        var normalized = category.Trim().ToLowerInvariant();
        return AllowedPlanCategories.Contains(normalized) ? normalized : "job_fit";
    }

    private static AIResumeProfileResponse ParseResumeProfileForMock(string resumeProfileJson)
    {
        if (string.IsNullOrWhiteSpace(resumeProfileJson))
            return new AIResumeProfileResponse();

        try
        {
            return JsonSerializer.Deserialize<AIResumeProfileResponse>(resumeProfileJson, ResumePlanSerializerOptions) ?? new AIResumeProfileResponse();
        }
        catch
        {
            return new AIResumeProfileResponse();
        }
    }

    private AIResumeProfileResponse BuildMockResumeProfile(AIResumeProfileRequest request)
    {
        var resumeText = request?.ResumeText ?? string.Empty;
        var skills = ExtractSkills(resumeText);
        var projects = ExtractProjects(resumeText, skills);

        return new AIResumeProfileResponse
        {
            Success = true,
            Skills = skills,
            PrimarySkills = skills.Take(4).ToList(),
            Tools = skills.Where(skill => skill.Contains("azure", StringComparison.OrdinalIgnoreCase) ||
                                          skill.Contains("aws", StringComparison.OrdinalIgnoreCase) ||
                                          skill.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                                          skill.Contains("kubernetes", StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .ToList(),
            Projects = projects,
            ExperienceSummary = string.IsNullOrWhiteSpace(request?.JobTitle)
                ? "Resume-backed candidate profile."
                : $"Resume-backed profile for {request.JobTitle}.",
            SenioritySignals = new List<string> { "Hands-on delivery", "Project execution" },
            MissingOrUnclearAreas = new List<string>()
        };
    }

    private AIInterviewQuestionPlanResponse BuildMockQuestionPlan(AIInterviewQuestionPlanRequest request)
    {
        var profile = ParseResumeProfileForMock(request.ResumeProfileJson);
        var totalQuestionCount = Math.Clamp(request.TotalQuestionCount <= 0 ? (request.QuestionCount <= 0 ? 3 : request.QuestionCount) : request.TotalQuestionCount, 1, 10);
        var questionCount = Math.Clamp(request.QuestionCount <= 0 ? totalQuestionCount : request.QuestionCount, 1, totalQuestionCount);
        var existingQuestions = (request.ExistingQuestions ?? new List<string>())
            .Where(question => !string.IsNullOrWhiteSpace(question))
            .Select(question => question.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existingCategories = (request.ExistingCategories ?? new List<string>())
            .Where(category => !string.IsNullOrWhiteSpace(category) &&
                !string.Equals(category, "Introduction & Project Experience", StringComparison.OrdinalIgnoreCase))
            .Select(NormalizePlanCategory)
            .ToList();
        var shouldIncludeIntroQuestion = ShouldIncludeMockIntroductionQuestion(request, totalQuestionCount, existingQuestions);
        var generatedQuestionCount = shouldIncludeIntroQuestion ? questionCount - 1 : questionCount;
        var categories = BuildRemainingQuestionCategories(totalQuestionCount, generatedQuestionCount, existingCategories, profile.Projects.Any());
        var primarySkills = profile.PrimarySkills.Any() ? profile.PrimarySkills : profile.Skills.Any() ? profile.Skills : new List<string> { request.JobTitle, "problem solving" };
        var seenQuestions = new HashSet<string>(existingQuestions, StringComparer.OrdinalIgnoreCase);
        var questions = new List<AIInterviewQuestionPlanItem>();

        if (shouldIncludeIntroQuestion)
        {
            var introQuestion = BuildMockIntroductionQuestionPlanItem();
            questions.Add(introQuestion);
            seenQuestions.Add(introQuestion.Question);
        }

        for (var index = 0; index < generatedQuestionCount; index++)
        {
            var category = categories[index];
            var skill = primarySkills[index % primarySkills.Count];
            var project = profile.Projects.Any() ? profile.Projects[index % profile.Projects.Count] : null;
            var questionText = BuildMockPlanQuestionText(request, category, skill, project, index, 0);
            var variant = 1;
            while (seenQuestions.Contains(questionText) && variant < 6)
            {
                questionText = BuildMockPlanQuestionText(request, category, skill, project, index, variant);
                variant++;
            }

            if (seenQuestions.Contains(questionText))
                questionText = $"{questionText} (follow-up {variant})";

            seenQuestions.Add(questionText);

            questions.Add(new AIInterviewQuestionPlanItem
            {
                SequenceNumber = questions.Count + 1,
                Category = category,
                Question = questionText,
                ResumeEvidence = category == "project_scenario" && project != null
                    ? string.IsNullOrWhiteSpace(project.Name) ? project.Domain : project.Name
                    : skill,
                ExpectedSignals = category switch
                {
                    "skill" => new List<string> { $"{skill} depth", "Tradeoff awareness", "Practical example" },
                    "project_scenario" => new List<string> { "Scenario reasoning", "Project-specific detail", "Outcome focus" },
                    "behavioral" => new List<string> { "Ownership", "Communication", "Reflection" },
                    _ => new List<string> { "Role alignment", "Learning plan", "Execution focus" }
                },
                Rubric = new AIInterviewQuestionRubric
                {
                    Technical = "Evaluate technical depth and decision quality.",
                    Communication = "Evaluate clarity and structure.",
                    Professionalism = "Evaluate ownership and judgment.",
                    PositiveAttitude = "Evaluate constructive and growth-oriented mindset."
                }
            });
        }

        return new AIInterviewQuestionPlanResponse
        {
            Success = true,
            Questions = questions,
            RawJson = JsonSerializer.Serialize(new { questions }, ResumePlanSerializerOptions)
        };
    }

    private AIInterviewFinalScoringResponse BuildMockFinalScoring(AIInterviewFinalScoringRequest request)
    {
        var scoredTurns = (request?.Turns ?? new List<AIInterviewFinalScoringTurnRequest>())
            .OrderBy(turn => turn.SequenceNumber)
            .Select(turn =>
            {
                var score = BuildMockScore(new AIInterviewClientRequest
                {
                    JobTitle = request?.JobTitle,
                    JobContext = request?.JobContext,
                    Difficulty = request?.Difficulty,
                    Prompt = request?.Prompt,
                    Question = turn.Question,
                    Answer = turn.Answer,
                    QuestionNumber = turn.SequenceNumber,
                    ResumeProfileJson = request?.ResumeProfileJson,
                    CurrentTurnRubricJson = turn.CurrentTurnRubricJson
                });

                return new AIInterviewFinalScoringTurnResult
                {
                    SequenceNumber = turn.SequenceNumber,
                    TechnicalScore = score.TechnicalScore,
                    CommunicationScore = score.CommunicationScore,
                    ProfessionalismScore = score.ProfessionalismScore,
                    PositiveAttitudeScore = score.PositiveAttitudeScore,
                    Score = score.Score,
                    Feedback = score.Feedback,
                    AnswerQuality = score.AnswerQuality,
                    NonSubstantiveReason = score.NonSubstantiveReason,
                    RubricJson = score.RubricJson
                };
            })
            .ToList();

        var overallScore = scoredTurns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).DefaultIfEmpty(0).Average();
        return new AIInterviewFinalScoringResponse
        {
            Success = true,
            Turns = scoredTurns,
            Score = overallScore,
            Completion = "Final scoring completed.",
            RawJson = JsonSerializer.Serialize(new
            {
                turns = scoredTurns,
                overallScore,
                completion = "Final scoring completed."
            }, ResumePlanSerializerOptions)
        };
    }

    private AIInterviewStrengthsSummaryResponse BuildMockStrengthsSummary(AIInterviewStrengthsSummaryRequest request)
    {
        var answeredTurns = (request?.Turns ?? new List<AIInterviewStrengthsSummaryTurnRequest>())
            .Where(turn => !string.IsNullOrWhiteSpace(turn.Answer))
            .OrderByDescending(turn => turn.Score.GetValueOrDefault())
            .ThenBy(turn => turn.SequenceNumber)
            .Take(2)
            .ToList();
        if (!answeredTurns.Any())
        {
            return new AIInterviewStrengthsSummaryResponse
            {
                Success = false,
                ErrorMessage = "No answered turns available."
            };
        }

        var evidence = string.Join(" and ", answeredTurns.Select(turn => $"turn {turn.SequenceNumber}"));
        var strengthsText = $"The candidate showed practical role fit through {evidence}, giving concrete implementation details, clear ownership, and thoughtful tradeoff awareness. Their answers connected project experience to delivery quality, testing, monitoring, and collaborative execution.";
        strengthsText = NormalizeWhitespace(strengthsText);
        if (strengthsText.Length > 300)
            strengthsText = strengthsText[..300].TrimEnd('.', ' ') + ".";

        return new AIInterviewStrengthsSummaryResponse
        {
            Success = strengthsText.Length is >= 200 and <= 300,
            StrengthsText = strengthsText,
            Confidence = "mock",
            EvidenceTurnNumbers = answeredTurns.Select(turn => turn.SequenceNumber).ToList(),
            RawJson = JsonSerializer.Serialize(new
            {
                strengthsText,
                confidence = "mock",
                evidenceTurnNumbers = answeredTurns.Select(turn => turn.SequenceNumber).ToList()
            }, ResumePlanSerializerOptions)
        };
    }

    private static bool ShouldIncludeMockIntroductionQuestion(AIInterviewQuestionPlanRequest request, int totalQuestionCount, IList<string> existingQuestions)
    {
        if (request == null || request.QuestionCount <= 0 || request.QuestionCount != totalQuestionCount)
            return false;

        return existingQuestions?.Any(question => question.Contains("let's start with you", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("introduce yourself", StringComparison.OrdinalIgnoreCase)) != true;
    }

    private static AIInterviewQuestionPlanItem BuildMockIntroductionQuestionPlanItem()
    {
        return new AIInterviewQuestionPlanItem
        {
            SequenceNumber = 1,
            Category = "Introduction & Project Experience",
            Question = "Let's start with you. Please introduce yourself and walk me through one or two projects you are most proud of. I'd like to understand your role, the technologies you used, the main challenges you handled, and the impact of the work.",
            ResumeEvidence = string.Empty,
            ExpectedSignals = new List<string>
            {
                "Clear self-introduction",
                "Relevant project ownership",
                "Technologies used",
                "Implementation details and tradeoffs",
                "Challenges solved",
                "Measurable impact or outcome",
                "Communication clarity"
            },
            Rubric = new AIInterviewQuestionRubric
            {
                Technical = "Evaluate evidence of real project experience, architecture or implementation details, tools, tradeoffs, debugging or challenges, and impact.",
                Communication = "Evaluate clarity, structure, confidence, and ability to explain experience naturally.",
                Professionalism = "Evaluate ownership, honesty, relevance to the role, maturity, and responsibility.",
                PositiveAttitude = "Evaluate curiosity, learning mindset, constructive framing, and motivation."
            }
        };
    }

    private static IList<string> BuildRemainingQuestionCategories(int totalQuestionCount, int requestedQuestionCount, IList<string> existingCategories, bool hasProjects)
    {
        var remainingCategories = BuildQuestionCategories(totalQuestionCount, hasProjects).ToList();
        foreach (var existingCategory in existingCategories ?? Array.Empty<string>())
        {
            var normalized = NormalizePlanCategory(existingCategory);
            var index = remainingCategories.FindIndex(category => category.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                remainingCategories.RemoveAt(index);
        }

        while (remainingCategories.Count < requestedQuestionCount)
            remainingCategories.Add(remainingCategories.Count % 2 == 0 ? "job_fit" : "behavioral");

        return remainingCategories.Take(requestedQuestionCount).ToList();
    }

    private static string BuildMockPlanQuestionText(AIInterviewQuestionPlanRequest request, string category, string skill, AIResumeProjectProfile project, int index, int variant)
    {
        return category switch
        {
            "skill" => variant switch
            {
                0 => $"Your resume highlights {skill}. How would you apply that in a {request.Difficulty} {request.JobTitle} assignment?",
                1 => $"What is the hardest production problem you solved using {skill}, and how does that experience fit this {request.JobTitle} role?",
                _ => $"Which tradeoffs would you watch first when using {skill} in this {request.JobTitle} position?"
            },
            "project_scenario" when project != null => variant switch
            {
                0 => $"In your {project.Name} project, how would you handle a scenario where the core solution must scale while preserving reliability?",
                1 => $"Looking at {project.Name}, what would you change first if the project suddenly needed stronger observability and fault isolation?",
                _ => $"What architecture or delivery tradeoffs stood out most in {project.Name}, and how would you explain them to this team?"
            },
            "behavioral" => variant switch
            {
                0 => $"Describe a time you had to collaborate under pressure in work related to {request.JobTitle}. What did you do?",
                1 => $"Tell me about a disagreement you navigated while delivering work similar to this {request.JobTitle} role.",
                _ => $"How have you handled feedback or shifting priorities in work connected to {request.JobTitle}?"
            },
            _ => variant switch
            {
                0 => $"What part of this {request.JobTitle} role is the strongest match for your background, and where would you ramp up first?",
                1 => $"Which responsibility in this {request.JobTitle} job would you take ownership of earliest, and why?",
                _ => $"How does your background prepare you for the first 90 days of this {request.JobTitle} role?"
            }
        };
    }

    private static IList<string> BuildQuestionCategories(int questionCount, bool hasProjects)
    {
        int skillCount;
        int projectCount;

        if (questionCount == 1)
        {
            skillCount = 1;
            projectCount = 0;
        }
        else if (questionCount == 5)
        {
            skillCount = 2;
            projectCount = hasProjects ? 2 : 0;
        }
        else if (questionCount == 10)
        {
            skillCount = 4;
            projectCount = hasProjects ? 4 : 0;
        }
        else
        {
            skillCount = Math.Clamp((int)Math.Round(questionCount * 0.4m, MidpointRounding.AwayFromZero), questionCount >= 2 ? 1 : 0, questionCount);
            projectCount = hasProjects
                ? Math.Clamp((int)Math.Round(questionCount * 0.4m, MidpointRounding.AwayFromZero), questionCount >= 3 ? 1 : 0, Math.Max(0, questionCount - skillCount))
                : 0;
        }

        if (!hasProjects)
            projectCount = 0;

        while (skillCount + projectCount > questionCount && projectCount > 0)
            projectCount--;

        var remaining = Math.Max(0, questionCount - skillCount - projectCount);
        var categories = Enumerable.Repeat("skill", skillCount)
            .Concat(Enumerable.Repeat("project_scenario", projectCount))
            .ToList();

        for (var index = 0; index < remaining; index++)
            categories.Add(index % 2 == 0 ? "job_fit" : "behavioral");

        return categories.Take(questionCount).ToList();
    }

    private static IList<string> ExtractSkills(string resumeText)
    {
        var normalized = $" {resumeText?.ToLowerInvariant() ?? string.Empty} ";
        var found = KnownSkillKeywords
            .Where(keyword => normalized.Contains($" {keyword.ToLowerInvariant()} ", StringComparison.Ordinal) || normalized.Contains(keyword.ToLowerInvariant(), StringComparison.Ordinal))
            .Select(keyword => keyword.Equals(".net", StringComparison.OrdinalIgnoreCase) ? ".NET" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(keyword.Replace("c#", "C#")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return found.Any() ? found : new List<string> { "Problem solving", "System design" };
    }

    private static IList<AIResumeProjectProfile> ExtractProjects(string resumeText, IList<string> skills)
    {
        var lines = Regex.Split(resumeText ?? string.Empty, @"[\r\n\.]+")
            .Select(line => line.Trim())
            .Where(line => line.Length >= 12)
            .Where(line => line.Contains("project", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("application", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("platform", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("system", StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToList();

        return lines.Select((line, index) => new AIResumeProjectProfile
        {
            Name = TruncateSafe(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5).Aggregate((left, right) => $"{left} {right}"), 120),
            Domain = "Resume project",
            Technologies = skills.Take(3).ToList(),
            Responsibilities = new List<string> { TruncateSafe(line, 160) },
            Impact = "Delivered project responsibilities from resume evidence."
        }).ToList();
    }
}
