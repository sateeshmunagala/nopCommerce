using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
using Nop.Services.Logging;

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

    public async Task DeleteInterviewTurnsAsync(IList<InterviewTurn> turns)
    {
        if (turns == null || !turns.Any())
            return;

        await _turnRepository.DeleteAsync(turns);
    }
}

public partial class InterviewAiClient : IAIInterviewClient
{
    private const int GenerateMaxCompletionTokens = 400;
    private const int ScoreMaxCompletionTokens = 1200;
    private const int ScoreLengthRetryMaxCompletionTokens = 2000;

    private readonly AIInterviewSettings _settings;
    private readonly MockAIInterviewSettings _mockSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAzureOpenAiChatCompletionAdapter _azureOpenAiChatCompletionAdapter;
    private readonly IWorkContext _workContext;
    private readonly NopLogger _nopLogger;
    private readonly ILogger<InterviewAiClient> _logger;

    public InterviewAiClient(AIInterviewSettings settings, MockAIInterviewSettings mockSettings, IHttpClientFactory httpClientFactory = null, IWorkContext workContext = null, NopLogger nopLogger = null, ILogger<InterviewAiClient> logger = null, IAzureOpenAiChatCompletionAdapter azureOpenAiChatCompletionAdapter = null)
    {
        _settings = settings;
        _mockSettings = mockSettings;
        _httpClientFactory = httpClientFactory;
        _azureOpenAiChatCompletionAdapter = azureOpenAiChatCompletionAdapter ?? new AzureOpenAiChatCompletionAdapter(settings);
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

        var additionalUsageInfos = new List<AzureOpenAiUsageInfo>();
        if (response.UsageInfo != null)
            additionalUsageInfos.Add(response.UsageInfo);

        if (ShouldRetrySuspiciousZeroScore(request, response))
        {
            var retryPrompt = string.Join(" ", new[]
            {
                request?.Prompt?.Trim(),
                "Guardrail: if the answer attempts the question but is weak or generic, do not classify it as non_substantive and do not score it as 0.",
                "Use answerQuality weak with low but non-zero scores for attempted answers. Reserve answerQuality non_substantive and score 0 for empty, copied, refusal, AI-persona, or unrelated answers only."
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            var retriedResponse = await CallAzureAsync(request with { Prompt = retryPrompt }, "score");
            if (retriedResponse?.UsageInfo != null)
                additionalUsageInfos.Add(retriedResponse.UsageInfo);
            if (retriedResponse != null && retriedResponse.Success)
            {
                response = retriedResponse with
                {
                    AdditionalUsageInfos = additionalUsageInfos
                        .Take(Math.Max(0, additionalUsageInfos.Count - 1))
                        .ToList()
                };
            }

            if (response.Score.GetValueOrDefault() == 0)
            {
                var detail = $"Mode=score; Reason=suspicious all-zero scoring retained after retry; AnswerLength={(request?.Answer ?? string.Empty).Length}; AnswerWords={TokenizeScoreRetryText(NormalizeScoreRetryText(request?.Answer)).Length}; AnswerQuality={BuildSafeValue(response.AnswerQuality)}; NonSubstantiveReason={BuildSafeValue(response.NonSubstantiveReason)}; Sample={TruncateSafe(response.RawJson, 300)}.";
                await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview zero-score guardrail", detail);
            }
        }

        if (!response.Score.HasValue ||
            string.IsNullOrWhiteSpace(response.Feedback) ||
            !response.TechnicalScore.HasValue ||
            !response.CommunicationScore.HasValue ||
            !response.ProfessionalismScore.HasValue ||
            !response.PositiveAttitudeScore.HasValue)
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
            var prompt = BuildPrompt(request, mode);
            var maxCompletionTokens = GetInitialMaxCompletionTokens(mode);
            var result = await CompleteAzureChatAsync(request, mode, prompt, maxCompletionTokens);
            var additionalUsageInfos = new List<AzureOpenAiUsageInfo>();

            if (ShouldRetryLengthTruncatedEmptyScoreContent(mode, result))
            {
                if (result.UsageInfo != null)
                    additionalUsageInfos.Add(result.UsageInfo);

                var retryDetail = BuildAzureLengthRetryLog(mode, result, "retry initiated due to truncation", maxCompletionTokens, ScoreLengthRetryMaxCompletionTokens);
                await LogAiClientIssueAsync(NopLogLevel.Information, "AI Interview Azure OpenAI truncation retry initiated", retryDetail);

                result = await CompleteAzureChatAsync(request, mode, prompt, ScoreLengthRetryMaxCompletionTokens);
            }

            if (result?.Success != true)
            {
                var detail = BuildAzureAdapterFailureLog(mode, result);
                if (string.Equals(result?.FailureKind, "azure-openai-http-failure", StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogWarning("Azure OpenAI call failed with status {StatusCode}.", result?.StatusCode);
                    await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI HTTP failure", detail);
                }
                else
                {
                    _logger?.LogWarning("Azure OpenAI call exception.");
                    await LogAiClientIssueAsync(NopLogLevel.Error, "AI Interview Azure OpenAI exception", detail);
                }

                return BuildUnavailableResponse(detail);
            }

            var usageInfo = result.UsageInfo;
            if (string.IsNullOrWhiteSpace(result.Content))
            {
                if (additionalUsageInfos.Count > 0)
                {
                    var retryExhaustedDetail = BuildAzureLengthRetryLog(mode, result, "retry exhausted", ScoreLengthRetryMaxCompletionTokens, null);
                    await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI truncation retry exhausted", retryExhaustedDetail);
                }

                var detail = BuildAzureContractFailureLog(mode, result.Endpoint, result.DeploymentOrModel, BuildEmptyContentReason(result), result.ResponseBody);
                _logger?.LogWarning("Azure OpenAI call failed. Mode: {Mode}. Reason: Empty content string.", mode);
                await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI contract failure", detail);
                return BuildUnavailableResponse(detail) with { UsageInfo = usageInfo, AdditionalUsageInfos = additionalUsageInfos };
            }

            var parsed = ParseStructuredResponse(result.Content);
            if (parsed != null)
            {
                if (additionalUsageInfos.Count > 0)
                {
                    var retryRecoveredDetail = BuildAzureLengthRetryLog(mode, result, "retry recovered", ScoreLengthRetryMaxCompletionTokens, null);
                    await LogAiClientIssueAsync(NopLogLevel.Information, "AI Interview Azure OpenAI truncation retry recovered", retryRecoveredDetail);
                }

                return parsed with { UsageInfo = usageInfo, AdditionalUsageInfos = additionalUsageInfos };
            }

            if (additionalUsageInfos.Count > 0)
            {
                var retryExhaustedDetail = BuildAzureLengthRetryLog(mode, result, "retry exhausted", ScoreLengthRetryMaxCompletionTokens, null);
                await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI truncation retry exhausted", retryExhaustedDetail);
            }

            var contractReason = BuildStructuredResponseFailureLog(result.Content, mode);
            _logger?.LogWarning("Azure OpenAI call failed. Mode: {Mode}. Reason: Invalid JSON or failed contract parsing.", mode);
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI contract failure", contractReason);
            return BuildUnavailableResponse(contractReason) with { UsageInfo = usageInfo, AdditionalUsageInfos = additionalUsageInfos };
        }
        catch (System.Text.Json.JsonException ex)
        {
            var detail = BuildAzureExceptionLog(mode, "azure-openai-json-failure", "invalid JSON format", ex);
            _logger?.LogWarning(ex, "Azure OpenAI call failed. Mode: {Mode}. Reason: Invalid JSON format.", mode);
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI JSON failure", detail);
            return BuildUnavailableResponse(detail);
        }
        catch (Exception ex)
        {
            var detail = BuildAzureExceptionLog(mode, "azure-openai-exception", ex.GetType().Name, ex);
            _logger?.LogWarning(ex, "Azure OpenAI call exception.");
            await LogAiClientIssueAsync(NopLogLevel.Error, "AI Interview Azure OpenAI exception", detail);
            return BuildUnavailableResponse(detail);
        }

    }

    private Task<AzureOpenAiChatCompletionResult> CompleteAzureChatAsync(AIInterviewClientRequest request, string mode, string prompt, int maxCompletionTokens)
    {
        return _azureOpenAiChatCompletionAdapter.CompleteChatAsync(new AzureOpenAiChatCompletionRequest
        {
            Mode = mode,
            OperationName = BuildAzureOperationName(mode),
            SystemPrompt = mode == "generate"
                ? "Return JSON only. Question mode contract: question, complete:false, optional rubricJson. No markdown. No prose outside JSON."
                : "Return JSON only. Scoring mode contract: technicalScore, communicationScore, professionalismScore, positiveAttitudeScore, score, feedback, complete, optional nextQuestion, completion, optional answerQuality, optional nonSubstantiveReason, rubricJson. No markdown. No prose outside JSON. All numeric scores must be integers or decimals from 0 to 100. score must be present and must be the average of the four category scores. feedback must be present. technicalScore, communicationScore, professionalismScore, and positiveAttitudeScore must all be present. rubricJson should be a JSON object that repeats the category scores and score. Distinguish answerQuality as non_substantive, weak, or substantive. Reserve score 0 and answerQuality non_substantive only for empty, copied, refusal, AI-persona, or unrelated answers. If the answer attempts the question but is generic, vague, or lacks evidence, classify it as weak and assign low but non-zero scores with concrete feedback.",
            UserPrompt = prompt,
            MaxCompletionTokens = maxCompletionTokens
        });
    }

    private static int GetInitialMaxCompletionTokens(string mode)
    {
        return string.Equals(mode, "score", StringComparison.OrdinalIgnoreCase)
            ? ScoreMaxCompletionTokens
            : GenerateMaxCompletionTokens;
    }

    private static bool ShouldRetryLengthTruncatedEmptyScoreContent(string mode, AzureOpenAiChatCompletionResult result)
    {
        return string.Equals(mode, "score", StringComparison.OrdinalIgnoreCase) &&
            result?.Success == true &&
            string.IsNullOrWhiteSpace(result.Content) &&
            result.IsLengthTruncated;
    }

    private static string BuildEmptyContentReason(AzureOpenAiChatCompletionResult result)
    {
        return result?.IsLengthTruncated == true
            ? $"empty response content (finish_reason={BuildSafeValue(result.FinishReason)})"
            : "empty response content";
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
Job context: {TruncateSafe(request.JobContext, 1500)}
Difficulty: {request.Difficulty}
Prompt: {request.Prompt}
Question number: {request.QuestionNumber}
Resume profile JSON: {TruncateSafe(request.ResumeProfileJson, 2500)}
Previous questions: {previousQuestions}
Previous scores: {previousScores}
Previous answered turns:
{previousTurns}
Current question: {request.Question}
Candidate answer: {request.Answer}
Current turn rubric JSON: {TruncateSafe(request.CurrentTurnRubricJson, 2000)}
Response contract: {(mode == "generate" ? "question, complete:false, optional rubricJson" : "{\"technicalScore\":0-100,\"communicationScore\":0-100,\"professionalismScore\":0-100,\"positiveAttitudeScore\":0-100,\"score\":0-100,\"feedback\":\"string\",\"complete\":false,\"nextQuestion\":\"optional string or null\",\"completion\":\"string or null\",\"answerQuality\":\"optional non_substantive|weak|substantive\",\"nonSubstantiveReason\":\"optional string\",\"rubricJson\":{\"technicalScore\":0-100,\"communicationScore\":0-100,\"professionalismScore\":0-100,\"positiveAttitudeScore\":0-100,\"score\":0-100}}")}
Scoring rule: copied question text, irrelevant content, empty answers, refusal answers, AI-persona answers such as "As an AI...", or other non-substantive answers must receive score 0 with answerQuality non_substantive and feedback that tells the candidate to answer in their own words.
Scoring distinction: if the answer attempts the question but is generic, weak, vague, or lacks concrete evidence, set answerQuality to weak and assign low but non-zero scores instead of 0. Use answerQuality substantive when the answer provides relevant specific evidence.
""";
    }

    protected static string BuildAzureHttpFailureLog(string mode, string endpoint, int statusCode, string reasonPhrase, string responseBody, string requestShape = null, bool fallbackUsed = false, string deploymentOrModel = null)
    {
        var errorCode = string.Empty;
        var errorMessage = string.Empty;
        var responseSnippet = string.Empty;
        var deployment = !string.IsNullOrWhiteSpace(deploymentOrModel)
            ? deploymentOrModel
            : ExtractAzureDeploymentName(endpoint);

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.TryGetProperty("code", out var codeElement))
                        errorCode = SanitizeDiagnosticText(codeElement.GetString());
                    if (errorElement.TryGetProperty("message", out var messageElement))
                        errorMessage = SanitizeDiagnosticText(TruncateSafe(messageElement.GetString(), 180));
                }
            }
            catch
            {
            }

            responseSnippet = BuildResponseSnippet(responseBody);
        }

        var details = new List<string>
        {
            $"Mode={mode}",
            $"Operation={BuildAzureOperationName(mode)}",
            "FailureKind=azure-openai-http-failure",
            $"HttpStatus={statusCode}",
            "Reason=http failure"
        };
        if (!string.IsNullOrWhiteSpace(reasonPhrase))
            details.Add($"ReasonPhrase={SanitizeDiagnosticText(TruncateSafe(reasonPhrase, 80))}");
        details.Add($"EndpointHost={BuildSanitizedEndpointHost(endpoint)}");
        details.Add($"Endpoint={BuildSanitizedEndpointValue(endpoint)}");
        details.Add($"Deployment={BuildSafeValue(deployment)}");
        if (!string.IsNullOrWhiteSpace(requestShape))
            details.Add($"RequestShape={BuildSafeValue(requestShape)}");
        if (fallbackUsed)
            details.Add("FallbackUsed=true");
        details.Add($"ResponseLength={(responseBody ?? string.Empty).Length}");
        if (!string.IsNullOrWhiteSpace(errorCode))
            details.Add($"AzureErrorCode={errorCode}");
        if (!string.IsNullOrWhiteSpace(errorMessage))
            details.Add($"AzureErrorMessage={errorMessage}");
        if (!string.IsNullOrWhiteSpace(responseSnippet))
        {
            details.Add($"ResponseSnippet={responseSnippet}");
            details.Add($"AzureResponseBody={responseSnippet}");
        }

        return string.Join("; ", details) + ".";
    }

    protected static string BuildAzureContractFailureLog(string mode, string endpoint, string deploymentOrModel, string reason, string responseBody)
    {
        var responseSnippet = BuildResponseSnippet(responseBody);
        var deployment = !string.IsNullOrWhiteSpace(deploymentOrModel)
            ? deploymentOrModel
            : ExtractAzureDeploymentName(endpoint);
        var details = new List<string>
        {
            $"Mode={mode}",
            $"Operation={BuildAzureOperationName(mode)}",
            "FailureKind=azure-openai-contract-failure",
            $"Reason={BuildSafeValue(reason)}",
            $"EndpointHost={BuildSanitizedEndpointHost(endpoint)}",
            $"Endpoint={BuildSanitizedEndpointValue(endpoint)}",
            $"Deployment={BuildSafeValue(deployment)}",
            $"ResponseLength={(responseBody ?? string.Empty).Length}"
        };
        if (!string.IsNullOrWhiteSpace(responseSnippet))
        {
            details.Add($"Sample={responseSnippet}");
            details.Add($"AzureResponseBody={responseSnippet}");
        }

        return string.Join("; ", details) + ".";
    }

    protected static string BuildAzureContractFailureLog(string mode, string endpoint, string reason, string responseBody)
    {
        return BuildAzureContractFailureLog(mode, endpoint, null, reason, responseBody);
    }

    protected static string BuildAzureLengthRetryLog(string mode, AzureOpenAiChatCompletionResult result, string outcome, int maxCompletionTokens, int? retryMaxCompletionTokens)
    {
        var normalizedOutcome = outcome ?? string.Empty;
        var reason = normalizedOutcome.Contains("recovered", StringComparison.OrdinalIgnoreCase)
            ? "retry recovered after truncation"
            : normalizedOutcome.Contains("exhausted", StringComparison.OrdinalIgnoreCase) &&
                (result?.IsLengthTruncated != true || !string.IsNullOrWhiteSpace(result?.Content))
                    ? "retry exhausted after truncation"
                    : BuildEmptyContentReason(result);
        var details = new List<string>
        {
            $"Mode={mode}",
            $"Operation={BuildAzureOperationName(mode)}",
            "FailureKind=azure-openai-length-truncation",
            $"Reason={BuildSafeValue(reason)}",
            $"Outcome={BuildSafeValue(outcome)}",
            $"FinishReason={BuildSafeValue(result?.FinishReason)}",
            $"EndpointHost={BuildSanitizedEndpointHost(result?.Endpoint)}",
            $"Endpoint={BuildSanitizedEndpointValue(result?.Endpoint)}",
            $"Deployment={BuildSafeValue(result?.DeploymentOrModel)}",
            $"MaxCompletionTokens={maxCompletionTokens}",
            $"ResponseLength={(result?.ResponseBody ?? string.Empty).Length}"
        };

        if (retryMaxCompletionTokens.HasValue)
            details.Add($"RetryMaxCompletionTokens={retryMaxCompletionTokens.Value}");

        var responseSnippet = BuildResponseSnippet(result?.ResponseBody);
        if (!string.IsNullOrWhiteSpace(responseSnippet))
            details.Add($"Sample={responseSnippet}");

        return string.Join("; ", details) + ".";
    }

    protected static string BuildAzureExceptionLog(string mode, string failureKind, string reason, Exception exception)
    {
        return string.Join("; ", new[]
        {
            $"Mode={mode}",
            $"Operation={BuildAzureOperationName(mode)}",
            $"FailureKind={BuildSafeValue(failureKind)}",
            $"Reason={BuildSafeValue(reason)}",
            $"ExceptionType={BuildSafeValue(exception?.GetType().Name)}",
            $"ExceptionMessage={SanitizeDiagnosticText(TruncateSafe(exception?.Message, 300))}",
            $"ExceptionDetail={SanitizeDiagnosticText(TruncateSafe(exception?.ToString(), 500))}"
        }) + ".";
    }

    protected static string BuildAzureAdapterFailureLog(string mode, AzureOpenAiChatCompletionResult result)
    {
        if (string.Equals(result?.FailureKind, "azure-openai-http-failure", StringComparison.OrdinalIgnoreCase))
            return BuildAzureHttpFailureLog(mode, result?.Endpoint, result?.StatusCode ?? 0, result?.ReasonPhrase, result?.ResponseBody, result?.RequestShape, result?.FallbackUsed ?? false, result?.DeploymentOrModel);

        return string.Join("; ", new[]
        {
            $"Mode={mode}",
            $"Operation={BuildAzureOperationName(mode)}",
            $"FailureKind={BuildSafeValue(result?.FailureKind ?? "azure-openai-exception")}",
            $"Reason={BuildSafeValue(result?.Reason)}",
            $"EndpointHost={BuildSanitizedEndpointHost(result?.Endpoint)}",
            $"Endpoint={BuildSanitizedEndpointValue(result?.Endpoint)}",
            $"Deployment={BuildSafeValue(result?.DeploymentOrModel)}",
            $"ExceptionMessage={SanitizeDiagnosticText(TruncateSafe(result?.ErrorMessage, 300))}",
            $"ExceptionDetail={SanitizeDiagnosticText(TruncateSafe(result?.ResponseBody, 500))}"
        }) + ".";
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
            $"Operation={BuildAzureOperationName(mode)}",
            "FailureKind=azure-openai-configuration-incomplete",
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
        return string.IsNullOrWhiteSpace(value) ? "<empty>" : SanitizeDiagnosticText(TruncateSafe(value.Trim(), 120));
    }

    protected static string BuildResponseSnippet(string responseBody)
    {
        return string.IsNullOrWhiteSpace(responseBody)
            ? string.Empty
            : SanitizeDiagnosticText(TruncateSafe(responseBody.Replace('\r', ' ').Replace('\n', ' ').Trim(), 500));
    }

    protected static string SanitizeDiagnosticText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        sanitized = Regex.Replace(sanitized, "(?i)(api[-_ ]?key|authorization|access[_-]?token|refresh[_-]?token|bearer|subscription[-_ ]?key)\\s*[:=]\\s*\\\"?[^\\\"\\s,;}]+", "$1=<redacted>");
        sanitized = Regex.Replace(sanitized, "(?i)(sig|signature|code|client_secret)=([^&\\s]+)", "$1=<redacted>");
        return sanitized;
    }

    protected static string BuildAzureOperationName(string mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            "generate" => "llm-question-generation",
            "score" => "llm-scoring",
            "resume-profile" => "llm-resume-profile",
            "question-plan" => "llm-question-plan",
            _ => "llm-azure-openai"
        };
    }

    protected static string ExtractAzureDeploymentName(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return string.Empty;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "deployments", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(segments[index + 1]);
        }

        return string.Empty;
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
            string answerQuality = root.TryGetProperty("answerQuality", out var answerQualityElement) ? answerQualityElement.GetString() : null;
            string nonSubstantiveReason = root.TryGetProperty("nonSubstantiveReason", out var nonSubstantiveReasonElement) ? nonSubstantiveReasonElement.GetString() : null;
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
                AnswerQuality = answerQuality,
                NonSubstantiveReason = nonSubstantiveReason,
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
            AnswerQuality = response?.AnswerQuality,
            NonSubstantiveReason = response?.NonSubstantiveReason,
            RawJson = response?.RawJson,
            RubricJson = response?.RubricJson,
            UsageInfo = response?.UsageInfo,
            AdditionalUsageInfos = response?.AdditionalUsageInfos ?? new List<AzureOpenAiUsageInfo>()
        };
    }

    protected virtual bool ShouldRetrySuspiciousZeroScore(AIInterviewClientRequest request, AIInterviewClientResponse response)
    {
        if (response == null || !response.Success || !response.Score.HasValue || response.Score.Value != 0)
            return false;

        if (response.TechnicalScore.GetValueOrDefault() != 0 ||
            response.CommunicationScore.GetValueOrDefault() != 0 ||
            response.ProfessionalismScore.GetValueOrDefault() != 0 ||
            response.PositiveAttitudeScore.GetValueOrDefault() != 0)
        {
            return false;
        }

        if (string.Equals(response.AnswerQuality, "non_substantive", StringComparison.OrdinalIgnoreCase))
            return false;

        return LooksPotentiallySubstantiveAnswer(request?.Answer);
    }

    protected virtual bool LooksPotentiallySubstantiveAnswer(string answer)
    {
        var normalizedAnswer = NormalizeScoreRetryText(answer);
        var answerTokens = TokenizeScoreRetryText(normalizedAnswer);
        if (answerTokens.Length < 12 || normalizedAnswer.Length < 60)
            return false;

        if (normalizedAnswer.StartsWith("as an ai", StringComparison.Ordinal) ||
            normalizedAnswer.StartsWith("i cannot", StringComparison.Ordinal) ||
            normalizedAnswer.StartsWith("i can't", StringComparison.Ordinal) ||
            normalizedAnswer.Contains("language model", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    protected static string NormalizeScoreRetryText(string text)
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

            if (previousWasSpace)
                continue;

            builder.Append(' ');
            previousWasSpace = true;
        }

        return builder.ToString().Trim();
    }

    protected static string[] TokenizeScoreRetryText(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
        var reason = "invalid JSON or failed contract parsing";
        if (!score.HasValue)
            reason = "missing score";
        else if (!technicalScore.HasValue || !communicationScore.HasValue || !professionalismScore.HasValue || !positiveAttitudeScore.HasValue)
            reason = "missing category score";
        else if (string.IsNullOrWhiteSpace(feedback))
            reason = "missing feedback";

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
                : string.Equals(mode, "generate", StringComparison.OrdinalIgnoreCase)
                    ? new ScoreContractDiagnostics(GetStructuredResponseFailureReason(content, mode), document.RootElement.ValueKind.ToString(),
                    document.RootElement.ValueKind == JsonValueKind.Object ? string.Join(",", document.RootElement.EnumerateObject().Select(property => property.Name)) : string.Empty,
                    TruncateSafe(document.RootElement.GetRawText(), 800),
                    string.IsNullOrWhiteSpace(document.RootElement.TryGetProperty("question", out var questionElement) ? questionElement.GetString() : null) ? "question" : "<none>")
                    : new ScoreContractDiagnostics("invalid JSON or failed contract parsing", document.RootElement.ValueKind.ToString(),
                    document.RootElement.ValueKind == JsonValueKind.Object ? string.Join(",", document.RootElement.EnumerateObject().Select(property => property.Name)) : string.Empty,
                    TruncateSafe(document.RootElement.GetRawText(), 800),
                    "<not-applicable>");

            return $"Mode={mode}; Operation={BuildAzureOperationName(mode)}; FailureKind=azure-openai-contract-failure; Reason={diagnostics.Reason}; MissingFields={diagnostics.MissingFields}; Shape={diagnostics.Shape}; PropertyNames={diagnostics.PropertyNames}; ResponseLength={(content ?? string.Empty).Length}; Sample={BuildResponseSnippet(diagnostics.Sample)}.";
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

            return $"Mode={mode}; Operation={BuildAzureOperationName(mode)}; FailureKind=azure-openai-contract-failure; Reason={GetStructuredResponseFailureReason(content, mode)}; Shape={shape}; ResponseLength={(content ?? string.Empty).Length}; Sample={BuildResponseSnippet(sample)}.";
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
        var reason = response == null || !response.Score.HasValue ? "missing required score"
            : !response.TechnicalScore.HasValue || !response.CommunicationScore.HasValue || !response.ProfessionalismScore.HasValue || !response.PositiveAttitudeScore.HasValue ? "missing category score"
            : string.IsNullOrWhiteSpace(response.Feedback) ? "missing feedback"
            : "invalid score contract";

        var sample = TruncateSafe(response?.RawJson, 800);
        return $"Mode=score; Operation={BuildAzureOperationName("score")}; FailureKind=azure-openai-contract-failure; Reason={reason}; MissingFields={(missingFields.Count > 0 ? string.Join(",", missingFields) : "<none>")}; ResponseLength={(response?.RawJson ?? string.Empty).Length}; Sample={BuildResponseSnippet(sample)}.";
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
        var answerQuality = score >= 75 ? "substantive" : score >= 1 ? "weak" : "non_substantive";
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
            AnswerQuality = answerQuality,
            RawJson = JsonSerializer.Serialize(new
            {
                technicalScore,
                communicationScore,
                professionalismScore,
                positiveAttitudeScore,
                score = averageScore,
                feedback,
                answerQuality,
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

internal static class InterviewReportSummaryHelper
{
    private static readonly string[] NegativeFeedbackMarkers =
    {
        "did not",
        "more detail",
        "could",
        "should",
        "lacked",
        "missing",
        "improve",
        "strengthen the response",
        "not substantive",
        "answer in your own words"
    };

    private static readonly (string Phrase, string[] Terms)[] DomainPhraseMap =
    {
        ("LLM-driven conversational architectures", new[] { "llm", "chatbot", "conversational", "rag", "prompt" }),
        ("Azure AI Services integration work", new[] { "azure ai services", "azure openai", "azure" }),
        ("Microsoft Copilot and ServiceNow workflows", new[] { "copilot", "servicenow", "now assist", "teams" }),
        ("enterprise AI delivery", new[] { "enterprise", "workflow", "production", "platform" }),
        ("Python and ML solution delivery", new[] { "python", "xgboost", "ml", "machine learning", "fastapi" }),
        ("AWS-based deployment experience", new[] { "aws", "lambda", "s3", "ec2" })
    };

    internal static string BuildReport(IEnumerable<InterviewTurn> turns, decimal score, string reason, string aiCompletion = null)
    {
        var orderedTurns = (turns ?? Enumerable.Empty<InterviewTurn>())
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList();

        return string.Join(Environment.NewLine, new[]
        {
            $"Overall score: {score:N0}/100",
            $"Strengths: {BuildStrengthsSummary(orderedTurns)}",
            $"Improvement areas: {BuildImprovementAreasSummary(orderedTurns)}",
            string.IsNullOrWhiteSpace(aiCompletion) ? string.Empty : $"AI completion: {NormalizeWhitespace(aiCompletion)}",
            string.IsNullOrWhiteSpace(reason) ? string.Empty : $"Completion note: {NormalizeWhitespace(reason)}"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    internal static string NormalizePersistedReportData(string reportData, IEnumerable<InterviewTurn> turns, decimal score)
    {
        if (string.IsNullOrWhiteSpace(reportData))
            return reportData;

        var orderedTurns = (turns ?? Enumerable.Empty<InterviewTurn>())
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList();
        if (!orderedTurns.Any())
            return reportData;

        var normalizedQuestions = new HashSet<string>(
            orderedTurns.Select(turn => NormalizeComparisonText(turn.QuestionText))
                .Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.Ordinal);
        if (!normalizedQuestions.Any())
            return reportData;

        var lines = reportData
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (!lines.Any())
            return reportData;

        for (var index = 0; index < lines.Count; index++)
        {
            if (StartsWithLabel(lines[index], "Strengths") && SummaryLineContainsQuestionText(lines[index], normalizedQuestions))
            {
                lines[index] = $"Strengths: {BuildStrengthsSummary(orderedTurns)}";
                continue;
            }

            if (StartsWithLabel(lines[index], "Improvement areas") && SummaryLineContainsQuestionText(lines[index], normalizedQuestions))
                lines[index] = $"Improvement areas: {BuildImprovementAreasSummary(orderedTurns)}";
        }

        if (!lines.Any(line => StartsWithLabel(line, "Overall score")))
            lines.Insert(0, $"Overall score: {score:N0}/100");

        return string.Join(Environment.NewLine, lines);
    }

    internal static string BuildStrengthsSummary(IEnumerable<InterviewTurn> turns)
    {
        var strengths = (turns ?? Enumerable.Empty<InterviewTurn>())
            .Where(turn => turn.Score.GetValueOrDefault() >= 75)
            .OrderByDescending(turn => turn.Score.GetValueOrDefault())
            .ThenBy(turn => turn.SequenceNumber)
            .Select(TryBuildStrengthPhrase)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return strengths.Any()
            ? string.Join("; ", strengths)
            : "No scored strengths were identified from the submitted answers.";
    }

    internal static string BuildImprovementAreasSummary(IEnumerable<InterviewTurn> turns)
    {
        var improvements = (turns ?? Enumerable.Empty<InterviewTurn>())
            .Where(turn => turn.Score.GetValueOrDefault() < 75)
            .OrderBy(turn => turn.Score.GetValueOrDefault())
            .ThenBy(turn => turn.SequenceNumber)
            .Select(TryBuildImprovementPhrase)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return improvements.Any()
            ? string.Join("; ", improvements)
            : "Continue providing concrete examples and measurable outcomes.";
    }

    private static bool StartsWithLabel(string line, string label)
    {
        return !string.IsNullOrWhiteSpace(line) &&
            line.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SummaryLineContainsQuestionText(string line, HashSet<string> normalizedQuestions)
    {
        var separatorIndex = line.IndexOf(':');
        if (separatorIndex < 0)
            return false;

        var entries = line[(separatorIndex + 1)..]
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeComparisonText)
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return entries.Any(normalizedQuestions.Contains);
    }

    private static string TryBuildStrengthPhrase(InterviewTurn turn)
    {
        var feedbackStrength = BuildStrengthFromFeedback(turn?.Feedback);
        if (!string.IsNullOrWhiteSpace(feedbackStrength))
            return feedbackStrength;

        var answerStrength = BuildStrengthFromAnswer(turn?.AnswerText);
        if (!string.IsNullOrWhiteSpace(answerStrength))
            return answerStrength;

        return turn?.Score.GetValueOrDefault() >= 75
            ? "Provided relevant project examples and implementation context."
            : string.Empty;
    }

    private static string TryBuildImprovementPhrase(InterviewTurn turn)
    {
        var feedbackImprovement = BuildImprovementFromFeedback(turn?.Feedback);
        if (!string.IsNullOrWhiteSpace(feedbackImprovement))
            return feedbackImprovement;

        var rubricImprovement = BuildImprovementFromScores(turn);
        if (!string.IsNullOrWhiteSpace(rubricImprovement))
            return rubricImprovement;

        return "Provide more concrete examples and implementation details.";
    }

    private static string BuildStrengthFromFeedback(string feedback)
    {
        var normalizedFeedback = NormalizeWhitespace(feedback);
        if (string.IsNullOrWhiteSpace(normalizedFeedback))
            return string.Empty;

        var lowered = normalizedFeedback.ToLowerInvariant();
        if (NegativeFeedbackMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal)))
            return string.Empty;

        if (lowered.Contains("clear structure", StringComparison.Ordinal) || lowered.Contains("clear answer", StringComparison.Ordinal))
            return "Demonstrated clear structure and communication.";
        if (lowered.Contains("technical depth", StringComparison.Ordinal))
            return "Demonstrated relevant technical depth.";
        if (lowered.Contains("ownership", StringComparison.Ordinal))
            return "Showed ownership and professional judgment.";
        if (lowered.Contains("balanced", StringComparison.Ordinal))
            return "Provided a balanced and relevant response.";
        if (lowered.Contains("practical", StringComparison.Ordinal))
            return "Connected practical experience to the interview discussion.";

        var simplified = Regex.Replace(normalizedFeedback, @"^(strong|good|balanced|solid|clear|decent)\s+answer\s+(with|showing)\s+", string.Empty, RegexOptions.IgnoreCase);
        simplified = Regex.Replace(simplified, @"\b(answer|response)\b", string.Empty, RegexOptions.IgnoreCase);
        simplified = NormalizeWhitespace(simplified).Trim('.', ';', ',');
        if (string.IsNullOrWhiteSpace(simplified) || simplified.Length < 12)
            return string.Empty;

        simplified = char.ToLowerInvariant(simplified[0]) + simplified[1..];
        return $"Demonstrated {simplified}.";
    }

    private static string BuildStrengthFromAnswer(string answerText)
    {
        var normalizedAnswer = NormalizeWhitespace(answerText);
        if (string.IsNullOrWhiteSpace(normalizedAnswer))
            return string.Empty;

        var matchedPhrase = DomainPhraseMap
            .FirstOrDefault(candidate => candidate.Terms.Any(term => normalizedAnswer.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Phrase;
        if (!string.IsNullOrWhiteSpace(matchedPhrase))
            return $"Demonstrated experience with {matchedPhrase}.";

        var tokens = NormalizeComparisonText(normalizedAnswer).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length >= 12
            ? "Provided relevant project examples and implementation context."
            : string.Empty;
    }

    private static string BuildImprovementFromFeedback(string feedback)
    {
        var normalizedFeedback = NormalizeWhitespace(feedback);
        if (string.IsNullOrWhiteSpace(normalizedFeedback))
            return string.Empty;

        var specificExamplesMatch = Regex.Match(normalizedFeedback, @"did not provide specific examples of (?<topic>.+?)(?:\.|;|$)", RegexOptions.IgnoreCase);
        if (specificExamplesMatch.Success)
            return $"Provide specific examples of {NormalizeTopic(specificExamplesMatch.Groups["topic"].Value)}.";

        var moreDetailMatch = Regex.Match(normalizedFeedback, @"more detail on (?<topic>.+?) would strengthen(?: the response)?", RegexOptions.IgnoreCase);
        if (moreDetailMatch.Success)
            return $"Provide more detail on {NormalizeTopic(moreDetailMatch.Groups["topic"].Value)}.";

        if (normalizedFeedback.Contains("more concrete examples", StringComparison.OrdinalIgnoreCase))
            return "Provide more concrete examples and implementation details.";
        if (normalizedFeedback.Contains("measurable outcomes", StringComparison.OrdinalIgnoreCase))
            return "Add measurable outcomes and implementation impact.";
        if (normalizedFeedback.Contains("not substantive", StringComparison.OrdinalIgnoreCase) ||
            normalizedFeedback.Contains("own words", StringComparison.OrdinalIgnoreCase))
        {
            return "Provide a direct answer in your own words with relevant detail.";
        }

        var lackedMatch = Regex.Match(normalizedFeedback, @"lacked (?<topic>.+?)(?:\.|;|$)", RegexOptions.IgnoreCase);
        if (lackedMatch.Success)
            return $"Add {NormalizeTopic(lackedMatch.Groups["topic"].Value)}.";

        if (normalizedFeedback.Contains("specific examples", StringComparison.OrdinalIgnoreCase))
            return "Provide specific examples and implementation details.";
        if (normalizedFeedback.Contains("more detail", StringComparison.OrdinalIgnoreCase))
            return "Provide more detail on the implementation and outcomes.";

        return string.Empty;
    }

    private static string BuildImprovementFromScores(InterviewTurn turn)
    {
        var technical = ParseRubricScore(turn?.RubricJson, "technicalScore");
        var communication = ParseRubricScore(turn?.RubricJson, "communicationScore");
        var professionalism = ParseRubricScore(turn?.RubricJson, "professionalismScore");
        var attitude = ParseRubricScore(turn?.RubricJson, "positiveAttitudeScore");

        if (technical.HasValue && technical.Value < 75)
            return "Provide more concrete technical examples and implementation details.";
        if (communication.HasValue && communication.Value < 75)
            return "Explain decisions more clearly and with stronger structure.";
        if (professionalism.HasValue && professionalism.Value < 75)
            return "Show stronger ownership and decision-making rationale.";
        if (attitude.HasValue && attitude.Value < 75)
            return "Highlight constructive problem-solving and learning mindset.";

        return string.Empty;
    }

    private static decimal? ParseRubricScore(string rubricJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rubricJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(rubricJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var property))
                return null;

            return property.ValueKind switch
            {
                JsonValueKind.Number when property.TryGetDecimal(out var decimalValue) => decimalValue,
                JsonValueKind.String when decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeTopic(string topic)
    {
        var normalized = NormalizeWhitespace(topic)
            .Trim('.', ';', ',', ':')
            .Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "the implementation details"
            : normalized;
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static string NormalizeComparisonText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
                continue;
            }

            if (previousWasSpace)
                continue;

            builder.Append(' ');
            previousWasSpace = true;
        }

        return builder.ToString().Trim();
    }
}

public class InterviewRuntimeService : IInterviewRuntimeService
{
    private static readonly string[] PracticeSkillKeywords =
    [
        "practice skill",
        "skill",
        "skills",
        "interview skill"
    ];

    private static readonly string[] PracticeDifficultyValueAliases =
    [
        "low",
        "easy",
        "medium",
        "hard",
        "advanced"
    ];

    private sealed record SelectedProductAttributeValueSnapshot(string AttributeName, string TextPrompt, string Value);

    private static readonly JsonSerializerOptions StorageSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    private const string SpeechUnavailableMessage = "Voice mode is unavailable. Please type your answer below.";
    private static readonly ConcurrentDictionary<string, DateTime> SpeechTokenFailureLogCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan SpeechTokenFailureLogDedupeWindow = TimeSpan.FromMinutes(15);
    private static readonly Regex SensitiveJsonValueRegex = new(
        "(\"(?<name>key|token|secret|signature|password|authorization)\"\\s*:\\s*\")(?<value>[^\"]*)(\")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SensitiveKeyValueRegex = new(
        "(\\b(?<name>key|token|secret|signature|password|authorization)\\b\\s*[=:]\\s*)(?<quote>[\"']?)(?<value>[^\"'\\s;,&]+)(?<quote2>[\"']?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WhitespaceCollapseRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly IInterviewSessionService _sessionService;
    private readonly IInterviewTurnService _turnService;
    private readonly IAIInterviewClient _aiClient;
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;
    private readonly IApplicationService _applicationService;
    private readonly IResumeProfileService _resumeProfileService;
    private readonly IAzureUsageService _azureUsageService;
    private readonly ILocalizationService _localizationService;
    private readonly AIInterviewSettings _settings;
    private readonly MockAIInterviewSettings _mockSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWorkContext _workContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly NopLogger _nopLogger;
    private readonly ICustomerActivityService _customerActivityService;
    private readonly ILogger<InterviewRuntimeService> _logger;

    public InterviewRuntimeService(
        IInterviewSessionService sessionService,
        IInterviewTurnService turnService,
        IAIInterviewClient aiClient,
        IProductService productService,
        ICustomerService customerService,
        IApplicationService applicationService,
        IResumeProfileService resumeProfileService,
        IAzureUsageService azureUsageService,
        ILocalizationService localizationService,
        AIInterviewSettings settings,
        MockAIInterviewSettings mockSettings,
        IHttpClientFactory httpClientFactory,
        IWorkContext workContext,
        IEventPublisher eventPublisher = null,
        NopLogger nopLogger = null,
        ILogger<InterviewRuntimeService> logger = null,
        ICustomerActivityService customerActivityService = null)
    {
        _sessionService = sessionService;
        _turnService = turnService;
        _aiClient = aiClient;
        _productService = productService;
        _customerService = customerService;
        _applicationService = applicationService;
        _resumeProfileService = resumeProfileService;
        _azureUsageService = azureUsageService;
        _localizationService = localizationService;
        _settings = settings;
        _mockSettings = mockSettings;
        _httpClientFactory = httpClientFactory;
        _workContext = workContext;
        _eventPublisher = eventPublisher;
        _nopLogger = nopLogger;
        _logger = logger;
        _customerActivityService = customerActivityService;
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

    protected virtual async Task LogRuntimeActivityAsync(InterviewSession session, string systemKeyword, string comment, Customer customer = null, BaseEntity entity = null)
    {
        if (_customerActivityService == null || session == null || string.IsNullOrWhiteSpace(systemKeyword))
            return;

        var logCustomer = await ResolveLogCustomerAsync(session, customer);
        if (logCustomer == null)
            return;

        try
        {
            await _customerActivityService.InsertActivityAsync(logCustomer, systemKeyword, comment, entity ?? session);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AI Interview activity logging failed for keyword {SystemKeyword}, session {SessionId}.", systemKeyword, session.Id);
        }
    }

    protected static string BuildRuntimeActivityComment(InterviewSession session, InterviewTurn turn = null, string message = null, long? elapsedMilliseconds = null, int? statusCode = null, string failureKind = null)
    {
        var details = new List<string>
        {
            $"SessionId={session?.Id ?? 0}",
            $"CustomerId={session?.CustomerId ?? 0}",
            $"ProductId={session?.ProductId ?? 0}"
        };

        if (turn != null)
        {
            details.Add($"TurnId={turn.Id}");
            details.Add($"SequenceNumber={turn.SequenceNumber}");
        }

        if (elapsedMilliseconds.HasValue)
            details.Add($"ElapsedMs={Math.Max(0, elapsedMilliseconds.Value)}");
        if (statusCode.HasValue)
            details.Add($"StatusCode={statusCode.Value}");
        if (!string.IsNullOrWhiteSpace(failureKind))
            details.Add($"FailureKind={BuildSafeValue(failureKind)}");
        if (!string.IsNullOrWhiteSpace(message))
            details.Add($"Message={BuildSafeValue(message)}");

        return string.Join("; ", details);
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

    protected static string BuildMaskedTokenPrefix(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "<empty>";

        var trimmed = token.Trim();
        if (trimmed.Length <= 6)
            return "*****";

        return $"{trimmed[..6]}...";
    }

    protected static string SanitizeSpeechTokenLogValue(string value, int maxLength = 220)
    {
        var sanitized = SanitizeSensitiveSpeechValue(value);
        return string.IsNullOrWhiteSpace(sanitized)
            ? "<empty>"
            : TruncateSafe(sanitized, maxLength);
    }

    protected static string SanitizeAzureResponseBody(string responseBody, int maxLength = 4000)
    {
        return string.IsNullOrWhiteSpace(responseBody)
            ? string.Empty
            : TruncateSafe(SanitizeSensitiveSpeechValue(responseBody), maxLength);
    }

    protected static string BuildSpeechEndpointHost(string endpoint)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? SanitizeSpeechTokenLogValue(uri.Host, 200)
            : "<empty>";
    }

    protected static string BuildSpeechEndpointPath(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return "<empty>";

        var path = string.IsNullOrWhiteSpace(uri.PathAndQuery) ? uri.AbsolutePath : uri.PathAndQuery;
        return SanitizeSpeechTokenLogValue(path, 300);
    }

    protected static string BuildSpeechTokenFailureDedupeKey(
        string failureKind,
        InterviewSession session,
        string token,
        string region,
        int? httpStatus = null,
        string reasonPhrase = null,
        string azureResponseBody = null,
        Exception exception = null,
        bool? speechKeyConfigured = null,
        bool? speechRegionConfigured = null)
    {
        var sessionOrToken = session?.Id > 0
            ? $"session:{session.Id}"
            : $"token:{BuildMaskedTokenPrefix(token)}";
        var parts = new List<string>
        {
            sessionOrToken,
            $"kind:{NormalizeSpeechTokenDedupeValue(failureKind, 80)}",
            $"region:{NormalizeSpeechTokenDedupeValue(region, 120)}"
        };

        if (httpStatus.HasValue)
            parts.Add($"status:{httpStatus.Value}");
        if (!string.IsNullOrWhiteSpace(reasonPhrase))
            parts.Add($"reason:{NormalizeSpeechTokenDedupeValue(reasonPhrase, 120)}");
        if (speechKeyConfigured.HasValue)
            parts.Add($"keyConfigured:{speechKeyConfigured.Value.ToString().ToLowerInvariant()}");
        if (speechRegionConfigured.HasValue)
            parts.Add($"regionConfigured:{speechRegionConfigured.Value.ToString().ToLowerInvariant()}");
        if (exception != null)
        {
            parts.Add($"exceptionType:{NormalizeSpeechTokenDedupeValue(exception.GetType().FullName ?? exception.GetType().Name, 200)}");
            parts.Add($"exceptionMessage:{NormalizeSpeechTokenDedupeValue(exception.Message, 400)}");
        }
        if (!string.IsNullOrWhiteSpace(azureResponseBody))
            parts.Add($"bodyHash:{BuildSpeechTokenFailureBodyFingerprint(azureResponseBody)}");

        return string.Join("|", parts);
    }

    protected virtual bool ShouldLogSpeechTokenFailure(string dedupeKey, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(dedupeKey))
            return true;

        PruneExpiredSpeechTokenFailureKeys(nowUtc);
        var expiresUtc = nowUtc.Add(SpeechTokenFailureLogDedupeWindow);
        while (true)
        {
            if (SpeechTokenFailureLogCache.TryGetValue(dedupeKey, out var existingExpiresUtc) &&
                existingExpiresUtc > nowUtc)
            {
                _logger?.LogDebug("Suppressed duplicate AI Interview speech token failure log. DedupeKey={DedupeKey}. ExpiresUtc={ExpiresUtc:o}.",
                    dedupeKey, existingExpiresUtc);
                return false;
            }

            if (SpeechTokenFailureLogCache.TryAdd(dedupeKey, expiresUtc))
                return true;

            if (SpeechTokenFailureLogCache.TryUpdate(dedupeKey, expiresUtc, existingExpiresUtc))
                return true;
        }
    }

    protected virtual void PruneExpiredSpeechTokenFailureKeys(DateTime nowUtc)
    {
        foreach (var item in SpeechTokenFailureLogCache)
        {
            if (item.Value <= nowUtc)
                SpeechTokenFailureLogCache.TryRemove(item.Key, out _);
        }
    }

    protected static string BuildSpeechTokenFailureLog(
        string failureKind,
        InterviewSession session,
        string token,
        string region,
        string endpoint,
        string reason,
        int? httpStatus = null,
        string reasonPhrase = null,
        string azureResponseBody = null,
        Exception exception = null,
        int? responseLength = null,
        bool? speechKeyConfigured = null,
        bool? speechRegionConfigured = null)
    {
        var details = new List<string>
        {
            "Mode=speech-token",
            $"FailureKind={SanitizeSpeechTokenLogValue(failureKind, 80)}",
            $"Reason={SanitizeSpeechTokenLogValue(reason, 200)}",
            $"SessionId={session?.Id ?? 0}",
            $"CustomerId={session?.CustomerId ?? 0}",
            $"ProductId={session?.ProductId ?? 0}",
            $"TokenPrefix={BuildMaskedTokenPrefix(token)}",
            $"SpeechRegion={SanitizeSpeechTokenLogValue(region, 120)}"
        };

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            details.Add($"EndpointHost={BuildSpeechEndpointHost(endpoint)}");
            details.Add($"EndpointPath={BuildSpeechEndpointPath(endpoint)}");
        }

        if (speechKeyConfigured.HasValue)
            details.Add($"AzureSpeechKeyConfigured={speechKeyConfigured.Value.ToString().ToLowerInvariant()}");
        if (speechRegionConfigured.HasValue)
            details.Add($"AzureSpeechRegionConfigured={speechRegionConfigured.Value.ToString().ToLowerInvariant()}");
        if (httpStatus.HasValue)
            details.Add($"HttpStatus={httpStatus.Value}");
        if (!string.IsNullOrWhiteSpace(reasonPhrase))
            details.Add($"ReasonPhrase={SanitizeSpeechTokenLogValue(reasonPhrase, 120)}");
        if (responseLength.HasValue)
            details.Add($"ResponseLength={responseLength.Value}");
        if (!string.IsNullOrWhiteSpace(azureResponseBody))
            details.Add($"AzureResponseBody={SanitizeAzureResponseBody(azureResponseBody)}");
        if (exception != null)
        {
            details.Add($"ExceptionType={SanitizeSpeechTokenLogValue(exception.GetType().FullName ?? exception.GetType().Name, 200)}");
            details.Add($"ExceptionMessage={SanitizeSpeechTokenLogValue(exception.Message, 400)}");
            details.Add($"ExceptionDetail={SanitizeAzureResponseBody(exception.ToString())}");
        }

        return string.Join("; ", details);
    }

    protected virtual SpeechTokenResponseModel BuildSpeechTokenFailureResult(
        string failureKind,
        string diagnosticMessage,
        string message = SpeechUnavailableMessage,
        int? azureStatusCode = null,
        string azureReasonPhrase = null)
    {
        return new SpeechTokenResponseModel
        {
            Success = false,
            Message = string.IsNullOrWhiteSpace(message) ? SpeechUnavailableMessage : message,
            FailureKind = failureKind,
            AzureStatusCode = azureStatusCode,
            AzureReasonPhrase = string.IsNullOrWhiteSpace(azureReasonPhrase) ? null : SanitizeSpeechTokenLogValue(azureReasonPhrase, 120),
            DiagnosticMessage = string.IsNullOrWhiteSpace(diagnosticMessage) ? string.Empty : diagnosticMessage
        };
    }

    private static string SanitizeSensitiveSpeechValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        sanitized = SensitiveJsonValueRegex.Replace(sanitized, match =>
            $"{match.Groups[1].Value}***{match.Groups[3].Value}");
        sanitized = SensitiveKeyValueRegex.Replace(sanitized, match =>
            $"{match.Groups[1].Value}{match.Groups["quote"].Value}***{match.Groups["quote2"].Value}");
        sanitized = WhitespaceCollapseRegex.Replace(sanitized, " ").Trim();
        return sanitized;
    }

    private static string NormalizeSpeechTokenDedupeValue(string value, int maxLength)
    {
        return SanitizeSpeechTokenLogValue(value, maxLength).ToLowerInvariant();
    }

    private static string BuildSpeechTokenFailureBodyFingerprint(string responseBody)
    {
        var sanitized = SanitizeAzureResponseBody(responseBody);
        if (string.IsNullOrWhiteSpace(sanitized))
            return string.Empty;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sanitized));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    protected async Task<string> GetLocalizedTextAsync(string resourceKey, string fallback)
    {
        var text = await _localizationService.GetResourceAsync(resourceKey);
        if (string.IsNullOrWhiteSpace(text) ||
            string.Equals(text.Trim(), resourceKey, StringComparison.OrdinalIgnoreCase))
            return fallback;

        return text;
    }

    protected static JsonNode TryParseJsonNode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonNode.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    protected static JsonObject TryParseJsonObject(string json)
    {
        return TryParseJsonNode(json) as JsonObject;
    }

    protected static void SetJsonScoreValue(JsonObject node, string propertyName, decimal? value)
    {
        if (node != null && value.HasValue)
            node[propertyName] = value.Value;
    }

    protected static JsonObject ExtractPlanMetadataNode(string rubricJson)
    {
        var root = TryParseJsonObject(rubricJson);
        if (root == null)
            return null;

        if (root["plan"] is JsonObject existingPlan)
            return (JsonObject)existingPlan.DeepClone();

        var plan = new JsonObject();
        var hasPlanData = false;
        foreach (var propertyName in new[] { "category", "resumeEvidence", "expectedSignals", "rubric" })
        {
            if (root[propertyName] == null)
                continue;

            plan[propertyName] = root[propertyName].DeepClone();
            hasPlanData = true;
        }

        return hasPlanData ? plan : null;
    }

    protected static string ExtractPlanCategory(string rubricJson)
    {
        var plan = ExtractPlanMetadataNode(rubricJson);
        if (plan == null || plan["category"] == null)
            return string.Empty;

        try
        {
            return plan["category"].GetValue<string>() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    protected static JsonNode ExtractQuestionPlanRawNode(string rawJson)
    {
        var root = TryParseJsonObject(rawJson);
        if (root?["questionPlan"] != null)
            return root["questionPlan"].DeepClone();

        return TryParseJsonNode(rawJson)?.DeepClone();
    }

    protected static JsonNode ExtractScoringResponseNode(string rawJson)
    {
        var root = TryParseJsonObject(rawJson);
        if (root?["scoringResponse"] != null)
            return root["scoringResponse"].DeepClone();

        var parsed = TryParseJsonNode(rawJson);
        return parsed?.DeepClone() ?? (!string.IsNullOrWhiteSpace(rawJson) ? JsonValue.Create(rawJson) : null);
    }

    protected virtual string BuildMergedRubricJson(string existingRubricJson, AIInterviewClientResponse evaluation)
    {
        var merged = TryParseJsonObject(evaluation?.RubricJson) ?? new JsonObject();
        SetJsonScoreValue(merged, "technicalScore", evaluation?.TechnicalScore);
        SetJsonScoreValue(merged, "communicationScore", evaluation?.CommunicationScore);
        SetJsonScoreValue(merged, "professionalismScore", evaluation?.ProfessionalismScore);
        SetJsonScoreValue(merged, "positiveAttitudeScore", evaluation?.PositiveAttitudeScore);
        SetJsonScoreValue(merged, "score", evaluation?.Score);

        if (!string.IsNullOrWhiteSpace(evaluation?.Feedback))
            merged["feedback"] = evaluation.Feedback;

        var planNode = ExtractPlanMetadataNode(existingRubricJson);
        if (planNode != null)
            merged["plan"] = planNode;

        var scoringNode = TryParseJsonNode(evaluation?.RubricJson)?.DeepClone();
        if (scoringNode == null)
        {
            var scoringFallback = new JsonObject();
            SetJsonScoreValue(scoringFallback, "technicalScore", evaluation?.TechnicalScore);
            SetJsonScoreValue(scoringFallback, "communicationScore", evaluation?.CommunicationScore);
            SetJsonScoreValue(scoringFallback, "professionalismScore", evaluation?.ProfessionalismScore);
            SetJsonScoreValue(scoringFallback, "positiveAttitudeScore", evaluation?.PositiveAttitudeScore);
            SetJsonScoreValue(scoringFallback, "score", evaluation?.Score);
            if (!string.IsNullOrWhiteSpace(evaluation?.Feedback))
                scoringFallback["feedback"] = evaluation.Feedback;
            scoringNode = scoringFallback.Count > 0 ? scoringFallback : null;
        }

        if (scoringNode != null)
            merged["scoring"] = scoringNode;

        return JsonSerializer.Serialize(merged, StorageSerializerOptions);
    }

    protected virtual string BuildMergedRawAiResponseJson(string existingRawJson, string scoringRawJson)
    {
        var merged = new JsonObject();
        var questionPlanNode = ExtractQuestionPlanRawNode(existingRawJson);
        if (questionPlanNode != null)
            merged["questionPlan"] = questionPlanNode;

        var scoringNode = ExtractScoringResponseNode(scoringRawJson);
        if (scoringNode != null)
            merged["scoringResponse"] = scoringNode;

        if (merged.Count == 0)
            return !string.IsNullOrWhiteSpace(scoringRawJson) ? scoringRawJson : existingRawJson;

        return JsonSerializer.Serialize(merged, StorageSerializerOptions);
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

        var ensuredTurns = await EnsureSingleActiveTurnAsync(session, await _turnService.GetTurnsBySessionIdAsync(session.Id), customer);
        var turns = ensuredTurns.Turns.ToList();
        if (!turns.Any())
        {
            if (!string.IsNullOrWhiteSpace(ensuredTurns.FailureReason))
            {
                var unavailableModel = await BuildRuntimeModelAsync(session, turns, customer);
                unavailableModel.CurrentQuestion = "AI service unavailable. Please try again later.";
                unavailableModel.ClientSettings ??= new RuntimeClientSettingsModel();
                unavailableModel.ClientSettings.SpeechAvailable = false;
                var logCustomer = await ResolveLogCustomerAsync(session, customer);
                await LogRuntimeIssueAsync(
                    NopLogLevel.Warning,
                    "AI Interview question plan unavailable",
                    $"Mode=plan; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; Reason=question plan generation failed; Detail={BuildSafeValue(ensuredTurns.FailureReason ?? "AI service unavailable.")}.",
                    logCustomer);
                return unavailableModel;
            }
        }

        if (!session.StartedOnUtc.HasValue && InterviewTurnNormalizationHelper.GetVisibleRuntimeTurns(turns, GetMaxQuestions(session)).Any())
        {
            session.StartedOnUtc = DateTime.UtcNow;
            await _sessionService.UpdateInterviewSessionAsync(session);
            await LogRuntimeActivityAsync(
                session,
                "AIInterview.Runtime.InterviewStarted",
                BuildRuntimeActivityComment(session, message: "Interview started."),
                customer);
        }

        return await BuildRuntimeModelAsync(session, turns, customer);
    }

    public Task<SubmitInterviewAnswerResponse> SubmitAnswerAsync(string token, string answer)
    {
        return SubmitAnswerAsync(new SubmitInterviewAnswerRequest
        {
            Token = token,
            Answer = answer
        });
    }

    public async Task<SubmitInterviewAnswerResponse> SubmitAnswerAsync(SubmitInterviewAnswerRequest request)
    {
        var submitStopwatch = Stopwatch.StartNew();
        var token = request?.Token;
        var answer = request?.Answer;
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
            await LogRuntimeActivityAsync(
                session,
                "AIInterview.Runtime.AnswerSubmitFailed",
                BuildRuntimeActivityComment(session, message: "Answer cannot be empty.", elapsedMilliseconds: submitStopwatch.ElapsedMilliseconds));
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidAnswer", "Answer cannot be empty.")
            };
        }

        var turns = (await _turnService.GetTurnsBySessionIdAsync(session.Id))
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList();
        var maxQuestions = GetMaxQuestions(session);
        if (!turns.Any())
        {
            await LogRuntimeIssueAsync(
                NopLogLevel.Warning,
                "AI Interview submit before begin",
                $"Mode=score; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; Reason=submit before begin.",
                await ResolveLogCustomerAsync(session));
            await LogRuntimeActivityAsync(
                session,
                "AIInterview.Runtime.AnswerSubmitFailed",
                BuildRuntimeActivityComment(session, message: "Submit before begin.", elapsedMilliseconds: submitStopwatch.ElapsedMilliseconds));
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = "Interview has not started. Click Start Interview to begin."
            };
        }

        turns = (await EnsureSingleActiveTurnAsync(session, turns)).Turns.ToList();
        var currentTurn = ResolveCurrentTurn(turns, request?.TurnId, request?.SequenceNumber, maxQuestions);
        if (currentTurn == null)
        {
            await LogRuntimeIssueAsync(
                NopLogLevel.Warning,
                "AI Interview submit before begin",
                $"Mode=score; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; Reason=submit before begin.",
                await ResolveLogCustomerAsync(session));
            await LogRuntimeActivityAsync(
                session,
                "AIInterview.Runtime.AnswerSubmitFailed",
                BuildRuntimeActivityComment(session, message: "Active turn missing.", elapsedMilliseconds: submitStopwatch.ElapsedMilliseconds));
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = "Interview has not started. Click Start Interview to begin."
            };
        }

        var answerValidationMessage = await ValidateAnswerAsync(currentTurn.QuestionText, answer);
        if (!string.IsNullOrWhiteSpace(answerValidationMessage))
        {
            await LogRuntimeActivityAsync(
                session,
                "AIInterview.Runtime.AnswerSubmitFailed",
                BuildRuntimeActivityComment(session, currentTurn, answerValidationMessage, submitStopwatch.ElapsedMilliseconds));
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = answerValidationMessage
            };
        }

        if (IsFinalScoringAtCompletionEnabled())
            return await SubmitAnswerWithoutImmediateScoringAsync(session, turns, currentTurn, request, answer, submitStopwatch);

        var product = session.ProductId > 0 ? await _productService.GetProductByIdAsync(session.ProductId) : null;
        var jobTitle = product?.Name ?? await GetJobTitleAsync(session.SourceProductId > 0 ? session.SourceProductId : session.ProductId);
        var jobContext = BuildInterviewContext(session, product);
        var resumeProfileJson = await GetResumeProfileJsonAsync(session, product);
        var evaluation = await _aiClient.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = jobTitle,
            JobContext = jobContext,
            Difficulty = session.Difficulty,
            Prompt = _settings.Prompt,
            Question = currentTurn.QuestionText,
            Answer = answer,
            QuestionNumber = currentTurn.SequenceNumber,
            ResumeProfileJson = resumeProfileJson,
            CurrentTurnRubricJson = currentTurn.RubricJson,
            PreviousQuestions = InterviewTurnNormalizationHelper
                .GetCanonicalTurns(turns, maxQuestions)
                .Select(turn => turn.QuestionText)
                .ToList(),
            PreviousScores = InterviewTurnNormalizationHelper
                .GetCompletedReportTurns(turns, maxQuestions)
                .Where(turn => turn.Score.HasValue)
                .Select(turn => turn.Score.Value)
                .ToList(),
            PreviousTurns = BuildPreviousTurnContext(InterviewTurnNormalizationHelper.GetCanonicalTurns(turns, maxQuestions), currentTurn)
        });
        await TrackOpenAiUsageAsync(
            session.Id,
            currentTurn.Id,
            AzureUsageMetricDefaults.UsageKindOpenAiAnswerScoring,
            "ScoreAnswer",
            evaluation?.UsageInfo,
            evaluation?.AdditionalUsageInfos,
            JsonSerializer.Serialize(new
            {
                questionNumber = currentTurn.SequenceNumber,
                mode = "score"
            }, StorageSerializerOptions));

        if (evaluation == null || !evaluation.Success || !evaluation.Score.HasValue)
        {
            _logger?.LogWarning("SubmitAnswer score failure for session {SessionId}. Reason: {Reason}.",
                session.Id, evaluation?.ErrorMessage ?? "Invalid format/range");
            await LogRuntimeIssueAsync(
                NopLogLevel.Warning,
                "AI Interview scoring failure",
                $"Mode=score; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; Reason={evaluation?.ErrorMessage ?? "missing required score"}.",
                await ResolveLogCustomerAsync(session));
            await LogRuntimeActivityAsync(
                session,
                "AIInterview.Runtime.AnswerSubmitFailed",
                BuildRuntimeActivityComment(session, currentTurn, evaluation?.ErrorMessage ?? "Scoring failed.", submitStopwatch.ElapsedMilliseconds));
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = "The AI interview service is temporarily unavailable. Please try again later."
            };
        }

        currentTurn.AnswerText = answer;
        currentTurn.Score = Math.Clamp(evaluation.Score.Value, 0, 100);
        currentTurn.Feedback = evaluation.Feedback;
        currentTurn.RubricJson = BuildMergedRubricJson(currentTurn.RubricJson, evaluation);
        currentTurn.RawAIResponseJson = BuildMergedRawAiResponseJson(currentTurn.RawAIResponseJson, evaluation.RawJson);
        currentTurn.AnsweredOnUtc = DateTime.UtcNow;
        await _turnService.UpdateInterviewTurnAsync(currentTurn);
        await TrackSpeechRecognitionUsageAsync(session, currentTurn, request);
        await LogRuntimeActivityAsync(
            session,
            "AIInterview.Runtime.AnswerSubmitted",
            BuildRuntimeActivityComment(session, currentTurn, "Answer submitted.", submitStopwatch.ElapsedMilliseconds),
            entity: currentTurn);

        turns = (await _turnService.GetTurnsBySessionIdAsync(session.Id)).ToList();
        var completedTurns = InterviewTurnNormalizationHelper.GetCompletedReportTurns(turns, maxQuestions).ToList();
        var averageScore = completedTurns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).DefaultIfEmpty(0).Average();
        session.Score = averageScore;
        session.QuestionScores = JsonSerializer.Serialize(completedTurns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList());

        var answeredCount = completedTurns.Count;
        var shouldComplete = answeredCount >= maxQuestions;
        if (!shouldComplete)
        {
            var replenishedTurns = await EnsureSingleActiveTurnAsync(session, turns);
            turns = replenishedTurns.Turns.ToList();
            var nextTurn = InterviewTurnNormalizationHelper.GetActivePendingTurn(turns, maxQuestions);

            if (nextTurn == null)
            {
                await LogRuntimeIssueAsync(
                    NopLogLevel.Warning,
                    "AI Interview next planned question unavailable",
                    $"Mode=plan; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; ConfiguredQuestions={maxQuestions}; ExistingTurns={turns.Count}; AnsweredTurns={answeredCount}; Reason=next planned turn missing.",
                    await ResolveLogCustomerAsync(session));
                await LogRuntimeActivityAsync(
                    session,
                    "AIInterview.Runtime.AnswerSubmitFailed",
                    BuildRuntimeActivityComment(session, currentTurn, "Next planned question missing.", submitStopwatch.ElapsedMilliseconds),
                    entity: currentTurn);
                return new SubmitInterviewAnswerResponse
                {
                    Success = false,
                    Message = "The AI interview service is temporarily unavailable. Please try again later.",
                    Feedback = "The AI interview service is temporarily unavailable. Please try again later."
                };
            }

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
                Turn = MapTurn(currentTurn),
                Turns = MapTurns(InterviewTurnNormalizationHelper.GetVisibleRuntimeTurns(turns, maxQuestions))
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
            Turn = MapTurn(currentTurn),
            Turns = completion.Turns
        };
    }

    protected virtual async Task<SubmitInterviewAnswerResponse> SubmitAnswerWithoutImmediateScoringAsync(
        InterviewSession session,
        IList<InterviewTurn> turns,
        InterviewTurn currentTurn,
        SubmitInterviewAnswerRequest request,
        string answer,
        Stopwatch submitStopwatch)
    {
        var maxQuestions = GetMaxQuestions(session);

        currentTurn.AnswerText = answer;
        currentTurn.AnsweredOnUtc = DateTime.UtcNow;
        await _turnService.UpdateInterviewTurnAsync(currentTurn);
        await TrackSpeechRecognitionUsageAsync(session, currentTurn, request);
        await LogRuntimeIssueAsync(
            NopLogLevel.Information,
            "AI Interview submit persisted without scoring",
            $"Mode=submit; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; TurnId={currentTurn.Id}; SequenceNumber={currentTurn.SequenceNumber}; Reason=final scoring at completion enabled; ElapsedMs={submitStopwatch.ElapsedMilliseconds}.",
            await ResolveLogCustomerAsync(session));
        await LogRuntimeActivityAsync(
            session,
            "AIInterview.Runtime.AnswerSubmitted",
            BuildRuntimeActivityComment(session, currentTurn, "Answer submitted without immediate scoring.", submitStopwatch.ElapsedMilliseconds),
            entity: currentTurn);

        turns = (await _turnService.GetTurnsBySessionIdAsync(session.Id)).ToList();
        var completedTurns = InterviewTurnNormalizationHelper.GetCompletedReportTurns(turns, maxQuestions).ToList();
        var answeredCount = completedTurns.Count;
        var shouldComplete = answeredCount >= maxQuestions;
        if (!shouldComplete)
        {
            var replenishedTurns = await EnsureSingleActiveTurnAsync(session, turns);
            turns = replenishedTurns.Turns.ToList();
            var nextTurn = InterviewTurnNormalizationHelper.GetActivePendingTurn(turns, maxQuestions);

            if (nextTurn == null)
            {
                await LogRuntimeIssueAsync(
                    NopLogLevel.Warning,
                    "AI Interview next planned question unavailable",
                    $"Mode=plan; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; ConfiguredQuestions={maxQuestions}; ExistingTurns={turns.Count}; AnsweredTurns={answeredCount}; Reason=next planned turn missing.",
                    await ResolveLogCustomerAsync(session));
                await LogRuntimeActivityAsync(
                    session,
                    "AIInterview.Runtime.AnswerSubmitFailed",
                    BuildRuntimeActivityComment(session, currentTurn, "Next planned question missing.", submitStopwatch.ElapsedMilliseconds),
                    entity: currentTurn);
                return new SubmitInterviewAnswerResponse
                {
                    Success = false,
                    Message = "The AI interview service is temporarily unavailable. Please try again later.",
                    Feedback = "The AI interview service is temporarily unavailable. Please try again later."
                };
            }

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
                Turn = MapTurn(currentTurn),
                Turns = MapTurns(InterviewTurnNormalizationHelper.GetVisibleRuntimeTurns(turns, maxQuestions))
            };
        }

        var completion = await CompleteInterviewInternalAsync(session, turns, "Interview completed.");
        return new SubmitInterviewAnswerResponse
        {
            Success = completion.Success,
            IsTerminated = completion.IsTerminated,
            Completion = completion.Completion,
            Score = completion.Score,
            Message = completion.Message,
            ReportUrl = completion.ReportUrl,
            Interrupted = false,
            Question = string.Empty,
            Turn = MapTurn(currentTurn),
            Turns = completion.Turns,
            ReportGenerationInProgress = completion.ReportGenerationInProgress,
            EstimatedWaitSeconds = completion.EstimatedWaitSeconds
        };
    }

    public async Task<CompleteInterviewResponse> CompleteInterviewAsync(string token, string reason = null)
    {
        var session = await _sessionService.GetSessionByTokenAsync(token);
        var now = DateTime.UtcNow;
        if (session == null || (!IsSessionUsable(session, now) && !session.CompletedOnUtc.HasValue))
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
        if (!IsSessionUsable(session, now))
        {
            await LogRuntimeActivityAsync(
                session,
                "AIInterview.Runtime.SpeechUnavailable",
                BuildRuntimeActivityComment(session, message: "Speech token requested for invalid session.", failureKind: "invalid-session"));
            return BuildSpeechTokenFailureResult(
                "invalid-session",
                BuildSpeechTokenFailureLog("invalid-session", session, token, _settings?.AzureSpeechRegion, null, "session unavailable"));
        }

        var speechKeyConfigured = !string.IsNullOrWhiteSpace(_settings?.AzureSpeechKey);
        var speechRegionConfigured = !string.IsNullOrWhiteSpace(_settings?.AzureSpeechRegion);
        if (!speechKeyConfigured || !speechRegionConfigured)
        {
            var diagnosticMessage = BuildSpeechTokenFailureLog(
                "configuration-incomplete",
                session,
                token,
                _settings?.AzureSpeechRegion,
                null,
                "configuration incomplete",
                speechKeyConfigured: speechKeyConfigured,
                speechRegionConfigured: speechRegionConfigured);
            var dedupeKey = BuildSpeechTokenFailureDedupeKey(
                "configuration-incomplete",
                session,
                token,
                _settings?.AzureSpeechRegion,
                speechKeyConfigured: speechKeyConfigured,
                speechRegionConfigured: speechRegionConfigured);
            if (ShouldLogSpeechTokenFailure(dedupeKey, now))
            {
                await LogRuntimeIssueAsync(
                    NopLogLevel.Warning,
                    "AI Interview speech token unavailable",
                    diagnosticMessage,
                    await ResolveLogCustomerAsync(session));
            }
            await LogRuntimeActivityAsync(
                session,
                "AIInterview.Runtime.SpeechUnavailable",
                BuildRuntimeActivityComment(session, message: "Speech configuration incomplete.", failureKind: "configuration-incomplete"));
            return BuildSpeechTokenFailureResult("configuration-incomplete", diagnosticMessage);
        }

        var region = _settings.AzureSpeechRegion.Trim();
        var endpoint = $"https://{region}.api.cognitive.microsoft.com/sts/v1.0/issuetoken";

        try
        {
            using var httpClient = CreateHttpClient();
            httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _settings.AzureSpeechKey.Trim());
            var response = await httpClient.PostAsync(endpoint, new StringContent(string.Empty));
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var diagnosticMessage = BuildSpeechTokenFailureLog(
                    "azure-http-failure",
                    session,
                    token,
                    region,
                    endpoint,
                    "azure-http-failure",
                    httpStatus: (int)response.StatusCode,
                    reasonPhrase: response.ReasonPhrase,
                    azureResponseBody: responseBody);
                _logger?.LogWarning("Azure Speech token request failed. Region: {Region}. Status: {StatusCode}.",
                    region, response.StatusCode);
                var dedupeKey = BuildSpeechTokenFailureDedupeKey(
                    "azure-http-failure",
                    session,
                    token,
                    region,
                    httpStatus: (int)response.StatusCode,
                    reasonPhrase: response.ReasonPhrase,
                    azureResponseBody: responseBody);
                if (ShouldLogSpeechTokenFailure(dedupeKey, DateTime.UtcNow))
                {
                    await LogRuntimeIssueAsync(
                        NopLogLevel.Warning,
                        "AI Interview speech token failure",
                        diagnosticMessage,
                        await ResolveLogCustomerAsync(session));
                }
                await LogRuntimeActivityAsync(
                    session,
                    "AIInterview.Runtime.SpeechUnavailable",
                    BuildRuntimeActivityComment(session, message: "Azure Speech token request failed.", statusCode: (int)response.StatusCode, failureKind: "azure-http-failure"));
                return BuildSpeechTokenFailureResult("azure-http-failure", diagnosticMessage, azureStatusCode: (int)response.StatusCode, azureReasonPhrase: response.ReasonPhrase);
            }

            var tokenValue = responseBody?.Trim();
            if (string.IsNullOrWhiteSpace(tokenValue))
            {
                var diagnosticMessage = BuildSpeechTokenFailureLog(
                    "empty-token-response",
                    session,
                    token,
                    region,
                    endpoint,
                    "empty token response",
                    httpStatus: (int)response.StatusCode,
                    reasonPhrase: response.ReasonPhrase,
                    responseLength: responseBody?.Length ?? 0);
                _logger?.LogWarning("Azure Speech token request returned an empty token. Region: {Region}.", region);
                var dedupeKey = BuildSpeechTokenFailureDedupeKey(
                    "empty-token-response",
                    session,
                    token,
                    region,
                    httpStatus: (int)response.StatusCode,
                    reasonPhrase: response.ReasonPhrase);
                if (ShouldLogSpeechTokenFailure(dedupeKey, DateTime.UtcNow))
                {
                    await LogRuntimeIssueAsync(
                        NopLogLevel.Warning,
                        "AI Interview speech token failure",
                        diagnosticMessage,
                        await ResolveLogCustomerAsync(session));
                }
                await LogRuntimeActivityAsync(
                    session,
                    "AIInterview.Runtime.SpeechUnavailable",
                    BuildRuntimeActivityComment(session, message: "Azure Speech token response was empty.", statusCode: (int)response.StatusCode, failureKind: "empty-token-response"));
                return BuildSpeechTokenFailureResult("empty-token-response", diagnosticMessage, azureStatusCode: (int)response.StatusCode, azureReasonPhrase: response.ReasonPhrase);
            }

            return new SpeechTokenResponseModel
            {
                Success = true,
                Token = tokenValue,
                Region = region,
                ExpiresInSeconds = 600
            };
        }
        catch (Exception ex)
        {
            var diagnosticMessage = BuildSpeechTokenFailureLog(
                "azure-exception",
                session,
                token,
                region,
                endpoint,
                "azure-exception",
                exception: ex);
            _logger?.LogWarning(ex, "Azure Speech token request exception. Region: {Region}.", region);
            var dedupeKey = BuildSpeechTokenFailureDedupeKey(
                "azure-exception",
                session,
                token,
                region,
                exception: ex);
            if (ShouldLogSpeechTokenFailure(dedupeKey, DateTime.UtcNow))
            {
                await LogRuntimeIssueAsync(
                    NopLogLevel.Error,
                    "AI Interview speech token exception",
                    diagnosticMessage,
                    await ResolveLogCustomerAsync(session));
            }
            await LogRuntimeActivityAsync(
                session,
                "AIInterview.Runtime.SpeechUnavailable",
                BuildRuntimeActivityComment(session, message: ex.Message, failureKind: "azure-exception"));
            return BuildSpeechTokenFailureResult("azure-exception", diagnosticMessage);
        }
    }

    public async Task TrackSpeechSynthesisUsageAsync(SpeechSynthesisUsageRequest request)
    {
        if (request == null || _azureUsageService == null)
            return;

        var session = await _sessionService.GetSessionByTokenAsync(request.Token);
        if (!IsSessionUsable(session, DateTime.UtcNow))
            return;

        var turns = await _turnService.GetTurnsBySessionIdAsync(session.Id);
        var turn = ResolveSpeechTurn(turns, request.TurnId, request.SequenceNumber);

        await _azureUsageService.RecordSpeechUsageAsync(new AzureSpeechUsageRecordRequest
        {
            InterviewSessionId = session.Id,
            InterviewTurnId = turn?.Id,
            UsageKind = AzureUsageMetricDefaults.UsageKindSpeechSynthesis,
            OperationName = string.IsNullOrWhiteSpace(request.Purpose) ? "SpeechSynthesis" : request.Purpose.Trim(),
            SpeechSynthesisCharacters = request.SpeechSynthesisCharacters,
            ClientEventId = request.ClientEventId,
            MetadataJson = JsonSerializer.Serialize(new
            {
                source = "browser",
                purpose = request.Purpose,
                sequenceNumber = request.SequenceNumber,
                reportedTurnId = request.TurnId,
                speechRegion = _settings?.AzureSpeechRegion
            }, StorageSerializerOptions)
        });
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
        var questionCount = GetMaxQuestions(session);
        var visibleTurns = InterviewTurnNormalizationHelper.GetVisibleRuntimeTurns(turns, questionCount).ToList();
        var currentTurn = InterviewTurnNormalizationHelper.GetActivePendingTurn(turns, questionCount)
            ?? visibleTurns.LastOrDefault();
        var lastQuestion = currentTurn?.QuestionText ?? string.Empty;
        var isPracticeInterview = string.Equals(NormalizeInterviewType(session), AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.OrdinalIgnoreCase);
        var practiceSkill = isPracticeInterview ? ExtractPracticeSkill(session.SelectedProductAttributesJson, session.Difficulty) : string.Empty;
        var runtimeTopic = ResolveRuntimeTopic(session, product, practiceSkill);
        if (isPracticeInterview)
        {
            _logger?.LogDebug(
                "Runtime practice display context for session {SessionId}: difficulty={Difficulty}; selectedInputs={SelectedProductAttributesJson}; extractedSkill={PracticeSkill}.",
                session.Id,
                session.Difficulty,
                session.SelectedProductAttributesJson,
                practiceSkill);
        }

        return new InterviewRuntimeModel
        {
            SessionId = session.Id,
            ProductId = session.ProductId,
            QuestionCount = questionCount,
            SessionKey = session.SessionKey,
            Token = session.Token,
            ProductName = product?.Name ?? "Interview",
            CandidateName = candidate != null ? $"{candidate.FirstName} {candidate.LastName}".Trim() : string.Empty,
            Difficulty = session.Difficulty,
            IsPracticeInterview = isPracticeInterview,
            PracticeSkill = practiceSkill,
            RuntimeTopic = runtimeTopic,
            CurrentQuestion = lastQuestion,
            Score = session.Score,
            IsCompleted = session.CompletedOnUtc.HasValue,
            IsMockMode = _mockSettings?.UseMockResponses ?? true,
            SupportPhoneNumber = string.IsNullOrWhiteSpace(_settings.SupportPhoneNumber) ? AIInterviewDefaults.DefaultSupportPhoneNumber : _settings.SupportPhoneNumber.Trim(),
            ReportUrl = string.Empty,
            TokenExpiryUtc = session.TokenExpiryUtc,
            Turns = MapTurns(visibleTurns),
            ClientSettings = new RuntimeClientSettingsModel
            {
                QuestionCount = questionCount,
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

    protected virtual async Task<(IList<InterviewTurn> Turns, string FailureReason)> EnsureQuestionPlanAsync(InterviewSession session, IList<InterviewTurn> turns, Customer customer = null)
    {
        turns ??= new List<InterviewTurn>();
        var maxQuestions = GetMaxQuestions(session);
        var orderedTurns = turns
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList();
        var configuredSequenceNumbers = Enumerable.Range(1, maxQuestions).ToList();
        var existingSequenceNumbers = new HashSet<int>(orderedTurns.Select(turn => turn.SequenceNumber));
        var missingSequenceNumbers = configuredSequenceNumbers
            .Where(sequenceNumber => !existingSequenceNumbers.Contains(sequenceNumber))
            .ToList();

        if (!missingSequenceNumbers.Any())
            return (orderedTurns, null);

        if (orderedTurns.Any() && orderedTurns.All(turn => string.IsNullOrWhiteSpace(turn.AnswerText)))
        {
            await _turnService.DeleteInterviewTurnsAsync(orderedTurns);
            orderedTurns = new List<InterviewTurn>();
            missingSequenceNumbers = configuredSequenceNumbers;
        }

        var generatedPlan = await GenerateQuestionPlanTurnsAsync(session, customer, orderedTurns, missingSequenceNumbers);
        if (!generatedPlan.Turns.Any())
            return (orderedTurns, generatedPlan.FailureReason);

        existingSequenceNumbers = new HashSet<int>(orderedTurns.Select(turn => turn.SequenceNumber));
        foreach (var turn in generatedPlan.Turns.Where(turn => !existingSequenceNumbers.Contains(turn.SequenceNumber)))
        {
            var inserted = await _turnService.InsertInterviewTurnAsync(turn);
            orderedTurns.Add(inserted);
        }

        return (orderedTurns
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList(), null);
    }

    protected virtual async Task<(IList<InterviewTurn> Turns, string FailureReason)> GenerateQuestionPlanTurnsAsync(InterviewSession session, Customer customer = null, IList<InterviewTurn> existingTurns = null, IList<int> targetSequenceNumbers = null)
    {
        var product = session.ProductId > 0 ? await _productService.GetProductByIdAsync(session.ProductId) : null;
        var totalQuestionCount = GetMaxQuestions(session);
        var sequenceNumbers = (targetSequenceNumbers?.Any() == true
                ? targetSequenceNumbers
                : Enumerable.Range(1, totalQuestionCount).ToList())
            .Distinct()
            .OrderBy(sequenceNumber => sequenceNumber)
            .ToList();
        var questionCount = sequenceNumbers.Count;
        if (questionCount <= 0)
            return (Array.Empty<InterviewTurn>(), null);

        var locallyPlannedTurns = new List<InterviewTurn>();
        if (sequenceNumbers.Contains(1))
        {
            locallyPlannedTurns.Add(BuildIntroductionProjectTurn(session, customer));
            sequenceNumbers.Remove(1);
            questionCount = sequenceNumbers.Count;
        }

        if (questionCount <= 0)
            return (locallyPlannedTurns, null);

        var plannedContext = (existingTurns ?? new List<InterviewTurn>())
            .Concat(locallyPlannedTurns)
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList();
        var response = await _aiClient.GenerateQuestionPlanAsync(new AIInterviewQuestionPlanRequest
        {
            JobTitle = product?.Name ?? await GetJobTitleAsync(session.SourceProductId > 0 ? session.SourceProductId : session.ProductId),
            JobContext = BuildInterviewContext(session, product),
            Difficulty = session.Difficulty,
            QuestionCount = questionCount,
            TotalQuestionCount = totalQuestionCount,
            Prompt = _settings.Prompt,
            ResumeProfileJson = await GetResumeProfileJsonAsync(session, product),
            ExistingQuestions = plannedContext
                .Select(turn => turn.QuestionText?.Trim())
                .Where(questionText => !string.IsNullOrWhiteSpace(questionText))
                .ToList(),
            ExistingCategories = plannedContext
                .Select(turn => ExtractPlanCategory(turn.RubricJson))
                .Where(category => !string.IsNullOrWhiteSpace(category) &&
                    !string.Equals(category, "Introduction & Project Experience", StringComparison.OrdinalIgnoreCase))
                .ToList()
        });
        await TrackOpenAiUsageAsync(
            session.Id,
            null,
            AzureUsageMetricDefaults.UsageKindOpenAiQuestionPlanning,
            "GenerateQuestionPlan",
            response?.UsageInfo,
            metadataJson: JsonSerializer.Serialize(new
            {
                requestedQuestionCount = questionCount,
                totalQuestionCount,
                targetSequenceNumbers = sequenceNumbers
            }, StorageSerializerOptions));

        if (response == null || !response.Success || response.Questions == null)
            return (Array.Empty<InterviewTurn>(), response?.ErrorMessage ?? "Question plan generation failed.");

        var plannedQuestions = response.Questions
            .Where(question => question != null && !string.IsNullOrWhiteSpace(question.Question))
            .Take(questionCount)
            .ToList();
        if (plannedQuestions.Count != questionCount)
            return (Array.Empty<InterviewTurn>(), "Question plan did not return the configured number of questions.");

        var now = DateTime.UtcNow;
        var turns = plannedQuestions.Select((question, index) => new InterviewTurn
        {
            InterviewSessionId = session.Id,
            SequenceNumber = sequenceNumbers[index],
            QuestionId = sequenceNumbers[index],
            QuestionText = question.Question.Trim(),
            RubricJson = JsonSerializer.Serialize(new
            {
                category = question.Category,
                resumeEvidence = question.ResumeEvidence,
                expectedSignals = question.ExpectedSignals ?? Array.Empty<string>(),
                rubric = question.Rubric ?? new AIInterviewQuestionRubric()
            }, StorageSerializerOptions),
            RawAIResponseJson = JsonSerializer.Serialize(question, StorageSerializerOptions),
            AskedOnUtc = now,
            CreatedOnUtc = now
        }).ToList();

        return (locallyPlannedTurns.Concat(turns).OrderBy(turn => turn.SequenceNumber).ToList(), null);
    }

    protected virtual InterviewTurn BuildIntroductionProjectTurn(InterviewSession session, Customer customer)
    {
        var now = DateTime.UtcNow;
        var questionText = BuildIntroductionProjectQuestionText(customer);
        var rubric = new AIInterviewQuestionRubric
        {
            Technical = "Evaluate evidence of real project experience, architecture or implementation details, tools, tradeoffs, debugging or challenges, and impact.",
            Communication = "Evaluate clarity, structure, confidence, and ability to explain experience naturally.",
            Professionalism = "Evaluate ownership, honesty, relevance to the role, maturity, and responsibility.",
            PositiveAttitude = "Evaluate curiosity, learning mindset, constructive framing, and motivation."
        };
        var expectedSignals = new[]
        {
            "Clear self-introduction",
            "Relevant project ownership",
            "Technologies used",
            "Implementation details and tradeoffs",
            "Challenges solved",
            "Measurable impact or outcome",
            "Communication clarity"
        };
        var planItem = new AIInterviewQuestionPlanItem
        {
            SequenceNumber = 1,
            Category = "Introduction & Project Experience",
            Question = questionText,
            ResumeEvidence = string.Empty,
            ExpectedSignals = expectedSignals,
            Rubric = rubric
        };

        return new InterviewTurn
        {
            InterviewSessionId = session.Id,
            SequenceNumber = 1,
            QuestionId = 1,
            QuestionText = questionText,
            RubricJson = JsonSerializer.Serialize(new
            {
                category = planItem.Category,
                resumeEvidence = planItem.ResumeEvidence,
                expectedSignals,
                rubric
            }, StorageSerializerOptions),
            RawAIResponseJson = JsonSerializer.Serialize(planItem, StorageSerializerOptions),
            AskedOnUtc = now,
            CreatedOnUtc = now
        };
    }

    protected static string BuildIntroductionProjectQuestionText(Customer customer)
    {
        var candidateName = string.Join(" ", new[] { customer?.FirstName, customer?.LastName }
                .Where(namePart => !string.IsNullOrWhiteSpace(namePart))
                .Select(namePart => namePart.Trim()))
            .Trim();

        return string.IsNullOrWhiteSpace(candidateName)
            ? "Let's start with you. Please introduce yourself and walk me through one or two projects you are most proud of. I'd like to understand your role, the technologies you used, the main challenges you handled, and the impact of the work."
            : $"Hello {candidateName}, let's start with you. Please introduce yourself and walk me through one or two projects you are most proud of. I'd like to understand your role, the technologies you used, the main challenges you handled, and the impact of the work.";
    }

    protected virtual async Task<(InterviewTurn Turn, string FailureReason)> GenerateQuestionTurnAsync(InterviewSession session, int sequenceNumber, IList<InterviewTurn> turns)
    {
        var product = session.ProductId > 0 ? await _productService.GetProductByIdAsync(session.ProductId) : null;
        var request = new AIInterviewClientRequest
        {
            JobTitle = product?.Name ?? await GetJobTitleAsync(session.SourceProductId > 0 ? session.SourceProductId : session.ProductId),
            JobContext = BuildInterviewContext(session, product),
            Difficulty = session.Difficulty,
            Prompt = _settings.Prompt,
            QuestionNumber = sequenceNumber,
            ResumeProfileJson = await GetResumeProfileJsonAsync(session, product),
            PreviousQuestions = turns.Select(turn => turn.QuestionText).ToList(),
            PreviousScores = turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList(),
            PreviousTurns = BuildPreviousTurnContext(turns)
        };

        var aiResponse = await _aiClient.GenerateQuestionAsync(request);
        await TrackOpenAiUsageAsync(
            session.Id,
            null,
            AzureUsageMetricDefaults.UsageKindOpenAiQuestionGeneration,
            "GenerateQuestion",
            aiResponse?.UsageInfo,
            aiResponse?.AdditionalUsageInfos,
            JsonSerializer.Serialize(new
            {
                sequenceNumber,
                mode = "generate"
            }, StorageSerializerOptions));
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

    protected virtual async Task TrackOpenAiUsageAsync(int interviewSessionId, int? interviewTurnId, string usageKind, string operationName, AzureOpenAiUsageInfo usageInfo, IEnumerable<AzureOpenAiUsageInfo> additionalUsageInfos = null, string metadataJson = null)
    {
        if (_azureUsageService == null || interviewSessionId <= 0)
            return;

        var usageEvents = new List<AzureOpenAiUsageInfo>();
        if (additionalUsageInfos != null)
            usageEvents.AddRange(additionalUsageInfos.Where(item => item != null));
        if (usageInfo != null)
            usageEvents.Add(usageInfo);

        foreach (var usageEvent in usageEvents)
        {
            await _azureUsageService.RecordOpenAiUsageAsync(new AzureOpenAiUsageRecordRequest
            {
                InterviewSessionId = interviewSessionId,
                InterviewTurnId = interviewTurnId,
                UsageKind = usageKind,
                OperationName = operationName,
                UsageInfo = usageEvent,
                MetadataJson = metadataJson
            });
        }
    }

    protected virtual async Task TrackSpeechRecognitionUsageAsync(InterviewSession session, InterviewTurn currentTurn, SubmitInterviewAnswerRequest request)
    {
        if (_azureUsageService == null || session == null || request == null)
            return;

        await _azureUsageService.RecordSpeechUsageAsync(new AzureSpeechUsageRecordRequest
        {
            InterviewSessionId = session.Id,
            InterviewTurnId = currentTurn?.Id,
            UsageKind = AzureUsageMetricDefaults.UsageKindSpeechRecognition,
            OperationName = "SpeechRecognition",
            SpeechRecognitionCharacters = request.SpeechRecognitionCharacters,
            SpeechDurationMs = request.SpeechRecognitionDurationMs,
            ClientEventId = request.SpeechRecognitionClientEventId,
            MetadataJson = JsonSerializer.Serialize(new
            {
                source = "browser",
                eventType = "answerSubmit",
                sequenceNumber = currentTurn?.SequenceNumber,
                speechRegion = _settings?.AzureSpeechRegion
            }, StorageSerializerOptions)
        });
    }

    protected virtual InterviewTurn ResolveSpeechTurn(IList<InterviewTurn> turns, int? turnId, int? sequenceNumber)
    {
        if (turns == null || !turns.Any())
            return null;

        if (turnId.GetValueOrDefault() > 0)
        {
            var matchedTurn = turns.FirstOrDefault(turn => turn.Id == turnId.Value);
            if (matchedTurn != null)
                return matchedTurn;
        }

        if (sequenceNumber.GetValueOrDefault() > 0)
        {
            return turns
                .Where(turn => turn.SequenceNumber == sequenceNumber.Value)
                .OrderByDescending(turn => turn.Id)
                .FirstOrDefault();
        }

        return turns
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .FirstOrDefault(turn => string.IsNullOrWhiteSpace(turn.AnswerText))
            ?? turns.OrderByDescending(turn => turn.SequenceNumber).ThenByDescending(turn => turn.Id).FirstOrDefault();
    }

    protected virtual InterviewTurn ResolveCurrentTurn(IList<InterviewTurn> turns, int? turnId, int? sequenceNumber, int maxQuestions)
    {
        if (turns == null || !turns.Any())
            return null;

        var nextSequenceNumber = InterviewTurnNormalizationHelper.GetNextSequenceNumber(turns, maxQuestions);
        var pendingTurns = turns
            .Where(turn => turn != null
                && turn.SequenceNumber == nextSequenceNumber
                && !InterviewTurnNormalizationHelper.HasAnswer(turn))
            .OrderByDescending(turn => turn.Id)
            .ToList();
        if (!pendingTurns.Any())
            return null;

        if (turnId.GetValueOrDefault() > 0)
        {
            var matchedById = pendingTurns.FirstOrDefault(turn => turn.Id == turnId.Value);
            if (matchedById != null)
                return matchedById;
        }

        if (sequenceNumber.GetValueOrDefault() > 0)
        {
            var matchedBySequence = pendingTurns.FirstOrDefault(turn => turn.SequenceNumber == sequenceNumber.Value);
            if (matchedBySequence != null)
                return matchedBySequence;
        }

        return pendingTurns.FirstOrDefault();
    }

    protected static InterviewTurnViewModel MapTurn(InterviewTurn turn)
    {
        return turn == null
            ? null
            : new InterviewTurnViewModel
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
            };
    }

    protected static IList<InterviewTurnViewModel> MapTurns(IEnumerable<InterviewTurn> turns)
    {
        return (turns ?? Enumerable.Empty<InterviewTurn>())
            .Select(MapTurn)
            .Where(turn => turn != null)
            .ToList();
    }

    protected virtual async Task<CompleteInterviewResponse> CompleteInterviewInternalAsync(InterviewSession session, IList<InterviewTurn> turns, string reason, string aiCompletion = null)
    {
        var completionStopwatch = Stopwatch.StartNew();
        _logger?.LogInformation("Stop called with session id {SessionId}", session.Id);

        if (session.CompletedOnUtc.HasValue || !session.IsActive)
        {
            await LogRuntimeActivityAsync(
                session,
                "AIInterview.Runtime.InterviewCompleted",
                BuildRuntimeActivityComment(session, message: "Session already completed or inactive.", elapsedMilliseconds: completionStopwatch.ElapsedMilliseconds));
            return new CompleteInterviewResponse
            {
                Success = session.CompletedOnUtc.HasValue,
                IsTerminated = true,
                Score = session.Score,
                Feedback = InterviewTurnNormalizationHelper.GetCompletedReportTurns(turns, GetMaxQuestions(session)).LastOrDefault()?.Feedback ?? reason ?? string.Empty,
                Message = session.CompletedOnUtc.HasValue
                    ? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Interview.CompletedScore")
                    : await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken"),
                Completion = session.ReportData,
                ReportUrl = string.Empty,
                Turns = MapTurns(InterviewTurnNormalizationHelper.GetCompletedReportTurns(turns, GetMaxQuestions(session)))
            };
        }

        var completedTurns = InterviewTurnNormalizationHelper.GetCompletedReportTurns(turns, GetMaxQuestions(session)).ToList();
        if (IsFinalScoringAtCompletionEnabled())
        {
            var finalScoring = await FinalizeInterviewScoringAsync(session, completedTurns, reason);
            if (!finalScoring.Success)
            {
                await LogRuntimeActivityAsync(
                    session,
                    "AIInterview.Runtime.CompletionFinalizationFailed",
                    BuildRuntimeActivityComment(session, message: finalScoring.Message, elapsedMilliseconds: completionStopwatch.ElapsedMilliseconds));
                return finalScoring;
            }

            turns = finalScoring.Turns.Any()
                ? completedTurns
                : turns;
        }

        completedTurns = InterviewTurnNormalizationHelper.GetCompletedReportTurns(turns, GetMaxQuestions(session)).ToList();
        session.IsActive = false;
        session.CompletedOnUtc = DateTime.UtcNow;
        session.Score = completedTurns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).DefaultIfEmpty(0).Average();
        session.QuestionScores = JsonSerializer.Serialize(completedTurns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList());
        session.ReportData = BuildReport(completedTurns, session.Score, reason, aiCompletion);
        await _sessionService.UpdateInterviewSessionAsync(session);

        await PublishCompletionAsync(session);
        await LogRuntimeActivityAsync(
            session,
            "AIInterview.Runtime.InterviewCompleted",
            BuildRuntimeActivityComment(session, message: "Interview completed.", elapsedMilliseconds: completionStopwatch.ElapsedMilliseconds));

        var completion = new CompleteInterviewResponse
        {
            Success = true,
            IsTerminated = true,
            Score = session.Score,
            Feedback = completedTurns.LastOrDefault()?.Feedback ?? reason ?? string.Empty,
            Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Interview.CompletedScore"),
            Completion = session.ReportData,
            ReportUrl = string.Empty,
            ReportGenerationInProgress = IsFinalScoringAtCompletionEnabled(),
            EstimatedWaitSeconds = IsFinalScoringAtCompletionEnabled() ? 90 : 0,
            Turns = MapTurns(completedTurns)
        };

        _logger?.LogInformation("Interview completed for session {SessionId}, customer {CustomerId}, product {ProductId}.",
            session.Id, session.CustomerId, session.ProductId);

        return completion;
    }

    protected virtual bool IsFinalScoringAtCompletionEnabled()
    {
        return _settings?.EnableFinalScoringAtCompletion != false;
    }

    protected virtual async Task<CompleteInterviewResponse> FinalizeInterviewScoringAsync(InterviewSession session, IList<InterviewTurn> turns, string reason)
    {
        var finalScoringStopwatch = Stopwatch.StartNew();
        var answeredTurns = InterviewTurnNormalizationHelper
            .GetCompletedReportTurns(turns, GetMaxQuestions(session))
            .Where(turn => !string.IsNullOrWhiteSpace(turn.AnswerText))
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList();

        await LogRuntimeIssueAsync(
            NopLogLevel.Information,
            "AI Interview final scoring started",
            $"Mode=final-score; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; AnsweredTurns={answeredTurns.Count}; Reason={BuildSafeValue(reason)}.",
            await ResolveLogCustomerAsync(session));

        try
        {
            var product = session.ProductId > 0 ? await _productService.GetProductByIdAsync(session.ProductId) : null;
            var jobTitle = product?.Name ?? await GetJobTitleAsync(session.SourceProductId > 0 ? session.SourceProductId : session.ProductId);
            var jobContext = BuildInterviewContext(session, product);
            var resumeProfileJson = await GetResumeProfileJsonAsync(session, product);

            foreach (var turn in answeredTurns.Where(turn => !turn.Score.HasValue))
            {
                var evaluation = await _aiClient.ScoreAnswerAsync(new AIInterviewClientRequest
                {
                    JobTitle = jobTitle,
                    JobContext = jobContext,
                    Difficulty = session.Difficulty,
                    Prompt = _settings.Prompt,
                    Question = turn.QuestionText,
                    Answer = turn.AnswerText,
                    QuestionNumber = turn.SequenceNumber,
                    ResumeProfileJson = resumeProfileJson,
                    CurrentTurnRubricJson = turn.RubricJson,
                    PreviousQuestions = InterviewTurnNormalizationHelper
                        .GetCanonicalTurns(turns, GetMaxQuestions(session))
                        .Select(candidate => candidate.QuestionText)
                        .ToList(),
                    PreviousScores = answeredTurns
                        .Where(candidate => candidate.Id != turn.Id && candidate.Score.HasValue)
                        .Select(candidate => candidate.Score.Value)
                        .ToList(),
                    PreviousTurns = BuildPreviousTurnContext(InterviewTurnNormalizationHelper.GetCanonicalTurns(turns, GetMaxQuestions(session)), turn)
                });
                await TrackOpenAiUsageAsync(
                    session.Id,
                    turn.Id,
                    AzureUsageMetricDefaults.UsageKindOpenAiAnswerScoring,
                    "FinalScoreAnswer",
                    evaluation?.UsageInfo,
                    evaluation?.AdditionalUsageInfos,
                    JsonSerializer.Serialize(new
                    {
                        questionNumber = turn.SequenceNumber,
                        mode = "final-score"
                    }, StorageSerializerOptions));

                if (evaluation == null || !evaluation.Success || !evaluation.Score.HasValue)
                    throw new InvalidOperationException(evaluation?.ErrorMessage ?? "Final scoring missing required score.");

                turn.Score = Math.Clamp(evaluation.Score.Value, 0, 100);
                turn.Feedback = evaluation.Feedback;
                turn.RubricJson = BuildMergedRubricJson(turn.RubricJson, evaluation);
                turn.RawAIResponseJson = BuildMergedRawAiResponseJson(turn.RawAIResponseJson, evaluation.RawJson);
                await _turnService.UpdateInterviewTurnAsync(turn);
            }

            var scoredTurns = InterviewTurnNormalizationHelper.GetCompletedReportTurns(answeredTurns, GetMaxQuestions(session)).ToList();
            session.Score = scoredTurns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).DefaultIfEmpty(0).Average();
            session.QuestionScores = JsonSerializer.Serialize(scoredTurns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList());
            session.ReportData = BuildReport(scoredTurns, session.Score, reason);

            await LogRuntimeIssueAsync(
                NopLogLevel.Information,
                "AI Interview final scoring completed",
                $"Mode=final-score; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; AnsweredTurns={answeredTurns.Count}; ScoredTurns={scoredTurns.Count(turn => turn.Score.HasValue)}; ElapsedMs={finalScoringStopwatch.ElapsedMilliseconds}.",
                await ResolveLogCustomerAsync(session));

            return new CompleteInterviewResponse
            {
                Success = true,
                IsTerminated = true,
                Score = session.Score,
                Feedback = scoredTurns.LastOrDefault()?.Feedback ?? reason ?? string.Empty,
                Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Interview.CompletedScore"),
                Completion = session.ReportData,
                ReportGenerationInProgress = true,
                EstimatedWaitSeconds = 90,
                Turns = MapTurns(scoredTurns)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AI Interview final scoring failed for session {SessionId}.", session.Id);
            await LogRuntimeIssueAsync(
                NopLogLevel.Warning,
                "AI Interview final scoring failed",
                $"Mode=final-score; SessionId={session.Id}; ProductId={session.ProductId}; CustomerId={session.CustomerId}; AnsweredTurns={answeredTurns.Count}; Reason={BuildSafeValue(ex.Message)}; ElapsedMs={finalScoringStopwatch.ElapsedMilliseconds}.",
                await ResolveLogCustomerAsync(session));

            return new CompleteInterviewResponse
            {
                Success = false,
                IsTerminated = false,
                Message = "Report generation is taking longer than expected. Please try opening the report from your interview history shortly.",
                ReportGenerationInProgress = false,
                EstimatedWaitSeconds = 0,
                Turns = MapTurns(answeredTurns)
            };
        }
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
        return InterviewReportSummaryHelper.BuildReport(turns, score, reason, aiCompletion);
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
        await LogRuntimeActivityAsync(
            session,
            "AIInterview.Runtime.RecordingUploaded",
            BuildRuntimeActivityComment(session, message: $"Recording uploaded. Bytes={recording?.Length ?? 0}.", statusCode: azureStatus));
    }

    protected virtual async Task LogRecordingUploadFailureAsync(InterviewSession session, IFormFile recording, string blobName, string message, string reason, int? azureStatus = null, string azureErrorBody = null, string normalizedContentType = null)
    {
        var detail = BuildRecordingUploadLog(session, recording, blobName, "Failure", message, reason, azureStatus, azureErrorBody, normalizedContentType);
        _logger?.LogWarning("AI Interview recording upload failure. {Detail}", detail);
        await LogRuntimeIssueAsync(NopLogLevel.Warning, "AI Interview recording upload failure", detail, await ResolveLogCustomerAsync(session));
        await LogRuntimeActivityAsync(
            session,
            "AIInterview.Runtime.RecordingUploadFailed",
            BuildRuntimeActivityComment(session, message: message ?? reason ?? "Recording upload failed.", statusCode: azureStatus, failureKind: reason));
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

    protected static string ExtractPracticeSkill(string selectedProductAttributesJson, string difficultyFallback = null)
    {
        if (string.IsNullOrWhiteSpace(selectedProductAttributesJson))
            return string.Empty;

        try
        {
            var attributes = ParseSelectedPracticeAttributes(selectedProductAttributesJson);
            if (attributes == null || attributes.Count == 0)
                return string.Empty;

            var skill = attributes.FirstOrDefault(attribute =>
                MatchesAttributeKeyword([attribute.AttributeName, attribute.TextPrompt], PracticeSkillKeywords) &&
                !string.IsNullOrWhiteSpace(attribute.Value));
            if (!string.IsNullOrWhiteSpace(skill?.Value))
                return skill.Value.Trim();

            var selectedDifficulty = !string.IsNullOrWhiteSpace(difficultyFallback)
                ? difficultyFallback.Trim()
                : attributes.FirstOrDefault(attribute =>
                    MatchesAttributeKeyword([attribute.AttributeName, attribute.TextPrompt], AIInterviewDefaults.InterviewDifficultyValues) ||
                    IsPracticeDifficultyValue(attribute.Value))
                    ?.Value?.Trim();

            var fallbackSkill = attributes.FirstOrDefault(attribute =>
            {
                var value = attribute.Value?.Trim();
                return !string.IsNullOrWhiteSpace(value) &&
                    !string.Equals(value, selectedDifficulty, StringComparison.OrdinalIgnoreCase) &&
                    !IsPracticeDifficultyValue(value);
            });

            return fallbackSkill?.Value?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static List<SelectedProductAttributeValueSnapshot> ParseSelectedPracticeAttributes(string selectedProductAttributesJson)
    {
        if (string.IsNullOrWhiteSpace(selectedProductAttributesJson))
            return new List<SelectedProductAttributeValueSnapshot>();

        using var document = JsonDocument.Parse(selectedProductAttributesJson);
        if (!document.RootElement.TryGetProperty("attributes", out var attributesElement) ||
            attributesElement.ValueKind != JsonValueKind.Array)
        {
            return new List<SelectedProductAttributeValueSnapshot>();
        }

        var attributes = new List<SelectedProductAttributeValueSnapshot>();
        foreach (var attributeElement in attributesElement.EnumerateArray())
        {
            if (attributeElement.ValueKind != JsonValueKind.Object)
                continue;

            var value = TryGetJsonString(attributeElement, "value");
            if (string.IsNullOrWhiteSpace(value))
                continue;

            attributes.Add(new SelectedProductAttributeValueSnapshot(
                TryGetJsonString(attributeElement, "attributeName"),
                TryGetJsonString(attributeElement, "textPrompt"),
                value));
        }

        return attributes;
    }

    protected static bool MatchesAttributeKeyword(IEnumerable<string> attributeLabels, IEnumerable<string> keywords)
    {
        if (attributeLabels == null || keywords == null)
            return false;

        var labels = attributeLabels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .ToList();
        if (!labels.Any())
            return false;

        foreach (var label in labels)
        {
            foreach (var keyword in keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword) &&
                    label.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string TryGetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => property.GetRawText()
        };
    }

    protected virtual async Task<(IList<InterviewTurn> Turns, string FailureReason)> EnsureSingleActiveTurnAsync(InterviewSession session, IList<InterviewTurn> turns, Customer customer = null)
    {
        turns ??= new List<InterviewTurn>();
        var maxQuestions = GetMaxQuestions(session);
        var orderedTurns = turns
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList();

        var stalePendingTurns = InterviewTurnNormalizationHelper.GetStalePendingTurns(orderedTurns, maxQuestions)
            .Where(turn => turn.Id > 0)
            .ToList();
        if (stalePendingTurns.Any())
        {
            await _turnService.DeleteInterviewTurnsAsync(stalePendingTurns);
            var staleIds = stalePendingTurns.Select(turn => turn.Id).ToHashSet();
            orderedTurns = orderedTurns.Where(turn => !staleIds.Contains(turn.Id)).ToList();
        }

        if (InterviewTurnNormalizationHelper.GetAnsweredCount(orderedTurns, maxQuestions) >= maxQuestions)
            return (orderedTurns, null);

        var activePendingTurn = InterviewTurnNormalizationHelper.GetActivePendingTurn(orderedTurns, maxQuestions);
        if (activePendingTurn != null)
            return (orderedTurns, null);

        var nextSequenceNumber = InterviewTurnNormalizationHelper.GetNextSequenceNumber(orderedTurns, maxQuestions);
        var generationContext = InterviewTurnNormalizationHelper.GetCompletedReportTurns(orderedTurns, maxQuestions).ToList();
        var generatedPlan = await GenerateQuestionPlanTurnsAsync(session, customer, generationContext, new List<int> { nextSequenceNumber });
        if (!generatedPlan.Turns.Any())
            return (orderedTurns, generatedPlan.FailureReason);

        var nextTurn = generatedPlan.Turns
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .FirstOrDefault(turn => turn.SequenceNumber == nextSequenceNumber);
        if (nextTurn == null)
            return (orderedTurns, generatedPlan.FailureReason ?? "Question generation did not return the next active turn.");

        orderedTurns.Add(await _turnService.InsertInterviewTurnAsync(nextTurn));
        return (orderedTurns
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList(), null);
    }

    protected static bool IsPracticeDifficultyValue(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return PracticeDifficultyValueAliases.Any(alias =>
            string.Equals(alias, normalized, StringComparison.OrdinalIgnoreCase)) ||
            AIInterviewDefaults.InterviewDifficultyValues.Any(alias =>
                string.Equals(alias, normalized, StringComparison.OrdinalIgnoreCase));
    }

    protected static string ResolveRuntimeTopic(InterviewSession session, Product product, string practiceSkill)
    {
        if (string.Equals(NormalizeInterviewType(session), AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(practiceSkill) ? practiceSkill : "Resume Practice";

        return !string.IsNullOrWhiteSpace(product?.Name) ? product.Name : "Interview";
    }

    protected virtual async Task<string> GetResumeProfileJsonAsync(InterviewSession session, Product product = null)
    {
        if (session == null || _resumeProfileService == null)
            return string.Empty;

        if (string.Equals(NormalizeInterviewType(session), AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.OrdinalIgnoreCase))
        {
            if (session.ResumeDownloadId <= 0)
                return string.Empty;

            if (string.IsNullOrWhiteSpace(session.ResumeProfileJson))
            {
                var profileResult = await _resumeProfileService.EnsureResumeProfileAsync(session, product);
                return profileResult.Success ? profileResult.ProfileJson ?? string.Empty : string.Empty;
            }

            return session.ResumeProfileJson;
        }

        if (_applicationService == null)
            return string.Empty;

        var application = await GetLinkedApplicationAsync(session);
        if (application == null || application.ResumeDownloadId <= 0)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(application.ResumeProfileJson))
        {
            var profileResult = await _resumeProfileService.EnsureResumeProfileAsync(application, product);
            return profileResult.Success ? profileResult.ProfileJson ?? string.Empty : string.Empty;
        }

        return application.ResumeProfileJson;
    }

    protected static string NormalizeInterviewType(InterviewSession session)
    {
        if (!string.IsNullOrWhiteSpace(session?.InterviewType))
            return session.InterviewType;

        return session != null && session.ProductId > 0
            ? AIInterviewDefaults.InterviewTypeJob
            : AIInterviewDefaults.InterviewTypeMockPractice;
    }

    protected virtual async Task<JobApplication> GetLinkedApplicationAsync(InterviewSession session)
    {
        if (session == null || _applicationService == null)
            return null;

        JobApplication application = null;
        if (session.JobApplicationId > 0)
            application = await _applicationService.GetJobApplicationByIdAsync(session.JobApplicationId);

        if (application == null && session.CustomerId > 0 && session.ProductId > 0)
        {
            application = (await _applicationService.GetJobApplicationsByCustomerIdAsync(session.CustomerId) ?? new List<JobApplication>())
                .Where(candidate => candidate.ProductId == session.ProductId)
                .OrderByDescending(candidate => candidate.CreatedOnUtc)
                .ThenByDescending(candidate => candidate.Id)
                .FirstOrDefault();

            if (application != null && session.JobApplicationId == 0)
            {
                session.JobApplicationId = application.Id;
                await _sessionService.UpdateInterviewSessionAsync(session);
            }
        }

        return application;
    }

    protected static string BuildJobContext(Product product)
    {
        if (product == null)
            return string.Empty;

        var parts = new[]
        {
            StripMarkup(product.Name),
            StripMarkup(product.ShortDescription),
            StripMarkup(product.FullDescription)
        }.Where(value => !string.IsNullOrWhiteSpace(value));

        var context = string.Join(Environment.NewLine, parts);
        return context.Length <= 4000 ? context : context[..4000];
    }

    protected static string BuildInterviewContext(InterviewSession session, Product product)
    {
        var context = BuildJobContext(product);
        if (!string.Equals(NormalizeInterviewType(session), AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(session?.SelectedProductAttributesJson))
        {
            return context;
        }

        var combined = string.IsNullOrWhiteSpace(context)
            ? $"Selected practice inputs: {session.SelectedProductAttributesJson}"
            : $"{context}{Environment.NewLine}Selected practice inputs: {session.SelectedProductAttributesJson}";

        return combined.Length <= 4000 ? combined : combined[..4000];
    }

    protected static string StripMarkup(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var withoutTags = System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", " ");
        var normalized = System.Text.RegularExpressions.Regex.Replace(withoutTags, @"\s+", " ").Trim();
        return System.Net.WebUtility.HtmlDecode(normalized);
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
