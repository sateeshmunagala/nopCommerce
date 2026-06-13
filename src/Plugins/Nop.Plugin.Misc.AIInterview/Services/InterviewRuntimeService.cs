using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Nop.Core;
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
    private readonly ILogger<InterviewAiClient> _logger;

    public InterviewAiClient(AIInterviewSettings settings, MockAIInterviewSettings mockSettings, IHttpClientFactory httpClientFactory = null, ILogger<InterviewAiClient> logger = null)
    {
        _settings = settings;
        _mockSettings = mockSettings;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<AIInterviewClientResponse> GenerateQuestionAsync(AIInterviewClientRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockQuestion(request);

        var response = await CallAzureAsync(request, "generate");
        if (response == null)
            return BuildUnavailableResponse();

        if (string.IsNullOrWhiteSpace(response.Question))
            return BuildValidationFailureResponse(response, "AI service unavailable.");

        return response;
    }

    public async Task<AIInterviewClientResponse> ScoreAnswerAsync(AIInterviewClientRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockScore(request);

        var response = await CallAzureAsync(request, "score");
        if (response == null)
            return BuildUnavailableResponse();

        if (!response.Score.HasValue || response.Score.Value < 0 || response.Score.Value > 100)
            return BuildValidationFailureResponse(response, "AI service unavailable.");

        return response;
    }

    protected virtual AIInterviewClientResponse BuildUnavailableResponse()
    {
        return new AIInterviewClientResponse
        {
            Success = false,
            ErrorMessage = "AI service unavailable.",
            Feedback = "AI service unavailable.",
            Completion = "AI service unavailable.",
            RawJson = string.Empty,
            RubricJson = string.Empty,
            Score = null
        };
    }

    protected virtual async Task<AIInterviewClientResponse> CallAzureAsync(AIInterviewClientRequest request, string mode)
    {
        if (string.IsNullOrWhiteSpace(_settings?.AzureOpenAiEndpointUrl) ||
            string.IsNullOrWhiteSpace(_settings?.AzureOpenAiApiKey) ||
            string.IsNullOrWhiteSpace(_settings?.AzureOpenAiDeploymentOrModel))
        {
            _logger?.LogWarning("AI service unavailable: Azure OpenAI configuration is incomplete.");
            return null;
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
                            : "Return JSON only. Scoring mode contract: score, feedback, complete, nextQuestion when continuing, completion when ending, optional rubricJson. No markdown. No prose outside JSON."
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
                _logger?.LogWarning("Azure OpenAI call failed with status {StatusCode}. Response: {Response}", result.StatusCode, TruncateSafe(json));
                return null;
            }

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                _logger?.LogWarning("Azure OpenAI call failed. Mode: {Mode}. Reason: Empty choices.", mode);
                return null;
            }

            if (!choices[0].TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var contentProperty))
            {
                _logger?.LogWarning("Azure OpenAI call failed. Mode: {Mode}. Reason: Missing message content.", mode);
                return null;
            }

            var content = contentProperty.GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger?.LogWarning("Azure OpenAI call failed. Mode: {Mode}. Reason: Empty content string.", mode);
                return null;
            }

            var parsed = ParseStructuredResponse(content);
            if (parsed != null)
                return parsed;

            _logger?.LogWarning("Azure OpenAI call failed. Mode: {Mode}. Reason: Invalid JSON or failed contract parsing. Raw content: {Content}", mode, TruncateSafe(content));
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger?.LogWarning(ex, "Azure OpenAI call failed. Mode: {Mode}. Reason: Invalid JSON format.", mode);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Azure OpenAI call exception.");
        }

        return BuildUnavailableResponse();
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

        return $"""
Interview mode: {mode}
Job title: {request.JobTitle}
Difficulty: {request.Difficulty}
Prompt: {request.Prompt}
Question number: {request.QuestionNumber}
Previous questions: {previousQuestions}
Previous scores: {previousScores}
Current question: {request.Question}
Candidate answer: {request.Answer}
Response contract: {(mode == "generate" ? "question, complete:false, optional rubricJson" : "score, feedback, complete, nextQuestion, completion, optional rubricJson")}
""";
    }

    public static AIInterviewClientResponse ParseStructuredResponse(string content)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            content = content.Trim();
            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                var firstBrace = content.IndexOf('{');
                var lastBrace = content.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                    content = content.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
                content = content.Substring(start, end - start + 1);

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var score = TryParseNullableDecimal(root, "score");

            string question = root.TryGetProperty("question", out var q) ? q.GetString() : null;
            string nextQuestion = root.TryGetProperty("nextQuestion", out var nq) ? nq.GetString()
                : root.TryGetProperty("optionalNextQuestion", out var onq) ? onq.GetString() : null;
            string feedback = root.TryGetProperty("feedback", out var fb) ? fb.GetString() : null;
            string completion = root.TryGetProperty("completion", out var cmp) ? cmp.GetString() : null;
            string rubricJson = root.TryGetProperty("rubricJson", out var rubricJsonElement) ? rubricJsonElement.GetRawText()
                : root.TryGetProperty("rubric", out var rubricElement) ? rubricElement.GetRawText()
                : null;

            return new AIInterviewClientResponse
            {
                Success = true,
                Question = question,
                NextQuestion = nextQuestion,
                Score = score,
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

        return new AIInterviewClientResponse
        {
            Success = true,
            Question = string.Empty,
            NextQuestion = string.Empty,
            Score = score,
            Feedback = feedback,
            Complete = false,
            Completion = string.Empty,
            RawJson = JsonSerializer.Serialize(new { score, feedback, complete = false }),
            RubricJson = JsonSerializer.Serialize(new { score, feedback })
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
        _logger = logger;
    }


    protected static string TruncateSafe(string text, int length = 500)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Length <= length ? text : text.Substring(0, length) + "...";
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

        return await EnsureInterviewStartedAsync(session);
    }

    public async Task<InterviewRuntimeModel> EnsureInterviewStartedAsync(InterviewSession session, Customer customer = null)
    {
        if (session == null)
            return null;

        var turns = await _turnService.GetTurnsBySessionIdAsync(session.Id);
        if (!turns.Any())
        {
            var first = await GenerateQuestionTurnAsync(session, 1, turns);
            if (first == null)
            {
                var unavailableModel = await BuildRuntimeModelAsync(session, turns, customer);
                unavailableModel.CurrentQuestion = "AI service unavailable.";
                unavailableModel.ClientSettings ??= new RuntimeClientSettingsModel();
                unavailableModel.ClientSettings.SpeechAvailable = false;
                unavailableModel.ClientSettings.AgoraAvailable = false;
                return unavailableModel;
            }

            await _turnService.InsertInterviewTurnAsync(first);
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
                Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken")
            };
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidAnswer")
            };
        }

        var turns = (await _turnService.GetTurnsBySessionIdAsync(session.Id)).ToList();
        var currentTurn = turns.LastOrDefault(turn => string.IsNullOrWhiteSpace(turn.AnswerText)) ?? turns.LastOrDefault();
        if (currentTurn == null)
        {
            currentTurn = await GenerateQuestionTurnAsync(session, 1, turns);
            if (currentTurn == null)
            {
                return new SubmitInterviewAnswerResponse
                {
                    Success = false,
                    Message = "AI service unavailable.",
                    Feedback = "AI service unavailable."
                };
            }

            await _turnService.InsertInterviewTurnAsync(currentTurn);
            turns.Add(currentTurn);
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
            PreviousScores = turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList()
        });

        if (evaluation == null || !evaluation.Success || !evaluation.Score.HasValue || evaluation.Score.Value < 0 || evaluation.Score.Value > 100)
        {
            _logger?.LogWarning("SubmitAnswer score failure for session {SessionId}. Mode: score. Reason: {Reason}. Raw: {RawJson}",
                session.Id, evaluation?.ErrorMessage ?? "Invalid format/range", TruncateSafe(evaluation?.RawJson));
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = "AI service unavailable.",
                Feedback = "AI service unavailable."
            };
        }

        currentTurn.AnswerText = answer;
        currentTurn.Score = evaluation.Score.Value;
        currentTurn.Feedback = evaluation.Feedback;
        currentTurn.RubricJson = evaluation.RubricJson;
        currentTurn.RawAIResponseJson = evaluation.RawJson;
        currentTurn.AnsweredOnUtc = DateTime.UtcNow;
        await _turnService.UpdateInterviewTurnAsync(currentTurn);

        turns = (await _turnService.GetTurnsBySessionIdAsync(session.Id)).ToList();
        var averageScore = turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).DefaultIfEmpty(0).Average();
        session.Score = averageScore;
        session.QuestionScores = JsonSerializer.Serialize(turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList());

        var maxQuestions = GetMaxQuestions(session.Difficulty);
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
                nextTurn = await GenerateQuestionTurnAsync(session, currentTurn.SequenceNumber + 1, turns);
            }

            if (nextTurn == null)
            {
                return new SubmitInterviewAnswerResponse
                {
                    Success = false,
                    Message = "AI service unavailable.",
                    Feedback = "AI service unavailable."
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
                Feedback = evaluation.Feedback,
                Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Interview.NextQuestion"),
                Interrupted = false,
                Completion = string.Empty,
                Turn = new InterviewTurnViewModel
                {
                    TurnId = currentTurn.Id,
                    SequenceNumber = currentTurn.SequenceNumber,
                    QuestionText = currentTurn.QuestionText,
                    AnswerText = currentTurn.AnswerText,
                    Score = currentTurn.Score,
                    Feedback = currentTurn.Feedback,
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
            Feedback = completion.Feedback,
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
                Score = currentTurn.Score,
                Feedback = currentTurn.Feedback,
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
                Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken")
            };
        }

        var turns = await _turnService.GetTurnsBySessionIdAsync(session.Id);
        return await CompleteInterviewInternalAsync(session, turns, reason);
    }

    public async Task<SpeechTokenResponseModel> GetSpeechTokenAsync(string token)
    {
        var session = await _sessionService.GetSessionByTokenAsync(token);
        var now = DateTime.UtcNow;
        if (!IsSessionUsable(session, now) ||
            string.IsNullOrWhiteSpace(_settings?.AzureSpeechKey) ||
            string.IsNullOrWhiteSpace(_settings?.AzureSpeechRegion))
            return null;

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
            return null;
        }
    }

    public async Task<AgoraTokenResponseModel> GetAgoraTokenAsync(string token)
    {
        var session = await _sessionService.GetSessionByTokenAsync(token);
        var now = DateTime.UtcNow;
        if (!IsSessionUsable(session, now) ||
            string.IsNullOrWhiteSpace(_settings?.AgoraAppId) ||
            string.IsNullOrWhiteSpace(_settings?.AgoraTokenServiceUrl))
            return null;

        try
        {
            var requestUrl = _settings.AgoraTokenServiceUrl.Trim();
            var separator = requestUrl.Contains('?') ? "&" : "?";
            requestUrl = $"{requestUrl}{separator}channel={Uri.EscapeDataString(session.SessionKey)}&uid={session.CustomerId}&appId={Uri.EscapeDataString(_settings.AgoraAppId.Trim())}";

            using var httpClient = CreateHttpClient();
            var response = await httpClient.GetAsync(requestUrl);
            if (!response.IsSuccessStatusCode)
                return null;

            var payload = (await response.Content.ReadAsStringAsync())?.Trim();
            if (string.IsNullOrWhiteSpace(payload))
                return null;

            string tokenValue = null;
            string channel = session.SessionKey;
            string appId = _settings.AgoraAppId.Trim();
            uint uid = (uint)session.CustomerId;
            int expiresInSeconds = 600;

            if (payload.StartsWith("{", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                tokenValue = root.TryGetProperty("token", out var tokenElement) ? tokenElement.GetString()
                    : root.TryGetProperty("accessToken", out var accessTokenElement) ? accessTokenElement.GetString()
                    : root.TryGetProperty("access_token", out var accessTokenAltElement) ? accessTokenAltElement.GetString()
                    : null;
                channel = root.TryGetProperty("channel", out var channelElement) ? channelElement.GetString() : channel;
                appId = root.TryGetProperty("appId", out var appIdElement) ? appIdElement.GetString() : appId;
                if (root.TryGetProperty("uid", out var uidElement))
                {
                    if (uidElement.ValueKind == JsonValueKind.Number && uidElement.TryGetUInt32(out var uidValue))
                        uid = uidValue;
                    else if (uidElement.ValueKind == JsonValueKind.String && uint.TryParse(uidElement.GetString(), out var uidStringValue))
                        uid = uidStringValue;
                }
                if (root.TryGetProperty("expiresInSeconds", out var expiresElement))
                {
                    if (expiresElement.ValueKind == JsonValueKind.Number && expiresElement.TryGetInt32(out var expiresValue))
                        expiresInSeconds = expiresValue;
                    else if (expiresElement.ValueKind == JsonValueKind.String && int.TryParse(expiresElement.GetString(), out var expiresStringValue))
                        expiresInSeconds = expiresStringValue;
                }
            }
            else
            {
                tokenValue = payload;
            }

            if (string.IsNullOrWhiteSpace(tokenValue))
                return null;

            return new AgoraTokenResponseModel
            {
                AppId = appId,
                Channel = channel,
                Token = tokenValue,
                Uid = uid,
                ExpiresInSeconds = expiresInSeconds
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<RecordingUploadResponseModel> UploadRecordingAsync(string token, IFormFile recording)
    {
        var session = await _sessionService.GetSessionByTokenAsync(token);
        var now = DateTime.UtcNow;
        if (!CanUploadRecording(session, token, now))
            return RecordingFailure("Invalid or expired session token.");

        if (recording == null || recording.Length <= 0)
            return RecordingFailure("Recording file is empty.");

        const long maxRecordingBytes = 100L * 1024L * 1024L;
        if (recording.Length > maxRecordingBytes)
            return RecordingFailure("Recording file is too large.");

        if (string.IsNullOrWhiteSpace(_settings?.AzureBlobStorageContainerUrl) ||
            string.IsNullOrWhiteSpace(_settings?.AzureBlobStorageSasToken))
            return RecordingFailure("Recording storage is not configured.");

        var containerUrl = _settings.AzureBlobStorageContainerUrl.Trim().TrimEnd('/');
        var sasToken = _settings.AzureBlobStorageSasToken.Trim();
        if (!sasToken.StartsWith("?", StringComparison.Ordinal))
            sasToken = sasToken.StartsWith("&", StringComparison.Ordinal) ? "?" + sasToken[1..] : "?" + sasToken;

        var blobName = $"recordings-{session.SessionKey}-{DateTime.UtcNow:yyyyMMddHHmmss}.webm";
        var uploadUrl = $"{containerUrl}/{Uri.EscapeDataString(blobName)}{sasToken}";

        try
        {
            using var httpClient = CreateHttpClient();
            using var content = new StreamContent(recording.OpenReadStream());
            content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(recording.ContentType) ? "video/webm" : recording.ContentType);

            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
            {
                Content = content
            };
            request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");

            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return RecordingFailure("Recording upload failed.");

            session.RecordingUrl = $"{containerUrl}/{Uri.EscapeDataString(blobName)}";
            if (!session.CompletedOnUtc.HasValue)
                session.CompletedOnUtc = now;
            session.IsActive = false;
            await _sessionService.UpdateInterviewSessionAsync(session);

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
                session.Id, session.CustomerId, session.ProductId);
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
        var lastQuestion = turns.LastOrDefault()?.QuestionText ?? string.Empty;

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
            ReportUrl = session.Id > 0 ? $"/aiinterview/report/{session.Id}" : string.Empty,
            TokenExpiryUtc = session.TokenExpiryUtc,
            Turns = turns.Select(turn => new InterviewTurnViewModel
            {
                TurnId = turn.Id,
                SequenceNumber = turn.SequenceNumber,
                QuestionText = turn.QuestionText,
                AnswerText = turn.AnswerText,
                Score = turn.Score,
                Feedback = turn.Feedback,
                AskedOnUtc = turn.AskedOnUtc,
                AnsweredOnUtc = turn.AnsweredOnUtc
            }).ToList(),
            ClientSettings = new RuntimeClientSettingsModel
            {
                SpeechRegion = _settings.AzureSpeechRegion,
                SpeechVoiceName = string.Empty,
                AgoraAppId = _settings.AgoraAppId,
                ProductName = product?.Name,
                Token = session.Token,
                SpeechAvailable = !string.IsNullOrWhiteSpace(_settings.AzureSpeechKey) && !string.IsNullOrWhiteSpace(_settings.AzureSpeechRegion),
                AgoraAvailable = !string.IsNullOrWhiteSpace(_settings.AgoraAppId) && !string.IsNullOrWhiteSpace(_settings.AgoraTokenServiceUrl),
                RecordingUploadUrl = string.Empty,
                RecordingAvailable = !string.IsNullOrWhiteSpace(_settings.AzureBlobStorageContainerUrl) && !string.IsNullOrWhiteSpace(_settings.AzureBlobStorageSasToken)
            }
        };
    }

    protected virtual async Task<InterviewTurn> GenerateQuestionTurnAsync(InterviewSession session, int sequenceNumber, IList<InterviewTurn> turns)
    {
        var request = new AIInterviewClientRequest
        {
            JobTitle = await GetJobTitleAsync(session.ProductId),
            Difficulty = session.Difficulty,
            Prompt = _settings.Prompt,
            QuestionNumber = sequenceNumber,
            PreviousQuestions = turns.Select(turn => turn.QuestionText).ToList(),
            PreviousScores = turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList()
        };

        var aiResponse = await _aiClient.GenerateQuestionAsync(request);
        if (aiResponse == null || !aiResponse.Success || string.IsNullOrWhiteSpace(aiResponse.Question))
        {
            _logger?.LogWarning("GenerateQuestion failure for session {SessionId}. Mode: generate. Reason: {Reason}. Raw: {RawJson}",
                session.Id, aiResponse?.ErrorMessage ?? "Invalid format", TruncateSafe(aiResponse?.RawJson));
            return null;
        }

        return new InterviewTurn
        {
            InterviewSessionId = session.Id,
            SequenceNumber = sequenceNumber,
            QuestionId = sequenceNumber,
            QuestionText = aiResponse.Question,
            AskedOnUtc = DateTime.UtcNow,
            CreatedOnUtc = DateTime.UtcNow,
            RawAIResponseJson = aiResponse.RawJson,
            RubricJson = aiResponse.RubricJson
        };
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
            ReportUrl = session.Id > 0 ? $"/aiinterview/report/{session.Id}" : string.Empty,
            Turns = turns.Select(turn => new InterviewTurnViewModel
            {
                TurnId = turn.Id,
                SequenceNumber = turn.SequenceNumber,
                QuestionText = turn.QuestionText,
                AnswerText = turn.AnswerText,
                Score = turn.Score,
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
            $"Overall score: {score:N0}",
            $"Strengths: {(strengths.Any() ? string.Join("; ", strengths) : "Good structure and engagement.")}",
            $"Improvement areas: {(improvements.Any() ? string.Join("; ", improvements) : "Provide more concrete examples.")}",
            string.IsNullOrWhiteSpace(aiCompletion) ? string.Empty : $"AI completion: {aiCompletion}",
            string.IsNullOrWhiteSpace(reason) ? string.Empty : $"Completion note: {reason}"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    protected virtual int GetMaxQuestions(string difficulty)
    {
        return (difficulty ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "easy" => 2,
            "hard" => 4,
            _ => 3
        };
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
}
