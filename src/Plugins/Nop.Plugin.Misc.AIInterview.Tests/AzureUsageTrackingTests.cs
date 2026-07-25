using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Azure;
using Moq;
using Nop.Core.Caching;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Data;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class AzureUsageTrackingTests
{
    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class FakeAzureOpenAiChatCompletionAdapter : IAzureOpenAiChatCompletionAdapter
    {
        private readonly AzureOpenAiChatCompletionResult _result;

        public FakeAzureOpenAiChatCompletionAdapter(AzureOpenAiChatCompletionResult result)
        {
            _result = result;
        }

        public Task<AzureOpenAiChatCompletionResult> CompleteChatAsync(AzureOpenAiChatCompletionRequest request)
        {
            return Task.FromResult(_result);
        }
    }

    [Test]
    public async Task InterviewAiClient_GenerateQuestion_ExtractsAzureUsageMetadata()
    {
        var adapter = new FakeAzureOpenAiChatCompletionAdapter(new AzureOpenAiChatCompletionResult
        {
            Success = true,
            Content = "{\"question\":\"Tell me about your background.\",\"complete\":false}",
            Endpoint = "https://example.openai.azure.com",
            EndpointHost = "example.openai.azure.com",
            DeploymentOrModel = "resume-deployment",
            ModelName = "gpt-4o-mini",
            ResponseId = "resp_123",
            UsageInfo = new AzureOpenAiUsageInfo
            {
                DeploymentOrModel = "resume-deployment",
                ModelName = "gpt-4o-mini",
                PromptTokens = 123,
                CompletionTokens = 45,
                TotalTokens = 168,
                RawUsageJson = "{\"prompt_tokens\":123,\"completion_tokens\":45,\"total_tokens\":168}",
                MetadataJson = "{\"mode\":\"generate\",\"responseId\":\"resp_123\",\"endpoint\":\"example.openai.azure.com/\"}"
            }
        });

        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "key",
                AzureOpenAiDeploymentOrModel = "resume-deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            azureOpenAiChatCompletionAdapter: adapter);

        var response = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Platform Engineer",
            Difficulty = "Medium",
            Prompt = "Be concise",
            QuestionNumber = 1
        });

        Assert.That(response.Success, Is.True);
        Assert.That(response.Question, Is.EqualTo("Tell me about your background."));
        Assert.That(response.UsageInfo, Is.Not.Null);
        Assert.That(response.UsageInfo.PromptTokens, Is.EqualTo(123));
        Assert.That(response.UsageInfo.CompletionTokens, Is.EqualTo(45));
        Assert.That(response.UsageInfo.TotalTokens, Is.EqualTo(168));
        Assert.That(response.UsageInfo.DeploymentOrModel, Is.EqualTo("resume-deployment"));
        Assert.That(response.UsageInfo.ModelName, Is.EqualTo("gpt-4o-mini"));

        using var usageDocument = JsonDocument.Parse(response.UsageInfo.RawUsageJson);
        Assert.That(usageDocument.RootElement.GetProperty("prompt_tokens").GetInt32(), Is.EqualTo(123));
        Assert.That(usageDocument.RootElement.GetProperty("completion_tokens").GetInt32(), Is.EqualTo(45));
        Assert.That(usageDocument.RootElement.GetProperty("total_tokens").GetInt32(), Is.EqualTo(168));
    }

    [TestCase("api-key=secret-key failed", "api-key=<redacted>", "secret-key")]
    [TestCase("Authorization: BearerTokenValue failed", "Authorization=<redacted>", "BearerTokenValue")]
    [TestCase("refresh_token: refresh-secret failed", "refresh_token=<redacted>", "refresh-secret")]
    [TestCase("https://example.test/path?sig=secret-signature", "sig=<redacted>", "secret-signature")]
    [TestCase("client_secret=secret-client", "client_secret=<redacted>", "secret-client")]
    public void AzureOpenAiChatCompletionAdapter_SanitizesSecretBearingDiagnostics(string input, string expected, string secret)
    {
        var method = typeof(AzureOpenAiChatCompletionAdapter).GetMethod("SanitizeDiagnosticText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var sanitized = (string)method.Invoke(null, new object[] { input });

        Assert.That(sanitized, Does.Contain(expected));
        Assert.That(sanitized, Does.Not.Contain(secret));
    }

    [Test]
    public void AzureOpenAiChatCompletionAdapter_NormalizesResourceEndpointForSdkClient()
    {
        var method = typeof(AzureOpenAiChatCompletionAdapter).GetMethod("NormalizeResourceEndpoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var normalized = (Uri)method.Invoke(null, new object[] { "https://example.cognitiveservices.azure.com/openai/responses?api-version=2025-04-01-preview" });

        Assert.That(normalized.ToString(), Is.EqualTo("https://example.cognitiveservices.azure.com/"));
        Assert.That(normalized.Host, Is.EqualTo("example.cognitiveservices.azure.com"));
    }

    [Test]
    public void AzureOpenAiChatCompletionRequest_UsesMaxCompletionTokensOnly()
    {
        var requestType = typeof(AzureOpenAiChatCompletionRequest);

        Assert.That(requestType.GetProperty("MaxCompletionTokens"), Is.Not.Null);
        Assert.That(requestType.GetProperty("MaxTokens"), Is.Null);
        Assert.That(requestType.GetProperty("Temperature"), Is.Null);
    }

    [TestCase("https://example.openai.azure.com/")]
    [TestCase("https://example.cognitiveservices.azure.com/")]
    public void AzureOpenAiChatCompletionAdapter_AcceptsSupportedResourceEndpointFamilies(string endpoint)
    {
        var method = typeof(AzureOpenAiChatCompletionAdapter).GetMethod("ValidateConfiguration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = (AzureOpenAiChatCompletionResult)method.Invoke(null, new object[]
        {
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = endpoint,
                AzureOpenAiApiKey = "secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-5-deployment"
            }
        });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void AzureOpenAiChatCompletionAdapter_AcceptsOperationStyleEndpointAndRejectsPathLikeDeployment()
    {
        var method = typeof(AzureOpenAiChatCompletionAdapter).GetMethod("ValidateConfiguration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var operationEndpointResult = (AzureOpenAiChatCompletionResult)method.Invoke(null, new object[]
        {
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.cognitiveservices.azure.com/openai/responses?api-version=2025-04-01-preview",
                AzureOpenAiApiKey = "secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-5-deployment"
            }
        });
        var deploymentResult = (AzureOpenAiChatCompletionResult)method.Invoke(null, new object[]
        {
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com/",
                AzureOpenAiApiKey = "secret-key",
                AzureOpenAiDeploymentOrModel = "openai/deployments/deploy"
            }
        });

        Assert.That(operationEndpointResult, Is.Null);
        Assert.That(deploymentResult.Success, Is.False);
        Assert.That(deploymentResult.Reason, Does.Contain("deployment name"));
    }

    [Test]
    public void AzureOpenAiChatCompletionAdapter_RejectsUnsupportedEndpointHost()
    {
        var method = typeof(AzureOpenAiChatCompletionAdapter).GetMethod("ValidateConfiguration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = (AzureOpenAiChatCompletionResult)method.Invoke(null, new object[]
        {
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.azurewebsites.net/",
                AzureOpenAiApiKey = "secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-5-deployment"
            }
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureKind, Is.EqualTo("azure-openai-configuration-invalid"));
        Assert.That(result.Reason, Does.Contain("openai.azure.com").And.Contain("cognitiveservices.azure.com"));
    }


    [Test]
    public void AzureOpenAiChatCompletionAdapter_FormatsEndpointMetadataAsHostOnly()
    {
        var method = typeof(AzureOpenAiChatCompletionAdapter).GetMethod("BuildEndpointMetadataValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var metadataValue = (string)method.Invoke(null, new object[] { new Uri("https://example.openai.azure.com/") });
        var emptyValue = (string)method.Invoke(null, new object[] { null });

        Assert.That(metadataValue, Is.EqualTo("example.openai.azure.com/"));
        Assert.That(metadataValue, Does.Not.Contain("https://"));
        Assert.That(emptyValue, Is.EqualTo("<empty>"));
    }

    [Test]
    public void AzureOpenAiChatCompletionAdapter_MapsRequestFailedExceptionFields()
    {
        var method = typeof(AzureOpenAiChatCompletionAdapter).GetMethod("BuildRequestFailedResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var exception = new RequestFailedException(429, "Too many requests for deployment.", "rate_limit_exceeded", null);

        var result = (AzureOpenAiChatCompletionResult)method.Invoke(null, new object[]
        {
            exception,
            new Uri("https://example.openai.azure.com/"),
            "interview-deployment"
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureKind, Is.EqualTo("azure-openai-http-failure"));
        Assert.That(result.Reason, Is.EqualTo("http failure"));
        Assert.That(result.StatusCode, Is.EqualTo(429));
        Assert.That(result.ReasonPhrase, Does.Contain("Too many requests for deployment."));
        Assert.That(result.ErrorCode, Is.EqualTo("rate_limit_exceeded"));
        Assert.That(result.ErrorMessage, Does.Contain("Too many requests for deployment."));
        Assert.That(result.ResponseBody, Does.Contain("Too many requests for deployment."));
        Assert.That(result.Endpoint, Is.EqualTo("https://example.openai.azure.com/"));
        Assert.That(result.EndpointHost, Is.EqualTo("example.openai.azure.com"));
        Assert.That(result.DeploymentOrModel, Is.EqualTo("interview-deployment"));
    }

    [Test]
    public void AzureOpenAiChatCompletionAdapter_RequestFailedMappingRedactsDiagnostics()
    {
        var method = typeof(AzureOpenAiChatCompletionAdapter).GetMethod("BuildRequestFailedResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var exception = new RequestFailedException(
            401,
            "Authorization: secret-auth failed; api-key=secret-key; https://example.test/?sig=secret-signature",
            "client_secret=secret-client",
            null);

        var result = (AzureOpenAiChatCompletionResult)method.Invoke(null, new object[]
        {
            exception,
            new Uri("https://example.openai.azure.com/"),
            "deployment"
        });
        var serialized = JsonSerializer.Serialize(result);

        Assert.That(serialized, Does.Contain("Authorization=<redacted>"));
        Assert.That(serialized, Does.Contain("api-key=<redacted>"));
        Assert.That(serialized, Does.Contain("sig=<redacted>"));
        Assert.That(serialized, Does.Contain("client_secret=<redacted>"));
        Assert.That(serialized, Does.Not.Contain("secret-auth"));
        Assert.That(serialized, Does.Not.Contain("secret-key"));
        Assert.That(serialized, Does.Not.Contain("secret-signature"));
        Assert.That(serialized, Does.Not.Contain("secret-client"));
    }

    [Test]
    public async Task AzureUsageService_RecordOpenAiUsage_StoresCostAndUpdatesSessionSummary()
    {
        var sessions = new List<InterviewSession> { new() { Id = 11 } };
        var metrics = new List<AzureUsageMetric>();
        var sessionRepository = CreateRepositoryMock(sessions);
        var metricRepository = CreateRepositoryMock(metrics);
        var service = new AzureUsageService(
            metricRepository.Object,
            sessionRepository.Object,
            new AIInterviewSettings
            {
                TrackAzureOpenAiUsage = true,
                CalculateAzureCostPerInterview = true,
                AzureOpenAiPromptTokenPricePerThousand = 0.01m,
                AzureOpenAiCompletionTokenPricePerThousand = 0.02m,
                AzureUsageCurrencyCode = "USD"
            });

        await service.RecordOpenAiUsageAsync(new AzureOpenAiUsageRecordRequest
        {
            InterviewSessionId = 11,
            UsageKind = AzureUsageMetricDefaults.UsageKindOpenAiQuestionPlanning,
            OperationName = "GenerateQuestionPlan",
            UsageInfo = new AzureOpenAiUsageInfo
            {
                DeploymentOrModel = "deployment-a",
                PromptTokens = 1500,
                CompletionTokens = 500,
                TotalTokens = 2000,
                RawUsageJson = "{\"prompt_tokens\":1500,\"completion_tokens\":500,\"total_tokens\":2000}"
            }
        });

        Assert.That(metrics.Count, Is.EqualTo(1));
        Assert.That(metrics[0].EstimatedCostUsd, Is.EqualTo(0.0250m));
        Assert.That(metrics[0].CurrencyCode, Is.EqualTo("USD"));
        Assert.That(sessions[0].TotalPromptTokens, Is.EqualTo(1500));
        Assert.That(sessions[0].TotalCompletionTokens, Is.EqualTo(500));
        Assert.That(sessions[0].TotalOpenAiCostUsd, Is.EqualTo(0.0250m));
        Assert.That(sessions[0].TotalAzureCostUsd, Is.EqualTo(0.0250m));
    }

    [Test]
    public async Task AzureUsageService_RecordSpeechUsage_StoresEstimatedSpeechCostAndSummary()
    {
        var sessions = new List<InterviewSession> { new() { Id = 12 } };
        var metrics = new List<AzureUsageMetric>();
        var sessionRepository = CreateRepositoryMock(sessions);
        var metricRepository = CreateRepositoryMock(metrics);
        var service = new AzureUsageService(
            metricRepository.Object,
            sessionRepository.Object,
            new AIInterviewSettings
            {
                TrackAzureSpeechUsage = true,
                CalculateAzureCostPerInterview = true,
                AzureSpeechRecognitionPricePerHour = 2m,
                AzureSpeechSynthesisPricePerThousandCharacters = 4m,
                AzureUsageCurrencyCode = "USD"
            });

        await service.RecordSpeechUsageAsync(new AzureSpeechUsageRecordRequest
        {
            InterviewSessionId = 12,
            UsageKind = AzureUsageMetricDefaults.UsageKindSpeechRecognition,
            OperationName = "SpeechRecognition",
            SpeechRecognitionCharacters = 320,
            SpeechDurationMs = 1800000,
            ClientEventId = "rec-1"
        });

        await service.RecordSpeechUsageAsync(new AzureSpeechUsageRecordRequest
        {
            InterviewSessionId = 12,
            UsageKind = AzureUsageMetricDefaults.UsageKindSpeechSynthesis,
            OperationName = "question",
            SpeechSynthesisCharacters = 1200,
            ClientEventId = "syn-1"
        });

        Assert.That(metrics.Count, Is.EqualTo(2));
        Assert.That(metrics.Sum(metric => metric.EstimatedCostUsd), Is.EqualTo(5.8000m));
        Assert.That(sessions[0].TotalSpeechRecognitionCharacters, Is.EqualTo(320));
        Assert.That(sessions[0].TotalSpeechSynthesisCharacters, Is.EqualTo(1200));
        Assert.That(sessions[0].TotalSpeechDurationMs, Is.EqualTo(1800000));
        Assert.That(sessions[0].TotalSpeechCostUsd, Is.EqualTo(5.8000m));
        Assert.That(sessions[0].TotalAzureCostUsd, Is.EqualTo(5.8000m));
    }

    [Test]
    public async Task AzureUsageService_RecordSpeechUsage_DeduplicatesClientEventId()
    {
        var sessions = new List<InterviewSession> { new() { Id = 13 } };
        var metrics = new List<AzureUsageMetric>();
        var sessionRepository = CreateRepositoryMock(sessions);
        var metricRepository = CreateRepositoryMock(metrics);
        var service = new AzureUsageService(
            metricRepository.Object,
            sessionRepository.Object,
            new AIInterviewSettings
            {
                TrackAzureSpeechUsage = true,
                CalculateAzureCostPerInterview = true,
                AzureSpeechSynthesisPricePerThousandCharacters = 4m,
                AzureUsageCurrencyCode = "USD"
            });

        var request = new AzureSpeechUsageRecordRequest
        {
            InterviewSessionId = 13,
            UsageKind = AzureUsageMetricDefaults.UsageKindSpeechSynthesis,
            OperationName = "question",
            SpeechSynthesisCharacters = 900,
            ClientEventId = "dup-1"
        };

        await service.RecordSpeechUsageAsync(request);
        await service.RecordSpeechUsageAsync(request);

        Assert.That(metrics.Count, Is.EqualTo(1));
        Assert.That(metrics[0].ClientEventId, Is.EqualTo("dup-1"));
    }

    private static Mock<IRepository<TEntity>> CreateRepositoryMock<TEntity>(List<TEntity> store) where TEntity : Nop.Core.BaseEntity
    {
        var repository = new Mock<IRepository<TEntity>>();

        repository.SetupGet(x => x.Table)
            .Returns(() => store.AsQueryable());

        repository.Setup(x => x.GetByIdAsync(It.IsAny<int?>(), It.IsAny<Func<ICacheKeyService, CacheKey>>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((int? id, Func<ICacheKeyService, CacheKey> cacheKeyFactory, bool includeDeleted, bool useShortTermCache) =>
                store.FirstOrDefault(entity => entity.Id == id));

        repository.Setup(x => x.GetAllAsync(It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(), It.IsAny<Func<ICacheKeyService, CacheKey>>(), It.IsAny<bool>()))
            .ReturnsAsync((Func<IQueryable<TEntity>, IQueryable<TEntity>> func, Func<ICacheKeyService, CacheKey> cacheKeyFactory, bool includeDeleted) =>
                (func != null ? func(store.AsQueryable()) : store.AsQueryable()).ToList());

        repository.Setup(x => x.InsertAsync(It.IsAny<TEntity>(), It.IsAny<bool>()))
            .Returns((TEntity entity, bool publishEvent) =>
            {
                if (entity.Id == 0)
                    entity.Id = store.Count == 0 ? 1 : store.Max(item => item.Id) + 1;
                store.Add(entity);
                return Task.CompletedTask;
            });

        repository.Setup(x => x.UpdateAsync(It.IsAny<TEntity>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        return repository;
    }
}
