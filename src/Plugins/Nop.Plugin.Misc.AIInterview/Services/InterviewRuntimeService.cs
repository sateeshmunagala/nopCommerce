using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
    private readonly ILogger<InterviewAiClient> _logger;

    public InterviewAiClient(AIInterviewSettings settings, MockAIInterviewSettings mockSettings, ILogger<InterviewAiClient> logger = null)
    {
        _settings = settings;
        _mockSettings = mockSettings;
        _logger = logger;
    }

    public async Task<AIInterviewClientResponse> GenerateQuestionAsync(AIInterviewClientRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockQuestion(request);

        var response = await CallAzureAsync(request, "generate");
        return response ?? BuildUnavailableResponse();
    }

    public async Task<AIInterviewClientResponse> ScoreAnswerAsync(AIInterviewClientRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockScore(request);

        var response = await CallAzureAsync(request, "score");
        return response ?? BuildUnavailableResponse();
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
            RubricJson = string.Empty
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
                    new { role = "system", content = "Return valid JSON only with fields question, score, feedback, complete, completion, rawJson, rubricJson." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.2,
                max_tokens = 400
            };

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("api-key", _settings.AzureOpenAiApiKey.Trim());
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var result = await httpClient.PostAsync(endpoint, body);
            if (!result.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Azure OpenAI call failed with status {StatusCode}.", result.StatusCode);
                return null;
            }

            var json = await result.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                return null;

            var parsed = ParseStructuredResponse(content);
            if (parsed != null)
                return parsed;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Azure OpenAI call failed.");
        }

        return BuildUnavailableResponse();
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
            decimal score = 0;
            if (root.TryGetProperty("score", out var scoreElement))
            {
                if (scoreElement.ValueKind == JsonValueKind.Number && scoreElement.TryGetDecimal(out var numericScore))
                    score = numericScore;
                else if (scoreElement.ValueKind == JsonValueKind.String && decimal.TryParse(scoreElement.GetString(), out var stringScore))
                    score = stringScore;
            }

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
                Score = 0,
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
        _workContext = workContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
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
        if (session == null || !session.IsActive || session.CompletedOnUtc.HasValue || (session.TokenExpiryUtc.HasValue && session.TokenExpiryUtc <= DateTime.UtcNow))
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

        if (evaluation == null || !evaluation.Success)
        {
            return new SubmitInterviewAnswerResponse
            {
                Success = false,
                Message = evaluation?.ErrorMessage ?? "AI service unavailable.",
                Feedback = evaluation?.ErrorMessage ?? "AI service unavailable."
            };
        }

        currentTurn.AnswerText = answer;
        currentTurn.Score = evaluation.Score;
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
        if (session == null || !session.IsActive || session.CompletedOnUtc.HasValue || (session.TokenExpiryUtc.HasValue && session.TokenExpiryUtc <= DateTime.UtcNow))
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
        if (session == null ||
            session.CompletedOnUtc.HasValue ||
            string.IsNullOrWhiteSpace(_settings?.AzureSpeechKey) ||
            string.IsNullOrWhiteSpace(_settings?.AzureSpeechRegion))
            return null;

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _settings.AzureSpeechKey.Trim());
            var endpoint = $"https://{_settings.AzureSpeechRegion.Trim()}.api.cognitive.microsoft.com/sts/v1.0/issuetoken";
            var response = await httpClient.PostAsync(endpoint, new StringContent(string.Empty));
            if (!response.IsSuccessStatusCode)
                return null;

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
        catch
        {
            return null;
        }
    }

    public async Task<AgoraTokenResponseModel> GetAgoraTokenAsync(string token)
    {
        var session = await _sessionService.GetSessionByTokenAsync(token);
        if (session == null ||
            session.CompletedOnUtc.HasValue ||
            string.IsNullOrWhiteSpace(_settings?.AgoraAppId) ||
            string.IsNullOrWhiteSpace(_settings?.AgoraTokenServiceUrl))
            return null;

        try
        {
            var requestUrl = _settings.AgoraTokenServiceUrl.Trim();
            var separator = requestUrl.Contains('?') ? "&" : "?";
            requestUrl = $"{requestUrl}{separator}channel={Uri.EscapeDataString(session.SessionKey)}&uid={session.CustomerId}&appId={Uri.EscapeDataString(_settings.AgoraAppId.Trim())}";

            using var httpClient = new HttpClient();
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
                AgoraAvailable = !string.IsNullOrWhiteSpace(_settings.AgoraAppId) && !string.IsNullOrWhiteSpace(_settings.AgoraTokenServiceUrl)
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
            return null;

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

    protected virtual async Task<string> GetJobTitleAsync(int productId)
    {
        if (productId <= 0)
            return "Practice Interview";

        var product = await _productService.GetProductByIdAsync(productId);
        return product?.Name ?? "Practice Interview";
    }
}
