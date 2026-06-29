using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Nop.Core;
using NopLogLevel = Nop.Core.Domain.Logging.LogLevel;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Events;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using NopLogger = Nop.Services.Logging.ILogger;
using Nop.Core.Events;
using Nop.Services.Events;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class InterviewTurnService : IInterviewTurnService
{
    private readonly IRepository<InterviewTurn> _turnRepository;

    public InterviewTurnService(IRepository<InterviewTurn> turnRepository)
    {
        _turnRepository = turnRepository;
    }

    public async Task<InterviewTurn> InsertInterviewTurnAsync(InterviewTurn turn)
    {
        await _turnRepository.InsertAsync(turn);
        return turn;
    }

    public async Task<IList<InterviewTurn>> GetTurnsBySessionIdAsync(int interviewSessionId)
    {
        return await _turnRepository.GetAllAsync(query => query
            .Where(turn => turn.InterviewSessionId == interviewSessionId)
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id));
    }

    public async Task<InterviewTurn> GetLatestTurnBySessionIdAsync(int interviewSessionId)
    {
        return (await _turnRepository.GetAllAsync(query => query
            .Where(turn => turn.InterviewSessionId == interviewSessionId)
            .OrderByDescending(turn => turn.SequenceNumber)
            .ThenByDescending(turn => turn.Id)))
            .FirstOrDefault();
    }

    public async Task UpdateInterviewTurnAsync(InterviewTurn turn)
    {
        await _turnRepository.UpdateAsync(turn);
    }
}

public class InterviewAiClient : IAIInterviewClient
{
    private readonly AIInterviewSettings _settings;
    private readonly MockAIInterviewSettings _mockSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWorkContext _workContext;
    private readonly NopLogger _nopLogger;
    private readonly ILogger<InterviewAiClient> _logger;

    public InterviewAiClient(AIInterviewSettings settings, MockAIInterviewSettings mockSettings, IHttpClientFactory httpClientFactory = null, IWorkContext workContext = null, NopLogger nopLogger = null, ILogger<InterviewAiClient> logger = null)
    {
        _settings = settings;
        _mockSettings = mockSettings;
        _httpClientFactory = httpClientFactory;
        _workContext = workContext;
        _nopLogger = nopLogger;
        _logger = logger;
    }

    public async Task<AIInterviewClientResponse> GenerateQuestionAsync(AIInterviewClientRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockQuestion(request);

        var response = await CallAzureAsync(request, "generate");
        if (response == null || !response.Success)
            return response ?? BuildUnavailableResponse();

        if (string.IsNullOrWhiteSpace(response.Question))
        {
            var detail = $"Mode=generate; Reason=empty question; QuestionMissing=true; Sample={TruncateSafe(response.RawJson, 300)}.";
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview question validation failure", detail);
            return BuildValidationFailureResponse(response, detail);
        }

        return response;
    }

    public async Task<AIInterviewClientResponse> ScoreAnswerAsync(AIInterviewClientRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockScore(request);

        var response = await CallAzureAsync(request, "score");
        if (response == null || !response.Success)
            return response ?? BuildUnavailableResponse();

        if (!response.Score.HasValue ||
            string.IsNullOrWhiteSpace(response.Feedback) ||
            !response.TechnicalScore.HasValue ||
            !response.CommunicationScore.HasValue ||
            !response.ProfessionalismScore.HasValue ||
            !response.PositiveAttitudeScore.HasValue ||
            (!response.Complete && string.IsNullOrWhiteSpace(response.NextQuestion)))
        {
            var detail = BuildScoreValidationFailureLog(response);
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview score validation failure", detail);
            return BuildValidationFailureResponse(response, detail);
        }

        return response;
    }

    protected virtual AIInterviewClientResponse BuildUnavailableResponse(string errorMessage = "AI service unavailable.")
    {
        var normalizedErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? "AI service unavailable."
            : errorMessage.Contains("AI service unavailable", StringComparison.OrdinalIgnoreCase)
                ? errorMessage
                : $"AI service unavailable. {errorMessage}";

        return new AIInterviewClientResponse
        {
            Success = false,
            ErrorMessage = normalizedErrorMessage,
            Feedback = "AI service unavailable.",
            Completion = "AI service unavailable.",
            RawJson = string.Empty,
            RubricJson = string.Empty,
            Score = null
        };
    }

    protected virtual async Task<AIInterviewClientResponse> CallAzureAsync(AIInterviewClientRequest request, string mode)
    {
        var endpointConfigured = !string.IsNullOrWhiteSpace(_settings?.AzureOpenAiEndpointUrl);
        var apiKeyConfigured = !string.IsNullOrWhiteSpace(_settings?.AzureOpenAiApiKey);
        var deploymentConfigured = !string.IsNullOrWhiteSpace(_settings?.AzureOpenAiDeploymentOrModel);
        if (!endpointConfigured || !apiKeyConfigured || !deploymentConfigured)
        {
            var detail = BuildConfigurationIncompleteLog(mode, endpointConfigured, apiKeyConfigured, deploymentConfigured);
            _logger?.LogWarning("AI service unavailable: Azure OpenAI configuration is incomplete.");
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI unavailable", detail);
            return BuildUnavailableResponse(detail);
        }

        try
        {
            var endpoint = _settings.AzureOpenAiEndpointUrl.TrimEnd('/');
            if (!endpoint.Contains("/openai/deployments/", StringComparison.OrdinalIgnoreCase))
                endpoint = $"{endpoint}/openai/deployments/{_settings.AzureOpenAiDeploymentOrModel.Trim()}/chat/completions?api-version=2024-06-01";
            else if (!endpoint.Contains("api-version=", StringComparison.OrdinalIgnoreCase))
                endpoint += endpoint.Contains('?') ? "&api-version=2024-06-01" : "?api-version=2024-06-01";

            var prompt = BuildPrompt(request, mode);
            var payload = new
            {
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = mode == "generate"
                            ? "Return JSON only. Question mode contract: question, complete:false, optional rubricJson. No markdown. No prose outside JSON."
                            : "Return JSON only. Scoring mode contract: technicalScore, communicationScore, professionalismScore, positiveAttitudeScore, score, feedback, complete, nextQuestion, completion, rubricJson. No markdown. No prose outside JSON. All numeric scores must be integers or decimals from 0 to 100. score must be present and must be the average of the four category scores. feedback must be present. technicalScore, communicationScore, professionalismScore, and positiveAttitudeScore must all be present. nextQuestion must be present unless complete=true. rubricJson should be a JSON object that repeats the category scores and score. Copied question text, irrelevant content, and no-answer responses must receive score 0 and feedback must clearly say the answer was not substantive."
                    },
                    new { role = "user", content = prompt }
                },
                temperature = 0.2,
                max_tokens = 400
            };

            using var httpClient = CreateHttpClient();
            httpClient.DefaultRequestHeaders.Add("api-key", _settings.AzureOpenAiApiKey.Trim());
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var result = await httpClient.PostAsync(endpoint, body);
            var json = await result.Content.ReadAsStringAsync();

