using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
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

    [Test]
    public async Task InterviewAiClient_GenerateQuestion_ExtractsAzureUsageMetadata()
    {
        var responseJson = """
        {
          "id": "resp_123",
          "model": "gpt-4o-mini",
          "usage": {
            "prompt_tokens": 123,
            "completion_tokens": 45,
            "total_tokens": 168
          },
          "choices": [
            {
              "message": {
                "content": "{\"question\":\"Tell me about your background.\",\"complete\":false}"
              }
            }
          ]
        }
        """;

        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler, disposeHandler: false));

        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "key",
                AzureOpenAiDeploymentOrModel = "resume-deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            httpClientFactory.Object);

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