            if (!result.IsSuccessStatusCode)
            {
                var detail = BuildAzureHttpFailureLog(mode, endpoint, (int)result.StatusCode, result.ReasonPhrase, json);
                _logger?.LogWarning("Azure OpenAI call failed with status {StatusCode}.", result.StatusCode);
                await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI HTTP failure", detail);
                return BuildUnavailableResponse(detail);
            }

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                var detail = $"Mode={mode}; Reason=empty response choices; Endpoint={BuildSanitizedEndpointValue(endpoint)}; Sample={BuildResponseSnippet(json)}.";
                _logger?.LogWarning("Azure OpenAI call failed. Mode: {Mode}. Reason: Empty choices.", mode);
                await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI contract failure", detail);
                return BuildUnavailableResponse(detail);
            }

            if (!choices[0].TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var contentProperty))
            {
                var detail = $"Mode={mode}; Reason=missing message content; Endpoint={BuildSanitizedEndpointValue(endpoint)}; Sample={BuildResponseSnippet(json)}.";
                _logger?.LogWarning("Azure OpenAI call failed. Mode: {Mode}. Reason: Missing message content.", mode);
                await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI contract failure", detail);
                return BuildUnavailableResponse(detail);
            }

            var content = contentProperty.GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                var detail = $"Mode={mode}; Reason=empty response content; Endpoint={BuildSanitizedEndpointValue(endpoint)}; Sample={BuildResponseSnippet(json)}.";
                _logger?.LogWarning("Azure OpenAI call failed. Mode: {Mode}. Reason: Empty content string.", mode);
                await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI contract failure", detail);
                return BuildUnavailableResponse(detail);
            }

            var parsed = ParseStructuredResponse(content);
            if (parsed != null)
                return parsed;

            var contractReason = BuildStructuredResponseFailureLog(content, mode);
            _logger?.LogWarning("Azure OpenAI call failed. Mode: {Mode}. Reason: Invalid JSON or failed contract parsing.", mode);
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI contract failure", contractReason);
            return BuildUnavailableResponse(contractReason);
        }
        catch (System.Text.Json.JsonException ex)
        {
            var detail = $"Mode={mode}; Reason=invalid JSON format; Exception={ex.GetType().Name}; Message={TruncateSafe(ex.Message, 220)}.";
            _logger?.LogWarning(ex, "Azure OpenAI call failed. Mode: {Mode}. Reason: Invalid JSON format.", mode);
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI JSON failure", detail);
            return BuildUnavailableResponse(detail);
        }
        catch (Exception ex)
        {
            var detail = $"Mode={mode}; Reason={ex.GetType().Name}; Message={TruncateSafe(ex.Message, 220)}.";
            _logger?.LogWarning(ex, "Azure OpenAI call exception.");
            await LogAiClientIssueAsync(NopLogLevel.Error, "AI Interview Azure OpenAI exception", detail);
            return BuildUnavailableResponse(detail);
        }

    }

    protected virtual async Task LogAiClientIssueAsync(NopLogLevel level, string shortMessage, string fullMessage)
    {
        if (_nopLogger == null)
            return;

        var customer = _workContext == null ? null : await _workContext.GetCurrentCustomerAsync();
        await _nopLogger.InsertLogAsync(level, shortMessage, fullMessage, customer);
    }


    protected static string TruncateSafe(string text, int length = 500)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Length <= length ? text : text.Substring(0, length) + "...";
    }

    protected virtual HttpClient CreateHttpClient()
    {
        return _httpClientFactory?.CreateClient(nameof(InterviewAiClient)) ?? new HttpClient();
    }

    protected virtual string BuildPrompt(AIInterviewClientRequest request, string mode)
    {
        var previousQuestions = request.PreviousQuestions.Any()
            ? string.Join("\n", request.PreviousQuestions.Select((q, index) => $"{index + 1}. {q}"))
            : "None";
        var previousScores = request.PreviousScores.Any()
            ? string.Join(", ", request.PreviousScores.Select(score => score.ToString("N0")))
            : "None";
        var previousTurns = request.PreviousTurns.Any()
            ? string.Join("\n", request.PreviousTurns.Select(turn =>
                $"#{turn.SequenceNumber} Q: {TruncateSafe(turn.Question, 120)} | A: {TruncateSafe(turn.Answer, 180)} | Score: {(turn.Score.HasValue ? turn.Score.Value.ToString("N0") : "-")} | Feedback: {TruncateSafe(turn.Feedback, 120)}"))
            : "None";

        return $"""
Interview mode: {mode}
Job title: {request.JobTitle}
Difficulty: {request.Difficulty}
Prompt: {request.Prompt}
Question number: {request.QuestionNumber}
Previous questions: {previousQuestions}
Previous scores: {previousScores}
Previous answered turns:
{previousTurns}
Current question: {request.Question}
Candidate answer: {request.Answer}
Response contract: {(mode == "generate" ? "question, complete:false, optional rubricJson" : "{\"technicalScore\":0-100,\"communicationScore\":0-100,\"professionalismScore\":0-100,\"positiveAttitudeScore\":0-100,\"score\":0-100,\"feedback\":\"string\",\"complete\":false,\"nextQuestion\":\"string or null\",\"completion\":\"string or null\",\"rubricJson\":{\"technicalScore\":0-100,\"communicationScore\":0-100,\"professionalismScore\":0-100,\"positiveAttitudeScore\":0-100,\"score\":0-100}}")}
Scoring rule: copied question text, irrelevant content, or non-substantive answers must receive score 0 with feedback that tells the candidate to answer in their own words.
""";
    }

    protected static string BuildAzureHttpFailureLog(string mode, string endpoint, int statusCode, string reasonPhrase, string responseBody)
    {
        var errorCode = string.Empty;
        var errorMessage = string.Empty;
        var responseSnippet = string.Empty;

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.TryGetProperty("code", out var codeElement))
                        errorCode = codeElement.GetString() ?? string.Empty;
                    if (errorElement.TryGetProperty("message", out var messageElement))
                        errorMessage = TruncateSafe(messageElement.GetString(), 180);
                }
            }
            catch
            {
            }

            responseSnippet = BuildResponseSnippet(responseBody);
        }

        var details = new List<string> { $"Mode={mode}", $"HttpStatus={statusCode}", "Reason=http failure" };
        if (!string.IsNullOrWhiteSpace(reasonPhrase))
            details.Add($"ReasonPhrase={TruncateSafe(reasonPhrase, 80)}");
        details.Add($"Endpoint={BuildSanitizedEndpointValue(endpoint)}");
        if (!string.IsNullOrWhiteSpace(errorCode))
            details.Add($"AzureErrorCode={errorCode}");
        if (!string.IsNullOrWhiteSpace(errorMessage))
            details.Add($"AzureErrorMessage={errorMessage}");
        if (!string.IsNullOrWhiteSpace(responseSnippet))
            details.Add($"ResponseSnippet={responseSnippet}");

        return string.Join("; ", details) + ".";
    }

    protected virtual string BuildConfigurationIncompleteLog(string mode, bool endpointConfigured, bool apiKeyConfigured, bool deploymentConfigured)
    {
        var missingFields = new List<string>();
        if (!endpointConfigured)
            missingFields.Add("AzureOpenAiEndpointUrl");
        if (!apiKeyConfigured)
            missingFields.Add("AzureOpenAiApiKey");
        if (!deploymentConfigured)
            missingFields.Add("DeploymentOrModel");

        return string.Join("; ", new[]
        {
            $"Mode={mode}",
            "Reason=configuration incomplete",
            $"MissingFields={(missingFields.Count > 0 ? string.Join(",", missingFields) : "<none>")}",
            $"MockModeEnabled={(_mockSettings?.UseMockResponses != false).ToString().ToLowerInvariant()}",
            $"EndpointConfigured={endpointConfigured.ToString().ToLowerInvariant()}",
            $"ApiKeyConfigured={apiKeyConfigured.ToString().ToLowerInvariant()}",
            $"DeploymentConfigured={deploymentConfigured.ToString().ToLowerInvariant()}",
            $"EndpointHost={BuildSanitizedEndpointHost(_settings?.AzureOpenAiEndpointUrl)}",
            $"Deployment={BuildSafeValue(_settings?.AzureOpenAiDeploymentOrModel)}"
        }) + ".";
    }

    protected static string BuildSanitizedEndpointHost(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "<empty>";

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? uri.Host
            : TruncateSafe(endpoint.Trim(), 120);
    }

    protected static string BuildSanitizedEndpointValue(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "<empty>";

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return TruncateSafe(endpoint.Trim(), 180);

        var path = uri.AbsolutePath;
        var query = string.IsNullOrWhiteSpace(uri.Query) ? string.Empty : "?api-version=<set>";
        return $"{uri.Host}{path}{query}";
    }

    protected static string BuildSafeValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<empty>" : TruncateSafe(value.Trim(), 120);
    }

    protected static string BuildResponseSnippet(string responseBody)
    {
        return string.IsNullOrWhiteSpace(responseBody)
            ? string.Empty
            : TruncateSafe(responseBody.Replace('\r', ' ').Replace('\n', ' ').Trim(), 220);
    }

    protected static string GetStructuredResponseFailureReason(string content, string mode)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "empty content";

        try
        {
            var normalized = ExtractJsonObjectPayload(content);
            using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;
            if (string.Equals(mode, "generate", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(root.TryGetProperty("question", out var q) ? q.GetString() : null) ? "empty question" : "unknown contract failure";

            var diagnostics = AnalyzeScoreContract(root);
            return diagnostics.Reason;
        }
        catch (JsonException)
        {
            return "invalid JSON";
        }
        catch
        {
            return "invalid JSON or failed contract parsing";
        }
    }

    public static AIInterviewClientResponse ParseStructuredResponse(string content)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            content = ExtractJsonObjectPayload(content);

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var rubricNode = TryParseJsonNode(root, "rubricJson") ?? TryParseJsonNode(root, "rubric");
            var technicalScore = GetScoreValue(root, rubricNode, "technicalScore", "technical", "technical_score");
            var communicationScore = GetScoreValue(root, rubricNode, "communicationScore", "communication", "communication_score");
            var professionalismScore = GetScoreValue(root, rubricNode, "professionalismScore", "professionalism", "professionalism_score");
            var positiveAttitudeScore = GetScoreValue(root, rubricNode, "positiveAttitudeScore", "positiveAttitude", "positive_attitude", "attitude", "positiveAttitudeScore");
            var score = GetScoreValue(root, rubricNode, "score", "overallScore", "overall_score", "totalScore");
            var rubricScores = new[] { technicalScore, communicationScore, professionalismScore, positiveAttitudeScore }
                .Where(value => value.HasValue)
                .Select(value => value.Value)
                .ToList();
            if (!score.HasValue && rubricScores.Count == 4)
                score = Math.Round(rubricScores.Average(), 2);

            string question = root.TryGetProperty("question", out var q) ? q.GetString() : null;
            string nextQuestion = root.TryGetProperty("nextQuestion", out var nq) ? nq.GetString()
                : root.TryGetProperty("optionalNextQuestion", out var onq) ? onq.GetString() : null;
            string feedback = root.TryGetProperty("feedback", out var fb) ? fb.GetString() : null;
            string completion = root.TryGetProperty("completion", out var cmp) ? cmp.GetString() : null;
            string rubricJson = root.TryGetProperty("rubricJson", out var rubricJsonElement) ? rubricJsonElement.GetRawText()
                : root.TryGetProperty("rubric", out var rubricElement) ? rubricElement.GetRawText()
                : null;

            var normalizedRubric = rubricNode as JsonObject;
            if (normalizedRubric == null && (technicalScore.HasValue || communicationScore.HasValue || professionalismScore.HasValue || positiveAttitudeScore.HasValue || score.HasValue))
                normalizedRubric = new JsonObject();

            if (normalizedRubric != null)
            {
                UpsertScoreValue(normalizedRubric, "technicalScore", technicalScore);
                UpsertScoreValue(normalizedRubric, "communicationScore", communicationScore);
                UpsertScoreValue(normalizedRubric, "professionalismScore", professionalismScore);
                UpsertScoreValue(normalizedRubric, "positiveAttitudeScore", positiveAttitudeScore);
                UpsertScoreValue(normalizedRubric, "score", score);
                if (!string.IsNullOrWhiteSpace(feedback))
                    normalizedRubric["feedback"] = feedback;
                rubricJson = normalizedRubric.ToJsonString();
            }

            return new AIInterviewClientResponse
            {
                Success = true,
                Question = question,
                NextQuestion = nextQuestion,
                Score = score,
                TechnicalScore = technicalScore,
                CommunicationScore = communicationScore,
                ProfessionalismScore = professionalismScore,
                PositiveAttitudeScore = positiveAttitudeScore,
                Feedback = feedback,
                Complete = TryParseBoolean(root, "complete"),
                Completion = completion,
                RawJson = content,
                RubricJson = rubricJson
            };
        }
        catch
        {
            return null;
        }
    }

    protected virtual AIInterviewClientResponse BuildValidationFailureResponse(AIInterviewClientResponse response, string errorMessage)
    {
        return new AIInterviewClientResponse
        {
            Success = false,
            ErrorMessage = errorMessage,
            Question = response?.Question,
            NextQuestion = response?.NextQuestion,
            Score = null,
            TechnicalScore = response?.TechnicalScore,
            CommunicationScore = response?.CommunicationScore,
            ProfessionalismScore = response?.ProfessionalismScore,
            PositiveAttitudeScore = response?.PositiveAttitudeScore,
            Feedback = response?.Feedback,
            Complete = response?.Complete ?? false,
            Completion = response?.Completion,
            RawJson = response?.RawJson,
            RubricJson = response?.RubricJson
        };
    }

    protected static decimal? TryParseNullableDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var numericScore))
            return numericScore;

        if (property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var stringScore))
            return stringScore;

        return null;
    }

    protected static decimal? TryParseNullableDecimal(JsonNode node, string propertyName)
    {
        if (node is not JsonObject obj || obj[propertyName] == null)
            return null;

        var valueNode = obj[propertyName];
        if (valueNode is JsonValue value)
        {
            if (value.TryGetValue<decimal>(out var numericValue))
                return numericValue;

            if (value.TryGetValue<string>(out var stringValue) &&
                decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        return null;
    }

    protected static JsonNode TryParseJsonNode(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        try
        {
            return property.ValueKind == JsonValueKind.String
                ? JsonNode.Parse(property.GetString() ?? string.Empty)
                : JsonNode.Parse(property.GetRawText());
        }
        catch
        {
            return null;
        }
    }

    protected static decimal? GetScoreValue(JsonElement root, JsonNode rubricNode, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var direct = TryParseNullableDecimal(root, propertyName);
            if (direct.HasValue)
                return Math.Clamp(direct.Value, 0, 100);

            var rubric = TryParseNullableDecimal(rubricNode, propertyName);
            if (rubric.HasValue)
                return Math.Clamp(rubric.Value, 0, 100);
        }

        return null;
    }

    protected static string ExtractJsonObjectPayload(string content)
    {
        var normalized = content?.Trim() ?? string.Empty;
        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = normalized.IndexOf('{');
            var lastBrace = normalized.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
                normalized = normalized.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        var start = normalized.IndexOf('{');
        var end = normalized.LastIndexOf('}');
        if (start >= 0 && end > start)
            normalized = normalized.Substring(start, end - start + 1);

        return normalized;
    }

    protected sealed record ScoreContractDiagnostics(string Reason, string Shape, string PropertyNames, string Sample, string MissingFields);

    protected static ScoreContractDiagnostics AnalyzeScoreContract(JsonElement root)
    {
        var rubricNode = TryParseJsonNode(root, "rubricJson") ?? TryParseJsonNode(root, "rubric");
        var technicalScore = GetScoreValue(root, rubricNode, "technicalScore", "technical", "technical_score");
        var communicationScore = GetScoreValue(root, rubricNode, "communicationScore", "communication", "communication_score");
        var professionalismScore = GetScoreValue(root, rubricNode, "professionalismScore", "professionalism", "professionalism_score");
        var positiveAttitudeScore = GetScoreValue(root, rubricNode, "positiveAttitudeScore", "positiveAttitude", "positive_attitude", "attitude");
        var score = GetScoreValue(root, rubricNode, "score", "overallScore", "overall_score", "totalScore");
        var feedback = root.TryGetProperty("feedback", out var feedbackElement) ? feedbackElement.GetString() : null;
        var nextQuestion = root.TryGetProperty("nextQuestion", out var nextQuestionElement) ? nextQuestionElement.GetString()
            : root.TryGetProperty("optionalNextQuestion", out var optionalNextQuestionElement) ? optionalNextQuestionElement.GetString() : null;
        var complete = TryParseBoolean(root, "complete");
        var propertyNames = root.ValueKind == JsonValueKind.Object
            ? string.Join(",", root.EnumerateObject().Select(property => property.Name))
            : string.Empty;

        var missingFields = new List<string>();
        if (!score.HasValue)
            missingFields.Add("score");
        if (!technicalScore.HasValue)
            missingFields.Add("technicalScore");
        if (!communicationScore.HasValue)
            missingFields.Add("communicationScore");
        if (!professionalismScore.HasValue)
            missingFields.Add("professionalismScore");
        if (!positiveAttitudeScore.HasValue)
            missingFields.Add("positiveAttitudeScore");
        if (string.IsNullOrWhiteSpace(feedback))
            missingFields.Add("feedback");
        if (!complete && string.IsNullOrWhiteSpace(nextQuestion))
            missingFields.Add("nextQuestion");

        var reason = "invalid JSON or failed contract parsing";
        if (!score.HasValue)
            reason = "missing score";
        else if (!technicalScore.HasValue || !communicationScore.HasValue || !professionalismScore.HasValue || !positiveAttitudeScore.HasValue)
            reason = "missing category score";
        else if (string.IsNullOrWhiteSpace(feedback))
            reason = "missing feedback";
        else if (!complete && string.IsNullOrWhiteSpace(nextQuestion))
            reason = "missing next question";

        return new ScoreContractDiagnostics(reason, root.ValueKind.ToString(), propertyNames, TruncateSafe(root.GetRawText(), 800), missingFields.Count > 0 ? string.Join(",", missingFields) : "<none>");
    }

    protected static string BuildStructuredResponseFailureLog(string content, string mode)
    {
        var shape = "empty";
        var sample = TruncateSafe(content, 800);
        try
        {
            var normalized = ExtractJsonObjectPayload(content);
            using var document = JsonDocument.Parse(normalized);
            var diagnostics = string.Equals(mode, "score", StringComparison.OrdinalIgnoreCase)
                ? AnalyzeScoreContract(document.RootElement)
                : new ScoreContractDiagnostics(GetStructuredResponseFailureReason(content, mode), document.RootElement.ValueKind.ToString(),
                    document.RootElement.ValueKind == JsonValueKind.Object ? string.Join(",", document.RootElement.EnumerateObject().Select(property => property.Name)) : string.Empty,
                    TruncateSafe(document.RootElement.GetRawText(), 800),
                    string.IsNullOrWhiteSpace(document.RootElement.TryGetProperty("question", out var questionElement) ? questionElement.GetString() : null) ? "question" : "<none>");

            return $"Mode={mode}; Reason={diagnostics.Reason}; MissingFields={diagnostics.MissingFields}; Shape={diagnostics.Shape}; PropertyNames={diagnostics.PropertyNames}; Sample={diagnostics.Sample}.";
        }
        catch
        {
            if (string.IsNullOrWhiteSpace(content))
                shape = "empty";
            else if (content.TrimStart().StartsWith("```", StringComparison.Ordinal))
                shape = "markdown fenced JSON";
            else if (content.Contains('{'))
                shape = "malformed object-like text";
            else
                shape = "plain text";

            return $"Mode={mode}; Reason={GetStructuredResponseFailureReason(content, mode)}; Shape={shape}; Sample={sample}.";
        }
    }

    protected static string BuildScoreValidationFailureLog(AIInterviewClientResponse response)
    {
        var missingFields = new List<string>();
        if (response == null || !response.Score.HasValue)
            missingFields.Add("score");
        if (response == null || !response.TechnicalScore.HasValue)
            missingFields.Add("technicalScore");
        if (response == null || !response.CommunicationScore.HasValue)
            missingFields.Add("communicationScore");
        if (response == null || !response.ProfessionalismScore.HasValue)
            missingFields.Add("professionalismScore");
        if (response == null || !response.PositiveAttitudeScore.HasValue)
            missingFields.Add("positiveAttitudeScore");
        if (string.IsNullOrWhiteSpace(response?.Feedback))
            missingFields.Add("feedback");
        if (response != null && !response.Complete && string.IsNullOrWhiteSpace(response.NextQuestion))
            missingFields.Add("nextQuestion");

        var reason = response == null || !response.Score.HasValue ? "missing required score"
            : !response.TechnicalScore.HasValue || !response.CommunicationScore.HasValue || !response.ProfessionalismScore.HasValue || !response.PositiveAttitudeScore.HasValue ? "missing category score"
            : string.IsNullOrWhiteSpace(response.Feedback) ? "missing feedback"
            : !response.Complete && string.IsNullOrWhiteSpace(response.NextQuestion) ? "missing next question"
            : "invalid score contract";

        var sample = TruncateSafe(response?.RawJson, 800);
        return $"Mode=score; Reason={reason}; MissingFields={(missingFields.Count > 0 ? string.Join(",", missingFields) : "<none>")}; Sample={sample}.";
    }

    protected static void UpsertScoreValue(JsonObject rubric, string propertyName, decimal? value)
    {
        if (value.HasValue)
            rubric[propertyName] = value.Value;
    }

    protected static decimal? ParseRubricScore(string rubricJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rubricJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(rubricJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
                JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return false;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
            _ => false
        };
    }

    protected virtual AIInterviewClientResponse BuildMockQuestion(AIInterviewClientRequest request)
    {
        var questionNumber = Math.Max(1, request.QuestionNumber);
        var question = questionNumber switch
        {
            1 => $"Tell me about your background for {request.JobTitle}.",
            2 => $"How would you handle a difficult problem in a {request.Difficulty} {request.JobTitle} role?",
            3 => $"What would you prioritize in your first 30 days as a {request.JobTitle}?",
            _ => $"Share a situation where you improved outcomes in a {request.JobTitle} role."
        };

            return new AIInterviewClientResponse
            {
                Success = true,
                Question = question,
                NextQuestion = question,
                Score = null,
            Feedback = string.Empty,
            Complete = false,
            Completion = string.Empty,
            RawJson = JsonSerializer.Serialize(new { question, complete = false }),
            RubricJson = "{}"
        };
    }

    protected virtual AIInterviewClientResponse BuildMockScore(AIInterviewClientRequest request)
    {
        var words = (request.Answer ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var score = Math.Min(100, Math.Max(10, (words.Length * 8) + (request.Answer?.Contains("because", StringComparison.OrdinalIgnoreCase) == true ? 10 : 0)));
        var feedback = score >= 75
            ? "Strong answer with clear structure."
            : score >= 50
                ? "Decent answer. Add more concrete examples."
                : "Answer is too brief. Explain your reasoning and impact.";
        var technicalScore = score;
        var communicationScore = Math.Clamp(score - 4, 0, 100);
        var professionalismScore = Math.Clamp(score + 2, 0, 100);
        var positiveAttitudeScore = Math.Clamp(score + 1, 0, 100);
        var averageScore = Math.Round((technicalScore + communicationScore + professionalismScore + positiveAttitudeScore) / 4m, 2);

        return new AIInterviewClientResponse
        {
            Success = true,
            Question = string.Empty,
            NextQuestion = string.Empty,
            Score = averageScore,
            TechnicalScore = technicalScore,
            CommunicationScore = communicationScore,
            ProfessionalismScore = professionalismScore,
            PositiveAttitudeScore = positiveAttitudeScore,
            Feedback = feedback,
            Complete = false,
            Completion = string.Empty,
            RawJson = JsonSerializer.Serialize(new
            {
                technicalScore,
                communicationScore,
                professionalismScore,
                positiveAttitudeScore,
                score = averageScore,
                feedback,
                complete = false
            }),
            RubricJson = JsonSerializer.Serialize(new
            {
                technicalScore,
                communicationScore,
                professionalismScore,
                positiveAttitudeScore,
                score = averageScore,
                feedback
            })
        };
    }
}

public class InterviewRuntimeService : IInterviewRuntimeService
{
    private readonly IInterviewSessionService _sessionService;
    private readonly IInterviewTurnService _turnService;
    private readonly IAIInterviewClient _aiClient;
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;
    private readonly ILocalizationService _localizationService;
    private readonly AIInterviewSettings _settings;
    private readonly MockAIInterviewSettings _mockSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWorkContext _workContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly NopLogger _nopLogger;
    private readonly ILogger<InterviewRuntimeService> _logger;

    public InterviewRuntimeService(
        IInterviewSessionService sessionService,
        IInterviewTurnService turnService,
        IAIInterviewClient aiClient,
        IProductService productService,
        ICustomerService customerService,
        ILocalizationService localizationService,
        AIInterviewSettings settings,
        MockAIInterviewSettings mockSettings,
        IHttpClientFactory httpClientFactory,
        IWorkContext workContext,
        IEventPublisher eventPublisher = null,
        NopLogger nopLogger = null,
        ILogger<InterviewRuntimeService> logger = null)
    {
        _sessionService = sessionService;
        _turnService = turnService;
        _aiClient = aiClient;
        _productService = productService;
        _customerService = customerService;
        _localizationService = localizationService;
        _settings = settings;
        _mockSettings = mockSettings;
        _httpClientFactory = httpClientFactory;
        _workContext = workContext;
        _eventPublisher = eventPublisher;
        _nopLogger = nopLogger;
        _logger = logger;
    }

    protected virtual Task LogRuntimeIssueAsync(NopLogLevel level, string shortMessage, string fullMessage = "", Customer customer = null)
    {
        return _nopLogger == null ? Task.CompletedTask : _nopLogger.InsertLogAsync(level, shortMessage, fullMessage, customer);
    }

    protected virtual async Task<Customer> ResolveLogCustomerAsync(InterviewSession session = null, Customer customer = null)
    {
        if (customer != null)
            return customer;

        if (session?.CustomerId > 0)
        {
            var sessionCustomer = await _customerService.GetCustomerByIdAsync(session.CustomerId);
            if (sessionCustomer != null)
                return sessionCustomer;
        }

        return _workContext == null ? null : await _workContext.GetCurrentCustomerAsync();
    }


    protected static string TruncateSafe(string text, int length = 500)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Length <= length ? text : text.Substring(0, length) + "...";
    }

    protected static decimal? ParseRubricScore(string rubricJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rubricJson) || string.IsNullOrWhiteSpace(propertyName))
            return null;

        try
        {
            using var document = JsonDocument.Parse(rubricJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
                JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    protected static string BuildSafeValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<empty>" : TruncateSafe(value.Trim(), 220);
    }

    protected async Task<string> GetLocalizedTextAsync(string resourceKey, string fallback)
    {
        var text = await _localizationService.GetResourceAsync(resourceKey);
        if (string.IsNullOrWhiteSpace(text) ||
            string.Equals(text.Trim(), resourceKey, StringComparison.OrdinalIgnoreCase))
            return fallback;

        return text;
    }

    protected virtual HttpClient CreateHttpClient()
    {
        return _httpClientFactory?.CreateClient(nameof(InterviewRuntimeService)) ?? new HttpClient();
    }

    public async Task<InterviewRuntimeModel> GetRuntimeModelAsync(string token)
    {
        var session = await _sessionService.GetSessionByTokenAsync(token);
        if (session == null)
            return null;

        var customer = session.CustomerId > 0 ? await _customerService.GetCustomerByIdAsync(session.CustomerId) : null;
        var turns = await _turnService.GetTurnsBySessionIdAsync(session.Id);
        var model = await BuildRuntimeModelAsync(session, turns, customer);
        if (model == null)
            return null;

        model.CurrentQuestion = string.Empty;
        model.Turns = Array.Empty<InterviewTurnViewModel>();
        return model;
    }

    public async Task<InterviewRuntimeModel> BeginInterviewAsync(string token, Customer customer = null)
    {
        var session = await _sessionService.GetSessionByTokenAsync(token);
        if (!IsSessionUsable(session, DateTime.UtcNow))
            return null;

        return await EnsureInterviewStartedAsync(session, customer);
    }

    public async Task<InterviewRuntimeModel> EnsureInterviewStartedAsync(InterviewSession session, Customer customer = null)
    {
        if (session == null)
            return null;

        var turns = await _turnService.GetTurnsBySessionIdAsync(session.Id);
        if (!turns.Any())
        {
            var firstResult = await GenerateQuestionTurnAsync(session, 1, turns);
            if (firstResult.Turn == null)
            {
                var unavailableModel = await BuildRuntimeModelAsync(session, turns, customer);
                unavailableModel.CurrentQuestion = "AI service unavailable. Please try again later.";
                unavailableModel.ClientSettings ??= new RuntimeClientSettingsModel();
                unavailableModel.ClientSettings.SpeechAvailable = false;
                var logCustomer = await ResolveLogCustomerAsync(session, customer);
                await LogRuntimeIssueAsync(
                    NopLogLevel.Warning,
                    "AI Interview first question unavailable",
                    $"Mode=generate; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; Reason=first question generation failed; Detail={BuildSafeValue(firstResult.FailureReason ?? "AI service unavailable.")}.",
                    logCustomer);
                return unavailableModel;
            }

            await _turnService.InsertInterviewTurnAsync(firstResult.Turn);
            turns = (await _turnService.GetTurnsBySessionIdAsync(session.Id)).ToList();
        }

        return await BuildRuntimeModelAsync(session, turns, customer);
    }

    public async Task<SubmitInterviewAnswerResponse> SubmitAnswerAsync(string token, string answer)
    {
        var session = await _sessionService.GetSessionByTokenAsync(token);
        var now = DateTime.UtcNow;
        if (!IsSessionUsable(session, now))
        {
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.")
            };
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidAnswer", "Answer cannot be empty.")
            };
        }

        var turns = (await _turnService.GetTurnsBySessionIdAsync(session.Id)).ToList();
        var currentTurn = turns.LastOrDefault(turn => string.IsNullOrWhiteSpace(turn.AnswerText)) ?? turns.LastOrDefault();
        if (currentTurn == null)
        {
            await LogRuntimeIssueAsync(
                NopLogLevel.Warning,
                "AI Interview submit before begin",
                $"Mode=score; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; Reason=submit before begin.",
                await ResolveLogCustomerAsync(session));
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = "Interview has not started. Click Start Interview to begin."
            };
        }

        var answerValidationMessage = await ValidateAnswerAsync(currentTurn.QuestionText, answer);
        if (!string.IsNullOrWhiteSpace(answerValidationMessage))
        {
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = answerValidationMessage
            };
        }

        var evaluation = await _aiClient.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = await GetJobTitleAsync(session.ProductId),
            Difficulty = session.Difficulty,
            Prompt = _settings.Prompt,
            Question = currentTurn.QuestionText,
            Answer = answer,
            QuestionNumber = currentTurn.SequenceNumber,
            PreviousQuestions = turns.Select(turn => turn.QuestionText).ToList(),
            PreviousScores = turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList(),
            PreviousTurns = BuildPreviousTurnContext(turns, currentTurn)
        });

        if (evaluation == null || !evaluation.Success || !evaluation.Score.HasValue)
        {
            _logger?.LogWarning("SubmitAnswer score failure for session {SessionId}. Reason: {Reason}.",
                session.Id, evaluation?.ErrorMessage ?? "Invalid format/range");
            await LogRuntimeIssueAsync(
                NopLogLevel.Warning,
                "AI Interview scoring failure",
                $"Mode=score; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; Reason={evaluation?.ErrorMessage ?? "missing required score"}.",
                await ResolveLogCustomerAsync(session));
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = "The AI interview service is temporarily unavailable. Please try again later."
            };
        }

        currentTurn.AnswerText = answer;
        currentTurn.Score = Math.Clamp(evaluation.Score.Value, 0, 100);
        currentTurn.Feedback = evaluation.Feedback;
        currentTurn.RubricJson = evaluation.RubricJson;
        currentTurn.RawAIResponseJson = evaluation.RawJson;
        currentTurn.AnsweredOnUtc = DateTime.UtcNow;
        await _turnService.UpdateInterviewTurnAsync(currentTurn);

        turns = (await _turnService.GetTurnsBySessionIdAsync(session.Id)).ToList();
        var averageScore = turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).DefaultIfEmpty(0).Average();
        session.Score = averageScore;
        session.QuestionScores = JsonSerializer.Serialize(turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList());

        var maxQuestions = GetMaxQuestions(session);
        var answeredCount = turns.Count(turn => !string.IsNullOrWhiteSpace(turn.AnswerText));
        var aiRequestedCompletion = evaluation.Complete;
        var shouldComplete = aiRequestedCompletion || answeredCount >= maxQuestions;
        if (!shouldComplete)
        {
            InterviewTurn nextTurn;
            var nextQuestionText = evaluation.NextQuestion ?? evaluation.Question;
            if (!string.IsNullOrWhiteSpace(nextQuestionText))
            {
                nextTurn = new InterviewTurn
                {
                    InterviewSessionId = session.Id,
                    SequenceNumber = currentTurn.SequenceNumber + 1,
                    QuestionId = currentTurn.SequenceNumber + 1,
                    QuestionText = nextQuestionText,
                    AskedOnUtc = DateTime.UtcNow,
                    CreatedOnUtc = DateTime.UtcNow,
                    RawAIResponseJson = evaluation.RawJson,
                    RubricJson = evaluation.RubricJson
                };
            }
            else
            {
                var nextTurnResult = await GenerateQuestionTurnAsync(session, currentTurn.SequenceNumber + 1, turns);
                nextTurn = nextTurnResult.Turn;
                if (nextTurn == null)
                {
                    await LogRuntimeIssueAsync(
                        NopLogLevel.Warning,
                        "AI Interview next question unavailable",
                        $"Mode=generate; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; Reason=next question generation failed; Detail={BuildSafeValue(nextTurnResult.FailureReason ?? "AI service unavailable.")}.",
                        await ResolveLogCustomerAsync(session));
                }
            }

            if (nextTurn == null)
            {
                return new SubmitInterviewAnswerResponse
                {
                    Success = false,
                    Message = "The AI interview service is temporarily unavailable. Please try again later.",
                    Feedback = "The AI interview service is temporarily unavailable. Please try again later."
                };
            }

            await _turnService.InsertInterviewTurnAsync(nextTurn);
            await _sessionService.UpdateInterviewSessionAsync(session);

            return new SubmitInterviewAnswerResponse
            {
                Success = true,
                IsTerminated = false,
                Question = nextTurn.QuestionText,
                Score = session.Score,
                Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Interview.NextQuestion"),
                Interrupted = false,
                Completion = string.Empty,
                Turn = new InterviewTurnViewModel
                {
                    TurnId = currentTurn.Id,
                    SequenceNumber = currentTurn.SequenceNumber,
                    QuestionText = currentTurn.QuestionText,
                    AnswerText = currentTurn.AnswerText,
                    AskedOnUtc = currentTurn.AskedOnUtc,
                    AnsweredOnUtc = currentTurn.AnsweredOnUtc
                }
            };
        }

        var completion = await CompleteInterviewInternalAsync(session, turns, evaluation.Completion ?? evaluation.Feedback ?? evaluation.ErrorMessage, evaluation.Completion);
        return new SubmitInterviewAnswerResponse
        {
            Success = true,
            IsTerminated = true,
            Completion = completion.Completion,
            Score = completion.Score,
            Message = completion.Message,
            ReportUrl = completion.ReportUrl,
            Interrupted = false,
            Question = string.Empty,
            Turn = new InterviewTurnViewModel
            {
                TurnId = currentTurn.Id,
                SequenceNumber = currentTurn.SequenceNumber,
                QuestionText = currentTurn.QuestionText,
                AnswerText = currentTurn.AnswerText,
                AskedOnUtc = currentTurn.AskedOnUtc,
                AnsweredOnUtc = currentTurn.AnsweredOnUtc
            }
        };
    }

    public async Task<CompleteInterviewResponse> CompleteInterviewAsync(string token, string reason = null)
    {
        var session = await _sessionService.GetSessionByTokenAsync(token);
        var now = DateTime.UtcNow;
        if (!IsSessionUsable(session, now))
        {
            return new CompleteInterviewResponse
            {
                Success = false,
                Message = await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.")
            };
        }

        var turns = await _turnService.GetTurnsBySessionIdAsync(session.Id);
        if (turns == null || !turns.Any())
        {
            return new CompleteInterviewResponse
            {
                Success = false,
                Message = "Interview has not started. Click Start Interview to begin."
            };
        }

        return await CompleteInterviewInternalAsync(session, turns, reason);
    }

    public async Task<SpeechTokenResponseModel> GetSpeechTokenAsync(string token)
    {
        var session = await _sessionService.GetSessionByTokenAsync(token);
        var now = DateTime.UtcNow;
        if (!IsSessionUsable(session, now) ||
            string.IsNullOrWhiteSpace(_settings?.AzureSpeechKey) ||
            string.IsNullOrWhiteSpace(_settings?.AzureSpeechRegion))
        {
            await LogRuntimeIssueAsync(
                NopLogLevel.Warning,
                "AI Interview speech token unavailable",
                $"Mode=speech-token; Reason=configuration incomplete; SessionId={session?.Id ?? 0}; ProductId={session?.ProductId ?? 0}; CustomerId={session?.CustomerId ?? 0}; SpeechKeyConfigured={(!string.IsNullOrWhiteSpace(_settings?.AzureSpeechKey)).ToString().ToLowerInvariant()}; SpeechRegionConfigured={(!string.IsNullOrWhiteSpace(_settings?.AzureSpeechRegion)).ToString().ToLowerInvariant()}; SpeechRegion={BuildSafeValue(_settings?.AzureSpeechRegion)}.",
                await ResolveLogCustomerAsync(session));
            return null;
        }

        try
        {
            using var httpClient = CreateHttpClient();
            httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _settings.AzureSpeechKey.Trim());
            var endpoint = $"https://{_settings.AzureSpeechRegion.Trim()}.api.cognitive.microsoft.com/sts/v1.0/issuetoken";
            var response = await httpClient.PostAsync(endpoint, new StringContent(string.Empty));
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Azure Speech token request failed. Region: {Region}. Status: {StatusCode}.",
                    _settings.AzureSpeechRegion.Trim(), response.StatusCode);
                await LogRuntimeIssueAsync(
                    NopLogLevel.Warning,
                    "AI Interview speech token failure",
                    $"Mode=speech-token; Reason=http failure; SessionId={session?.Id ?? 0}; ProductId={session?.ProductId ?? 0}; CustomerId={session?.CustomerId ?? 0}; Region={BuildSafeValue(_settings.AzureSpeechRegion)}; HttpStatus={(int)response.StatusCode}; ReasonPhrase={BuildSafeValue(response.ReasonPhrase)}.",
                    await ResolveLogCustomerAsync(session));
                return null;
            }

            var tokenValue = (await response.Content.ReadAsStringAsync())?.Trim();
            if (string.IsNullOrWhiteSpace(tokenValue))
                return null;

            return new SpeechTokenResponseModel
            {
                Token = tokenValue,
                Region = _settings.AzureSpeechRegion.Trim(),
                ExpiresInSeconds = 600
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Azure Speech token request exception. Region: {Region}.", _settings.AzureSpeechRegion.Trim());
            await LogRuntimeIssueAsync(
                NopLogLevel.Error,
                "AI Interview speech token exception",
                $"Mode=speech-token; Reason={ex.GetType().Name}; SessionId={session?.Id ?? 0}; ProductId={session?.ProductId ?? 0}; CustomerId={session?.CustomerId ?? 0}; Region={BuildSafeValue(_settings.AzureSpeechRegion)}; Message={TruncateSafe(ex.Message, 220)}.",
                await ResolveLogCustomerAsync(session));
            return null;
        }
    }

    public async Task<RecordingUploadResponseModel> UploadRecordingAsync(string token, IFormFile recording)
    {
        var session = await _sessionService.GetSessionByTokenAsync(token);
        var now = DateTime.UtcNow;
        var normalizedContentType = NormalizeRecordingContentType(recording?.ContentType);
        if (!CanUploadRecording(session, token, now))
        {
            await LogRecordingUploadFailureAsync(session, recording, null, "Invalid or expired session token.", "validation failed", normalizedContentType: normalizedContentType);
            return RecordingFailure("Invalid or expired session token.");
        }

        if (recording == null || recording.Length <= 0)
        {
            await LogRecordingUploadFailureAsync(session, recording, null, "Recording file is empty.", "empty recording", normalizedContentType: normalizedContentType);
            return RecordingFailure("Recording file is empty.");
        }

        const long maxRecordingBytes = 100L * 1024L * 1024L;
        if (recording.Length > maxRecordingBytes)
        {
            await LogRecordingUploadFailureAsync(session, recording, null, "Recording file is too large.", "recording too large", normalizedContentType: normalizedContentType);
            return RecordingFailure("Recording file is too large.");
        }

        if (string.IsNullOrWhiteSpace(_settings?.AzureBlobStorageContainerUrl) ||
            string.IsNullOrWhiteSpace(_settings?.AzureBlobStorageSasToken))
        {
            await LogRecordingUploadFailureAsync(session, recording, null, "Recording storage is not configured.", "missing Azure Blob configuration", normalizedContentType: normalizedContentType);
            return RecordingFailure("Recording storage is not configured.");
        }

        var containerUrl = _settings.AzureBlobStorageContainerUrl.Trim().TrimEnd('/');
        var sasToken = _settings.AzureBlobStorageSasToken.Trim();
        if (!sasToken.StartsWith("?", StringComparison.Ordinal))
            sasToken = sasToken.StartsWith("&", StringComparison.Ordinal) ? "?" + sasToken[1..] : "?" + sasToken;

        var blobName = $"recordings-{session.SessionKey}-{DateTime.UtcNow:yyyyMMddHHmmss}.webm";
        var uploadUrl = $"{containerUrl}/{Uri.EscapeDataString(blobName)}{sasToken}";

        try
        {
            await LogRecordingUploadStartAsync(session, recording, blobName, normalizedContentType);
            using var httpClient = CreateHttpClient();
            using var content = new StreamContent(recording.OpenReadStream());
            content.Headers.ContentType = new MediaTypeHeaderValue(normalizedContentType);

            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
            {
                Content = content
            };
            request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");

            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                await LogRecordingUploadFailureAsync(session, recording, blobName, "Recording upload failed.", $"Azure Blob PUT returned {(int)response.StatusCode}.", (int)response.StatusCode, errorBody, normalizedContentType);
                return RecordingFailure("Recording upload failed.");
            }

            session.RecordingUrl = $"{containerUrl}/{Uri.EscapeDataString(blobName)}";
            await _sessionService.UpdateInterviewSessionAsync(session);
            await _sessionService.EnsureRecordingShareTokenAsync(session);
            await LogRecordingUploadSuccessAsync(session, recording, blobName, (int)response.StatusCode, normalizedContentType);

            return new RecordingUploadResponseModel
            {
                Success = true,
                Message = "Recording uploaded successfully.",
                RecordingUrl = session.RecordingUrl
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Recording upload failed for session {SessionId}, customer {CustomerId}, product {ProductId}.",
                session?.Id ?? 0, session?.CustomerId ?? 0, session?.ProductId ?? 0);
            await LogRecordingUploadFailureAsync(session, recording, blobName, "Recording upload failed.", ex.ToString(), normalizedContentType: normalizedContentType);
            return RecordingFailure("Recording upload failed.");
        }
    }

    protected virtual bool IsSessionUsable(InterviewSession session, DateTime utcNow)
    {
        return session != null &&
            session.IsActive &&
            !session.CompletedOnUtc.HasValue &&
            (!session.TokenExpiryUtc.HasValue || session.TokenExpiryUtc > utcNow);
    }

    protected virtual bool CanUploadRecording(InterviewSession session, string token, DateTime utcNow)
    {
        if (session == null || string.IsNullOrWhiteSpace(token) || !string.Equals(session.Token, token, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(session.RecordingUrl))
            return false;

        if (session.IsActive && !session.CompletedOnUtc.HasValue && (!session.TokenExpiryUtc.HasValue || session.TokenExpiryUtc > utcNow))
            return true;

        if (session.CompletedOnUtc.HasValue && session.CompletedOnUtc.Value >= utcNow.AddMinutes(-10))
            return true;

        return false;
    }

    protected virtual async Task<InterviewRuntimeModel> BuildRuntimeModelAsync(InterviewSession session, IList<InterviewTurn> turns, Customer customer = null)
    {
        var product = session.ProductId > 0 ? await _productService.GetProductByIdAsync(session.ProductId) : null;
        var candidate = customer ?? await _customerService.GetCustomerByIdAsync(session.CustomerId);
        var currentTurn = turns.LastOrDefault(turn => string.IsNullOrWhiteSpace(turn.AnswerText)) ?? turns.LastOrDefault();
        var lastQuestion = currentTurn?.QuestionText ?? string.Empty;

        return new InterviewRuntimeModel
        {
            SessionId = session.Id,
            ProductId = session.ProductId,
            SessionKey = session.SessionKey,
            Token = session.Token,
            ProductName = product?.Name ?? "Interview",
            CandidateName = candidate != null ? $"{candidate.FirstName} {candidate.LastName}".Trim() : string.Empty,
            Difficulty = session.Difficulty,
            CurrentQuestion = lastQuestion,
            Score = session.Score,
            IsCompleted = session.CompletedOnUtc.HasValue,
            IsMockMode = _mockSettings?.UseMockResponses ?? true,
            ReportUrl = string.Empty,
            TokenExpiryUtc = session.TokenExpiryUtc,
            Turns = turns.Select(turn => new InterviewTurnViewModel
            {
                TurnId = turn.Id,
                SequenceNumber = turn.SequenceNumber,
                QuestionText = turn.QuestionText,
                AnswerText = turn.AnswerText,
                AskedOnUtc = turn.AskedOnUtc,
                AnsweredOnUtc = turn.AnsweredOnUtc
            }).ToList(),
            ClientSettings = new RuntimeClientSettingsModel
            {
                SpeechRegion = _settings.AzureSpeechRegion,
                SpeechVoiceName = string.Empty,
                ProductName = product?.Name,
                Token = session.Token,
                SpeechAvailable = !string.IsNullOrWhiteSpace(_settings.AzureSpeechKey) && !string.IsNullOrWhiteSpace(_settings.AzureSpeechRegion),
                RecordingUploadUrl = string.Empty,
                RecordingAvailable = !string.IsNullOrWhiteSpace(_settings.AzureBlobStorageContainerUrl) && !string.IsNullOrWhiteSpace(_settings.AzureBlobStorageSasToken)
            }
        };
    }

    protected virtual async Task<(InterviewTurn Turn, string FailureReason)> GenerateQuestionTurnAsync(InterviewSession session, int sequenceNumber, IList<InterviewTurn> turns)
    {
        var request = new AIInterviewClientRequest
        {
            JobTitle = await GetJobTitleAsync(session.ProductId),
            Difficulty = session.Difficulty,
            Prompt = _settings.Prompt,
            QuestionNumber = sequenceNumber,
            PreviousQuestions = turns.Select(turn => turn.QuestionText).ToList(),
            PreviousScores = turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList(),
            PreviousTurns = BuildPreviousTurnContext(turns)
        };

        var aiResponse = await _aiClient.GenerateQuestionAsync(request);
        if (aiResponse == null || !aiResponse.Success || string.IsNullOrWhiteSpace(aiResponse.Question))
        {
            _logger?.LogWarning("GenerateQuestion failure for session {SessionId}. Reason: {Reason}.",
                session.Id, aiResponse?.ErrorMessage ?? "Invalid format");
            return (null, aiResponse?.ErrorMessage ?? "Invalid format");
        }

        return (new InterviewTurn
        {
            InterviewSessionId = session.Id,
            SequenceNumber = sequenceNumber,
            QuestionId = sequenceNumber,
            QuestionText = aiResponse.Question,
            AskedOnUtc = DateTime.UtcNow,
            CreatedOnUtc = DateTime.UtcNow,
            RawAIResponseJson = aiResponse.RawJson,
            RubricJson = aiResponse.RubricJson
        }, null);
    }

    protected virtual async Task<CompleteInterviewResponse> CompleteInterviewInternalAsync(InterviewSession session, IList<InterviewTurn> turns, string reason, string aiCompletion = null)
    {
        _logger?.LogInformation("Stop called with session id {SessionId}", session.Id);

        if (session.CompletedOnUtc.HasValue || !session.IsActive)
        {
            return new CompleteInterviewResponse
            {
                Success = false,
                IsTerminated = true,
                Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken")
            };
        }

        session.IsActive = false;
        session.CompletedOnUtc = DateTime.UtcNow;
        session.Score = turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).DefaultIfEmpty(0).Average();
        session.QuestionScores = JsonSerializer.Serialize(turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList());
        session.ReportData = BuildReport(turns, session.Score, reason, aiCompletion);
        await _sessionService.UpdateInterviewSessionAsync(session);

        await PublishCompletionAsync(session);

        var completion = new CompleteInterviewResponse
        {
            Success = true,
            IsTerminated = true,
            Score = session.Score,
            Feedback = turns.LastOrDefault()?.Feedback ?? reason ?? string.Empty,
            Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Interview.CompletedScore"),
            Completion = session.ReportData,
            ReportUrl = string.Empty,
            Turns = turns.Select(turn => new InterviewTurnViewModel
            {
                TurnId = turn.Id,
                SequenceNumber = turn.SequenceNumber,
                QuestionText = turn.QuestionText,
                AnswerText = turn.AnswerText,
                Score = turn.Score,
                TechnicalScore = ParseRubricScore(turn.RubricJson, "technicalScore"),
                CommunicationScore = ParseRubricScore(turn.RubricJson, "communicationScore"),
                ProfessionalismScore = ParseRubricScore(turn.RubricJson, "professionalismScore"),
                PositiveAttitudeScore = ParseRubricScore(turn.RubricJson, "positiveAttitudeScore"),
                Feedback = turn.Feedback,
                AskedOnUtc = turn.AskedOnUtc,
                AnsweredOnUtc = turn.AnsweredOnUtc
            }).ToList()
        };

        _logger?.LogInformation("Interview completed for session {SessionId}, customer {CustomerId}, product {ProductId}.",
            session.Id, session.CustomerId, session.ProductId);

        return completion;
    }

    protected virtual async Task PublishCompletionAsync(InterviewSession session)
    {
        if (_eventPublisher == null)
            return;

        var workingLanguage = _workContext == null ? null : await _workContext.GetWorkingLanguageAsync();
        var languageId = workingLanguage?.Id ?? 0;
        await _eventPublisher.PublishAsync(new MockAiInterviewCompletedEvent(session, languageId));
    }

    protected virtual string BuildReport(IEnumerable<InterviewTurn> turns, decimal score, string reason, string aiCompletion = null)
    {
        var strengths = turns.Where(turn => turn.Score.GetValueOrDefault() >= 75).Select(turn => turn.QuestionText).Take(3).ToList();
        var improvements = turns.Where(turn => turn.Score.GetValueOrDefault() < 75).Select(turn => turn.QuestionText).Take(3).ToList();

        return string.Join(Environment.NewLine, new[]
        {
            $"Overall score: {score:N0}/100",
            $"Strengths: {(strengths.Any() ? string.Join("; ", strengths) : "Good structure and engagement.")}",
            $"Improvement areas: {(improvements.Any() ? string.Join("; ", improvements) : "Provide more concrete examples.")}",
            string.IsNullOrWhiteSpace(aiCompletion) ? string.Empty : $"AI completion: {aiCompletion}",
            string.IsNullOrWhiteSpace(reason) ? string.Empty : $"Completion note: {reason}"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    protected virtual int GetMaxQuestions(InterviewSession session)
    {
        if (session?.QuestionCount > 0)
            return Math.Clamp(session.QuestionCount, 1, 10);

        return (session?.Difficulty ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "easy" => 2,
            "hard" => 4,
            _ => 3
        };
    }

    protected virtual async Task<string> ValidateAnswerAsync(string question, string answer)
    {
        var trimmedAnswer = answer?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedAnswer))
            return await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidAnswer", "Answer cannot be empty.");

        var normalizedQuestion = NormalizeInterviewText(question);
        var normalizedAnswer = NormalizeInterviewText(trimmedAnswer);
        if (string.IsNullOrWhiteSpace(normalizedAnswer))
            return "Please answer the question in your own words.";

        var answerTokens = TokenizeInterviewText(normalizedAnswer);
        var questionTokens = TokenizeInterviewText(normalizedQuestion);
        if (answerTokens.Length < 2 || normalizedAnswer.Length < 8)
            return "Please answer the question in your own words.";

        if (!string.IsNullOrWhiteSpace(normalizedQuestion))
        {
            if (string.Equals(normalizedAnswer, normalizedQuestion, StringComparison.Ordinal))
                return "Please answer the question in your own words.";

            var jaccardSimilarity = CalculateJaccardSimilarity(questionTokens, answerTokens);
            if (jaccardSimilarity >= 0.85m)
                return "Please answer the question in your own words.";

            if (normalizedAnswer.Contains(normalizedQuestion, StringComparison.Ordinal) && answerTokens.Length <= questionTokens.Length + 3)
                return "Please answer the question in your own words.";

            var overlapRatio = CalculateOverlapRatio(questionTokens, answerTokens);
            if (overlapRatio >= 0.9m && answerTokens.Length <= questionTokens.Length + 5)
                return "Please answer the question in your own words.";
        }

        return string.Empty;
    }

    protected static string NormalizeInterviewText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    protected static string[] TokenizeInterviewText(string normalizedText)
    {
        return string.IsNullOrWhiteSpace(normalizedText)
            ? Array.Empty<string>()
            : normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    protected static decimal CalculateJaccardSimilarity(IEnumerable<string> left, IEnumerable<string> right)
    {
        var leftSet = new HashSet<string>(left ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        var rightSet = new HashSet<string>(right ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        if (leftSet.Count == 0 || rightSet.Count == 0)
            return 0m;

        var intersectionCount = leftSet.Intersect(rightSet, StringComparer.Ordinal).Count();
        var unionCount = leftSet.Union(rightSet, StringComparer.Ordinal).Count();
        return unionCount == 0 ? 0m : (decimal)intersectionCount / unionCount;
    }

    protected static decimal CalculateOverlapRatio(IEnumerable<string> sourceTokens, IEnumerable<string> candidateTokens)
    {
        var source = sourceTokens?.ToArray() ?? Array.Empty<string>();
        var candidate = candidateTokens?.ToArray() ?? Array.Empty<string>();
        if (source.Length == 0 || candidate.Length == 0)
            return 0m;

        var overlap = source.Count(token => candidate.Contains(token, StringComparer.Ordinal));
        return (decimal)overlap / source.Length;
    }

    protected virtual async Task LogRecordingUploadStartAsync(InterviewSession session, IFormFile recording, string blobName, string normalizedContentType)
    {
        var detail = BuildRecordingUploadLog(session, recording, blobName, "Start", normalizedContentType: normalizedContentType);
        _logger?.LogInformation("AI Interview recording upload start. {Detail}", detail);
        await LogRuntimeIssueAsync(NopLogLevel.Information, "AI Interview recording upload start", detail, await ResolveLogCustomerAsync(session));
    }

    protected virtual async Task LogRecordingUploadSuccessAsync(InterviewSession session, IFormFile recording, string blobName, int azureStatus, string normalizedContentType)
    {
        var detail = BuildRecordingUploadLog(session, recording, blobName, "Success", azureStatus: azureStatus, normalizedContentType: normalizedContentType);
        _logger?.LogInformation("AI Interview recording upload success. {Detail}", detail);
        await LogRuntimeIssueAsync(NopLogLevel.Information, "AI Interview recording upload success", detail, await ResolveLogCustomerAsync(session));
    }

    protected virtual async Task LogRecordingUploadFailureAsync(InterviewSession session, IFormFile recording, string blobName, string message, string reason, int? azureStatus = null, string azureErrorBody = null, string normalizedContentType = null)
    {
        var detail = BuildRecordingUploadLog(session, recording, blobName, "Failure", message, reason, azureStatus, azureErrorBody, normalizedContentType);
        _logger?.LogWarning("AI Interview recording upload failure. {Detail}", detail);
        await LogRuntimeIssueAsync(NopLogLevel.Warning, "AI Interview recording upload failure", detail, await ResolveLogCustomerAsync(session));
    }

    protected static string BuildRecordingUploadLog(InterviewSession session, IFormFile recording, string blobName, string stage, string message = null, string reason = null, int? azureStatus = null, string azureErrorBody = null, string normalizedContentType = null)
    {
        var details = new List<string>
        {
            $"Stage={stage}",
            $"SessionId={session?.Id ?? 0}",
            $"CustomerId={session?.CustomerId ?? 0}",
            $"ProductId={session?.ProductId ?? 0}",
            $"RecordingLength={recording?.Length ?? 0}",
            $"ContentType={recording?.ContentType ?? string.Empty}",
            $"NormalizedAzureContentType={normalizedContentType ?? NormalizeRecordingContentType(recording?.ContentType)}",
            $"BlobName={blobName ?? string.Empty}"
        };

        if (!string.IsNullOrWhiteSpace(message))
            details.Add($"Message={message}");
        if (!string.IsNullOrWhiteSpace(reason))
            details.Add($"Reason={reason}");
        if (azureStatus.HasValue)
            details.Add($"AzureHttpStatus={azureStatus.Value}");
        if (!string.IsNullOrWhiteSpace(azureErrorBody))
            details.Add($"AzureErrorBody={TruncateSafe(azureErrorBody, 4000)}");

        return string.Join("; ", details);
    }

    protected static string NormalizeRecordingContentType(string contentType)
    {
        const string fallbackContentType = "video/webm";

        if (string.IsNullOrWhiteSpace(contentType))
            return fallbackContentType;

        var candidate = contentType.Trim();
        var separatorIndex = candidate.IndexOf(';');
        if (separatorIndex >= 0)
            candidate = candidate[..separatorIndex];

        candidate = candidate.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return fallbackContentType;

        return MediaTypeHeaderValue.TryParse(candidate, out var parsed) && !string.IsNullOrWhiteSpace(parsed?.MediaType)
            ? parsed.MediaType
            : fallbackContentType;
    }

    protected virtual RecordingUploadResponseModel RecordingFailure(string message)
    {
        return new RecordingUploadResponseModel
        {
            Success = false,
            Message = message
        };
    }

    protected virtual async Task<string> GetJobTitleAsync(int productId)
    {
        if (productId <= 0)
            return "Practice Interview";

        var product = await _productService.GetProductByIdAsync(productId);
        return product?.Name ?? "Practice Interview";
    }

    protected virtual IList<AIInterviewHistoryItem> BuildPreviousTurnContext(IEnumerable<InterviewTurn> turns, InterviewTurn currentTurn = null)
    {
        return (turns ?? Enumerable.Empty<InterviewTurn>())
            .Where(turn => turn != null
                && turn.Id != currentTurn?.Id
                && !string.IsNullOrWhiteSpace(turn.AnswerText))
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .Select(turn => new AIInterviewHistoryItem
            {
                SequenceNumber = turn.SequenceNumber,
                Question = turn.QuestionText,
                Answer = turn.AnswerText,
                Score = turn.Score,
                Feedback = turn.Feedback
            })
            .ToList();
    }
}
