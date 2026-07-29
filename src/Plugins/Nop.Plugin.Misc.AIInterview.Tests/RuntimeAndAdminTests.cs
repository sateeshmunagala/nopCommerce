using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Logging;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Plugin.Misc.AIInterview.Events;
using Nop.Core.Domain.Media;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Messages;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class RuntimeAndAdminTests
{
    private const string FinalCompletionSpeechResourceKey = "Plugins.Misc.AIInterview.Runtime.FinalCompletionSpeech";
    private const string ApprovedFinalCompletionSpeech = "Thank you for completing your interview. Your responses have been submitted successfully. We are now preparing your interview report. Best wishes.";

    private Mock<IInterviewSessionService> _sessionService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<IWorkContext> _workContext;
    private Mock<ICustomerService> _customerService;
    private Mock<Nop.Core.Events.IEventPublisher> _eventPublisher;
    private Mock<ILogger> _nopLogger;
    private Mock<IDownloadService> _downloadService;
    private MockAiInterviewController _runtimeController;

    private Mock<ICreditService> _creditService;
    private Mock<ISponsorInviteService> _inviteService;
    private Mock<INotificationService> _notificationService;
    private Mock<ISettingService> _settingService;
    private AIInterviewSettings _aiInterviewSettings;
    private MockAIInterviewSettings _mockAIInterviewSettings;
    private MockAiInterviewAdminController _adminController;
    private Mock<IInterviewRuntimeService> _interviewRuntimeService;

    private Mock<IProductService> _productService;
    private SponsorInviteService _inviteServiceImplementation;

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

    private sealed class SequenceAzureOpenAiChatCompletionAdapter : IAzureOpenAiChatCompletionAdapter
    {
        private readonly Queue<AzureOpenAiChatCompletionResult> _results;

        public SequenceAzureOpenAiChatCompletionAdapter(params AzureOpenAiChatCompletionResult[] results)
        {
            _results = new Queue<AzureOpenAiChatCompletionResult>(results);
        }

        public List<AzureOpenAiChatCompletionRequest> Requests { get; } = new();

        public Task<AzureOpenAiChatCompletionResult> CompleteChatAsync(AzureOpenAiChatCompletionRequest request)
        {
            Requests.Add(request);
            if (_results.Count == 0)
                throw new InvalidOperationException("No fake Azure OpenAI result configured.");

            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class ThrowingAzureOpenAiChatCompletionAdapter : IAzureOpenAiChatCompletionAdapter
    {
        private readonly Exception _exception;

        public ThrowingAzureOpenAiChatCompletionAdapter(Exception exception)
        {
            _exception = exception;
        }

        public Task<AzureOpenAiChatCompletionResult> CompleteChatAsync(AzureOpenAiChatCompletionRequest request)
        {
            throw _exception;
        }
    }

    [SetUp]
    public void SetUp()
    {
        _sessionService = new Mock<IInterviewSessionService>();
        _localizationService = new Mock<ILocalizationService>();
        _workContext = new Mock<IWorkContext>();
        _customerService = new Mock<ICustomerService>();
        _eventPublisher = new Mock<Nop.Core.Events.IEventPublisher>();
        _nopLogger = new Mock<ILogger>();
        _downloadService = new Mock<IDownloadService>();
        _sessionService.Setup(x => x.SendRuntimeFeedbackSubmittedAdminNotificationAsync(It.IsAny<InterviewSession>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _creditService = new Mock<ICreditService>();
        _inviteService = new Mock<ISponsorInviteService>();
        _productService = new Mock<IProductService>();
        _runtimeController = new MockAiInterviewController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object, _eventPublisher.Object, null, null, null, null, null, _nopLogger.Object);
        _customerService.Setup(x => x.IsRegisteredAsync(It.Is<Customer>(customer => customer != null && !string.IsNullOrWhiteSpace(customer.Email)), true)).ReturnsAsync(true);

        _notificationService = new Mock<INotificationService>();
        _settingService = new Mock<ISettingService>();
        _aiInterviewSettings = new AIInterviewSettings();
        _mockAIInterviewSettings = new MockAIInterviewSettings();
        _interviewRuntimeService = new Mock<IInterviewRuntimeService>();
        _adminController = new MockAiInterviewAdminController(_creditService.Object, _inviteService.Object, _localizationService.Object, _notificationService.Object, _workContext.Object, _settingService.Object, _aiInterviewSettings, _mockAIInterviewSettings);

        _inviteServiceImplementation = new SponsorInviteService(null, _productService.Object, _customerService.Object, _localizationService.Object);

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key == "Plugins.Misc.AIInterview.Missing" ? "" : key);
    }

    [Test]
    public void FinalCompletionSpeech_LocaleResources_AreRegisteredForInstallAndUpgrade()
    {
        var upgradeMethod = typeof(AIInterviewPlugin).GetMethod("GetUpgradeLocaleResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(upgradeMethod, Is.Not.Null);

        var upgradeResources = (Dictionary<string, string>)upgradeMethod.Invoke(null, null);
        var pluginText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("AIInterviewPlugin.cs"));
        var installLocaleStart = pluginText.IndexOf("//locales", StringComparison.Ordinal);
        var installLocaleEnd = pluginText.IndexOf("await _localizationService.AddOrUpdateLocaleResourceAsync(GetUpgradeLocaleResources());", installLocaleStart, StringComparison.Ordinal);
        Assert.That(installLocaleStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(installLocaleEnd, Is.GreaterThan(installLocaleStart));
        var installLocaleBlock = pluginText.Substring(installLocaleStart, installLocaleEnd - installLocaleStart);

        Assert.That(AIInterviewPlugin.FinalCompletionSpeechResourceKey, Is.EqualTo(FinalCompletionSpeechResourceKey));
        Assert.That(AIInterviewPlugin.DefaultFinalCompletionSpeech, Is.EqualTo(ApprovedFinalCompletionSpeech));
        Assert.That(upgradeResources.ContainsKey(FinalCompletionSpeechResourceKey), Is.True);
        Assert.That(upgradeResources[AIInterviewPlugin.FinalCompletionSpeechResourceKey], Is.EqualTo(AIInterviewPlugin.DefaultFinalCompletionSpeech));
        Assert.That(upgradeResources[AIInterviewPlugin.FinalCompletionSpeechResourceKey], Is.EqualTo(ApprovedFinalCompletionSpeech));
        Assert.That(installLocaleBlock, Does.Contain("[FinalCompletionSpeechResourceKey] = DefaultFinalCompletionSpeech,"));
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("score").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("selected").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("rejected").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("recruiter").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("contact").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("congratulations").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("passed").IgnoreCase);
    }

    [Test]
    public void RuntimeClientSettingsModel_Exposes_FinalCompletionSpeech_AsCamelCaseJson()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new RuntimeClientSettingsModel
        {
            FinalCompletionSpeech = ApprovedFinalCompletionSpeech
        }, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        Assert.That(typeof(RuntimeClientSettingsModel).GetProperty(nameof(RuntimeClientSettingsModel.FinalCompletionSpeech)), Is.Not.Null);
        Assert.That(json, Does.Contain("\"finalCompletionSpeech\""));
        Assert.That(json, Does.Contain(ApprovedFinalCompletionSpeech));
    }

    [Test]
    public async Task RuntimeClientSettings_UnspecifiedTokenExpiry_IsTreatedAsUtcWithoutTickShift()
    {
        var databaseUtc = new DateTime(2026, 7, 28, 12, 34, 56, DateTimeKind.Unspecified);

        var settings = await ApplyRuntimeClientSettingsForExpiryAsync(databaseUtc);

        Assert.That(settings.TokenExpiryUtc, Is.Not.Null);
        Assert.That(settings.TokenExpiryUtc.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(settings.TokenExpiryUtc.Value.Ticks, Is.EqualTo(databaseUtc.Ticks));
    }

    [Test]
    public async Task RuntimeClientSettings_LocalTokenExpiry_IsConvertedToUtc()
    {
        var localExpiry = new DateTime(2026, 7, 28, 12, 34, 56, DateTimeKind.Local);
        var expectedUtc = localExpiry.ToUniversalTime();

        var settings = await ApplyRuntimeClientSettingsForExpiryAsync(localExpiry);

        Assert.That(settings.TokenExpiryUtc, Is.Not.Null);
        Assert.That(settings.TokenExpiryUtc.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(settings.TokenExpiryUtc.Value, Is.EqualTo(expectedUtc));
    }

    [Test]
    public async Task RuntimeClientSettings_UtcTokenExpiry_RemainsUnchanged()
    {
        var utcExpiry = new DateTime(2026, 7, 28, 12, 34, 56, DateTimeKind.Utc);

        var settings = await ApplyRuntimeClientSettingsForExpiryAsync(utcExpiry);

        Assert.That(settings.TokenExpiryUtc, Is.Not.Null);
        Assert.That(settings.TokenExpiryUtc.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(settings.TokenExpiryUtc.Value, Is.EqualTo(utcExpiry));
    }

    [Test]
    public async Task RuntimeClientSettings_NullTokenExpiry_RemainsNull()
    {
        var settings = await ApplyRuntimeClientSettingsForExpiryAsync(null);

        Assert.That(settings.TokenExpiryUtc, Is.Null);
    }

    [Test]
    public async Task RuntimeClientSettings_SerializesUnspecifiedDatabaseUtcExpiry_WithUtcSuffix()
    {
        var settings = await ApplyRuntimeClientSettingsForExpiryAsync(new DateTime(2026, 7, 28, 12, 34, 56, DateTimeKind.Unspecified));
        var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        Assert.That(json, Does.Contain("\"tokenExpiryUtc\":\"2026-07-28T12:34:56Z\""));
    }

    [Test]
    public async Task Runtime_Start_Unauthorized_ReturnsError()
    {
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync((Customer)null);
        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()));
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Unauthorized runtime request."));
    }

    [Test]
    public async Task Runtime_Prepare_ValidOwner_InvokesPreparation()
    {
        var customer = new Customer { Id = 7, Email = "owner@example.com" };
        var session = new InterviewSession
        {
            Id = 70,
            CustomerId = 7,
            ProductId = 12,
            Token = "owner-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(20)
        };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionByTokenAsync("owner-token")).ReturnsAsync(session);
        _interviewRuntimeService.Setup(x => x.PrepareInterviewAsync("owner-token", customer))
            .ReturnsAsync(new PrepareInterviewResponseModel
            {
                Success = true,
                Ready = true,
                Message = "Ready",
                ExpectedQuestionCount = 5,
                PersistedQuestionCount = 5
            });
        var controller = new MockAiInterviewController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object, _eventPublisher.Object, null, null, null, _interviewRuntimeService.Object, null, _nopLogger.Object);

        var result = await controller.Prepare("owner-token");

        Assert.That(result, Is.TypeOf<JsonResult>());
        Assert.That(GetJsonValue<bool>((JsonResult)result, "success"), Is.True);
        _interviewRuntimeService.Verify(x => x.PrepareInterviewAsync("owner-token", customer), Times.Once);
    }

    [TestCase(true, "other-token")]
    [TestCase(false, "guest-token")]
    [TestCase(true, "invalid-token")]
    [TestCase(true, "inactive-token")]
    [TestCase(true, "")]
    public async Task Runtime_Prepare_NonOwnerOrInvalid_DoesNotInvokePreparation(bool registeredCustomer, string token)
    {
        var customer = registeredCustomer ? new Customer { Id = 8, Email = "caller@example.com" } : null;
        var session = token switch
        {
            "other-token" => new InterviewSession { Id = 71, CustomerId = 7, Token = token, IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddMinutes(20) },
            "guest-token" => new InterviewSession { Id = 72, CustomerId = 8, Token = token, IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddMinutes(20) },
            "inactive-token" => new InterviewSession { Id = 75, CustomerId = 8, Token = token, IsActive = false, TokenExpiryUtc = DateTime.UtcNow.AddMinutes(20) },
            _ => null
        };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionByTokenAsync(token)).ReturnsAsync(session);
        var controller = new MockAiInterviewController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object, _eventPublisher.Object, null, null, null, _interviewRuntimeService.Object, null, _nopLogger.Object);

        var result = await controller.Prepare(token);

        Assert.That(result, Is.TypeOf<JsonResult>());
        Assert.That(GetJsonValue<bool>((JsonResult)result, "success"), Is.False);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        _interviewRuntimeService.Verify(x => x.PrepareInterviewAsync(It.IsAny<string>(), It.IsAny<Customer>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Prepare_UnregisteredCustomer_DoesNotInvokePreparation()
    {
        var customer = new Customer { Id = 8, Email = "caller@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _customerService.Setup(x => x.IsRegisteredAsync(customer, true)).ReturnsAsync(false);
        _sessionService.Setup(x => x.GetSessionByTokenAsync("unregistered-token")).ReturnsAsync(new InterviewSession
        {
            Id = 76,
            CustomerId = 8,
            Token = "unregistered-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        var controller = new MockAiInterviewController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object, _eventPublisher.Object, null, null, null, _interviewRuntimeService.Object, null, _nopLogger.Object);

        var result = await controller.Prepare("unregistered-token");

        Assert.That(result, Is.TypeOf<JsonResult>());
        Assert.That(GetJsonValue<bool>((JsonResult)result, "success"), Is.False);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        _interviewRuntimeService.Verify(x => x.PrepareInterviewAsync(It.IsAny<string>(), It.IsAny<Customer>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Prepare_ExpiredActiveOwnerToken_ReturnsInvalidToken_WithoutPreparation()
    {
        var customer = new Customer { Id = 7, Email = "owner@example.com" };
        var originalToken = "expired-owner-token";
        var originalExpiry = DateTime.UtcNow.AddMinutes(-1);
        var session = new InterviewSession
        {
            Id = 73,
            CustomerId = 7,
            ProductId = 12,
            Token = originalToken,
            IsActive = true,
            TokenExpiryUtc = originalExpiry
        };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _customerService.Setup(x => x.IsRegisteredAsync(customer, true)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetSessionByTokenAsync(originalToken)).ReturnsAsync(session);
        var controller = new MockAiInterviewController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object, _eventPublisher.Object, null, null, null, _interviewRuntimeService.Object, null, _nopLogger.Object);

        var result = await controller.Prepare(originalToken);

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;

        Assert.That(AIInterviewDefaults.RuntimeTokenLifetimeMinutes, Is.EqualTo(120));
        Assert.That(GetJsonValue<bool>(json, "success"), Is.False);
        Assert.That(GetJsonValue<string>(json, "message"), Is.EqualTo("Invalid or expired session token."));
        Assert.That(session.Token, Is.EqualTo(originalToken));
        Assert.That(session.TokenExpiryUtc, Is.EqualTo(originalExpiry));
        _customerService.Verify(x => x.IsRegisteredAsync(customer, true), Times.AtLeastOnce);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        _interviewRuntimeService.Verify(x => x.PrepareInterviewAsync(It.IsAny<string>(), It.IsAny<Customer>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Prepare_CompletedOwnerToken_ReturnsInvalidToken_WithoutPreparation()
    {
        var customer = new Customer { Id = 7, Email = "owner@example.com" };
        var session = new InterviewSession
        {
            Id = 74,
            CustomerId = 7,
            Token = "completed-owner-token",
            IsActive = true,
            CompletedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(20)
        };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _customerService.Setup(x => x.IsRegisteredAsync(customer)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetSessionByTokenAsync("completed-owner-token")).ReturnsAsync(session);
        var controller = new MockAiInterviewController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object, _eventPublisher.Object, null, null, null, _interviewRuntimeService.Object, null, _nopLogger.Object);

        var result = await controller.Prepare("completed-owner-token");

        Assert.That(result, Is.TypeOf<JsonResult>());
        Assert.That(GetJsonValue<bool>((JsonResult)result, "success"), Is.False);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        _interviewRuntimeService.Verify(x => x.PrepareInterviewAsync(It.IsAny<string>(), It.IsAny<Customer>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Begin_InvalidSession_ReturnsGenericErrorAndLogsSafeReason()
    {
        var customer = new Customer { Id = 7, Email = "owner@example.com" };
        var token = "begin-known-token-secret";
        var session = new InterviewSession
        {
            Id = 77,
            CustomerId = customer.Id,
            ProductId = 12,
            Token = token,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(20)
        };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionByTokenAsync(token)).ReturnsAsync(session);
        _interviewRuntimeService.Setup(x => x.BeginInterviewAsync(token, customer)).ReturnsAsync((InterviewRuntimeModel)null);
        var controller = new MockAiInterviewController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object, _eventPublisher.Object, null, null, null, _interviewRuntimeService.Object, null, _nopLogger.Object);

        var result = await controller.Begin(token);

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        Assert.That(GetJsonValue<bool>(json, "success"), Is.False);
        Assert.That(GetJsonValue<string>(json, "message"), Is.EqualTo("Invalid or expired session token."));
        _nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview begin rejected invalid session",
            It.Is<string>(message =>
                message.Contains("Endpoint=begin") &&
                message.Contains("Token=begin-...") &&
                message.Contains("ReasonCode=runtime-model-not-found") &&
                message.Contains("SessionId=77") &&
                message.Contains("CustomerId=7") &&
                message.Contains("ProductId=12") &&
                !message.Contains(token)),
            customer), Times.Once);
    }

    [Test]
    public async Task Runtime_SameTokenInterleavedCallers_PrepareSpeechAndBegin_DoNotMutateToken()
    {
        var customer = new Customer { Id = 7, Email = "owner@example.com" };
        var token = "shared-runtime-token";
        var tokenExpiryUtc = DateTime.UtcNow.AddMinutes(30);
        var session = new InterviewSession
        {
            Id = 79,
            CustomerId = customer.Id,
            ProductId = 12,
            Token = token,
            IsActive = true,
            TokenExpiryUtc = tokenExpiryUtc
        };
        var firstBeginModel = new InterviewRuntimeModel
        {
            CurrentQuestion = "First shared-token question?",
            Turns = new List<InterviewTurnViewModel>
            {
                new InterviewTurnViewModel { TurnId = 1, SequenceNumber = 1, QuestionText = "First shared-token question?" }
            }
        };
        var secondBeginModel = new InterviewRuntimeModel
        {
            CurrentQuestion = "Second shared-token question?",
            Turns = new List<InterviewTurnViewModel>
            {
                new InterviewTurnViewModel { TurnId = 1, SequenceNumber = 1, QuestionText = "Second shared-token question?" }
            }
        };
        var beginModels = new Queue<InterviewRuntimeModel>(new[] { firstBeginModel, secondBeginModel });
        var azureSpeechToken = "azure-speech-token";

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _customerService.Setup(x => x.IsRegisteredAsync(customer, true)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetSessionByTokenAsync(token)).ReturnsAsync(session);
        _interviewRuntimeService.Setup(x => x.PrepareInterviewAsync(token, customer))
            .ReturnsAsync(new PrepareInterviewResponseModel
            {
                Success = true,
                Ready = true,
                Message = "Ready",
                ExpectedQuestionCount = 5,
                PersistedQuestionCount = 5
            });
        _interviewRuntimeService.Setup(x => x.GetSpeechTokenAsync(token))
            .ReturnsAsync(new SpeechTokenResponseModel
            {
                Success = true,
                Token = azureSpeechToken,
                Region = "eastus",
                ExpiresInSeconds = 540
            });
        _interviewRuntimeService.Setup(x => x.BeginInterviewAsync(token, customer))
            .ReturnsAsync(() => beginModels.Dequeue());
        var controller = new MockAiInterviewController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object, _eventPublisher.Object, null, null, null, _interviewRuntimeService.Object, null, _nopLogger.Object);

        var callerAPrepare = (JsonResult)await controller.Prepare(token);
        var callerBSpeech = (JsonResult)await controller.SpeechToken(token);
        var callerABegin = (JsonResult)await controller.Begin(token);
        var callerBPrepare = (JsonResult)await controller.Prepare(token);
        var callerBBegin = (JsonResult)await controller.Begin(token);

        Assert.That(GetJsonValue<bool>(callerAPrepare, "success"), Is.True);
        Assert.That(GetJsonValue<bool>(callerBSpeech, "success"), Is.True);
        Assert.That(GetJsonValue<string>(callerBSpeech, "token"), Is.EqualTo(azureSpeechToken));
        Assert.That(GetJsonValue<string>(callerBSpeech, "token"), Is.Not.EqualTo(token));
        Assert.That(GetJsonValue<bool>(callerABegin, "success"), Is.True);
        Assert.That(GetJsonValue<bool>(callerBPrepare, "success"), Is.True);
        Assert.That(GetJsonValue<bool>(callerBBegin, "success"), Is.True);
        Assert.That(GetJsonValue<string>(callerABegin, "question"), Is.EqualTo(firstBeginModel.CurrentQuestion));
        Assert.That(GetJsonValue<string>(callerBBegin, "question"), Is.EqualTo(secondBeginModel.CurrentQuestion));
        Assert.That(session.Token, Is.EqualTo(token));
        Assert.That(session.TokenExpiryUtc, Is.EqualTo(tokenExpiryUtc));
        _interviewRuntimeService.Verify(x => x.PrepareInterviewAsync(token, customer), Times.Exactly(2));
        _interviewRuntimeService.Verify(x => x.GetSpeechTokenAsync(token), Times.Once);
        _interviewRuntimeService.Verify(x => x.BeginInterviewAsync(token, customer), Times.Exactly(2));
        _interviewRuntimeService.Verify(x => x.PrepareInterviewAsync(It.Is<string>(value => value != token), It.IsAny<Customer>()), Times.Never);
        _interviewRuntimeService.Verify(x => x.GetSpeechTokenAsync(It.Is<string>(value => value != token)), Times.Never);
        _interviewRuntimeService.Verify(x => x.BeginInterviewAsync(It.Is<string>(value => value != token), It.IsAny<Customer>()), Times.Never);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_ParallelValidTokenValidation_DoesNotPersistTokenOrExpiry()
    {
        var customer = new Customer { Id = 7, Email = "owner@example.com" };
        var token = "parallel-runtime-token";
        var tokenExpiryUtc = DateTime.UtcNow.AddMinutes(30);
        var session = new InterviewSession
        {
            Id = 80,
            CustomerId = customer.Id,
            ProductId = 12,
            Token = token,
            IsActive = true,
            TokenExpiryUtc = tokenExpiryUtc
        };
        var twoValidationsReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseValidations = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var validationCount = 0;

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _customerService.Setup(x => x.IsRegisteredAsync(customer, true)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetSessionByTokenAsync(token)).Returns(async () =>
        {
            if (System.Threading.Interlocked.Increment(ref validationCount) == 2)
                twoValidationsReached.SetResult();

            await releaseValidations.Task;
            return session;
        });
        _interviewRuntimeService.Setup(x => x.PrepareInterviewAsync(token, customer))
            .ReturnsAsync(new PrepareInterviewResponseModel
            {
                Success = true,
                Ready = true,
                Message = "Ready",
                ExpectedQuestionCount = 5,
                PersistedQuestionCount = 5
            });
        _interviewRuntimeService.Setup(x => x.GetSpeechTokenAsync(token))
            .ReturnsAsync(new SpeechTokenResponseModel
            {
                Success = true,
                Token = "parallel-azure-speech-token",
                Region = "eastus",
                ExpiresInSeconds = 540
            });
        var controller = new MockAiInterviewController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object, _eventPublisher.Object, null, null, null, _interviewRuntimeService.Object, null, _nopLogger.Object);

        var prepareTask = controller.Prepare(token);
        var speechTask = controller.SpeechToken(token);
        await twoValidationsReached.Task;
        releaseValidations.SetResult();
        var results = await Task.WhenAll(prepareTask, speechTask);

        Assert.That(results, Has.All.TypeOf<JsonResult>());
        Assert.That(GetJsonValue<bool>((JsonResult)results[0], "success"), Is.True);
        Assert.That(GetJsonValue<bool>((JsonResult)results[1], "success"), Is.True);
        Assert.That(GetJsonValue<string>((JsonResult)results[1], "token"), Is.EqualTo("parallel-azure-speech-token"));
        Assert.That(GetJsonValue<string>((JsonResult)results[1], "token"), Is.Not.EqualTo(token));
        Assert.That(session.Token, Is.EqualTo(token));
        Assert.That(session.TokenExpiryUtc, Is.EqualTo(tokenExpiryUtc));
        Assert.That(validationCount, Is.EqualTo(2));
        _interviewRuntimeService.Verify(x => x.PrepareInterviewAsync(token, customer), Times.Once);
        _interviewRuntimeService.Verify(x => x.GetSpeechTokenAsync(token), Times.Once);
        _interviewRuntimeService.Verify(x => x.PrepareInterviewAsync(It.Is<string>(value => value != token), It.IsAny<Customer>()), Times.Never);
        _interviewRuntimeService.Verify(x => x.GetSpeechTokenAsync(It.Is<string>(value => value != token)), Times.Never);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_RefreshToken_ExpiredActiveSession_ReturnsErrorWithoutUpdatingToken()
    {
        var session = new InterviewSession
        {
            Id = 91,
            CustomerId = 1,
            Token = "expired-active",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-active")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("expired-active");

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);

        Assert.That(success, Is.EqualTo(false));
        Assert.That(json.Value.GetType().GetProperty("newToken"), Is.Null);
        Assert.That(json.Value.GetType().GetProperty("tokenExpiryUtc"), Is.Null);
        Assert.That(session.Token, Is.EqualTo("expired-active"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_CreatesSessionWithSharedTokenLifetime()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        InterviewSession insertedSession = null;
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _sessionService.Setup(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(session => insertedSession = session)
            .Returns(Task.CompletedTask);
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), CreditLedgerSources.InterviewUsage, 1, 0)).ReturnsAsync(true);

        var beforeStartUtc = DateTime.UtcNow;
        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), 1, "Medium");
        var afterStartUtc = DateTime.UtcNow;

        Assert.That(result, Is.TypeOf<JsonResult>());
        Assert.That(insertedSession, Is.Not.Null);
        Assert.That(AIInterviewDefaults.RuntimeTokenLifetimeMinutes, Is.EqualTo(120));
        Assert.That(insertedSession.Token, Is.Not.Null.And.Not.Empty);
        Assert.That(insertedSession.TokenExpiryUtc, Is.Not.Null);
        Assert.That(insertedSession.TokenExpiryUtc.Value, Is.GreaterThanOrEqualTo(beforeStartUtc.AddMinutes(AIInterviewDefaults.RuntimeTokenLifetimeMinutes).AddSeconds(-5)));
        Assert.That(insertedSession.TokenExpiryUtc.Value, Is.LessThanOrEqualTo(afterStartUtc.AddMinutes(AIInterviewDefaults.RuntimeTokenLifetimeMinutes).AddSeconds(5)));
        Assert.That(insertedSession.StartedOnUtc, Is.EqualTo(insertedSession.CreatedOnUtc));
        Assert.That(insertedSession.TokenExpiryUtc.Value, Is.EqualTo(insertedSession.CreatedOnUtc.AddMinutes(AIInterviewDefaults.RuntimeTokenLifetimeMinutes)));
        Assert.That(insertedSession.CustomerId, Is.EqualTo(customer.Id));
        Assert.That(insertedSession.ProductId, Is.EqualTo(1));
        Assert.That(insertedSession.IsActive, Is.True);
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_InactiveSponsorInvite_FallsBack_To_CandidateCharge()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("inactive-token")).ReturnsAsync(new SponsorInvite
        {
            Id = 44,
            SponsorId = 2,
            Email = "candidate@example.com",
            InviteCode = "inactive-token",
            IsActive = false,
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1)
        });

        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), CreditLedgerSources.InterviewUsage, 1, 0)).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), 1, "Medium", "inactive-token");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session => session.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), CreditLedgerSources.InterviewUsage, 1, 0), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_ExhaustedSponsorInvite_FallsBack_To_CandidateCharge()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>
        {
            new InterviewSession { Id = 1, CustomerId = 1, ProductId = 1, SponsorInviteId = 55 },
            new InterviewSession { Id = 2, CustomerId = 1, ProductId = 1, SponsorInviteId = 55 }
        });
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("exhausted-token")).ReturnsAsync(new SponsorInvite
        {
            Id = 55,
            SponsorId = 2,
            Email = "candidate@example.com",
            InviteCode = "exhausted-token",
            IsActive = true,
            MaxAttempts = 2,
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1)
        });

        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), CreditLedgerSources.InterviewUsage, 1, 0)).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), 1, "Medium", "exhausted-token");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session => session.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), CreditLedgerSources.InterviewUsage, 1, 0), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_NoCredits_ReturnsLocalizedInlineError()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), CreditLedgerSources.InterviewUsage, 1, 0)).ReturnsAsync(false);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), 1, "Medium");

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(success, Is.False);
        Assert.That(error, Is.EqualTo("Insufficient credits. Please purchase credits to start the interview."));
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task MockPractice_History_Shows_Only_MockPractice_Sessions()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>
        {
            new()
            {
                Id = 11,
                CustomerId = customer.Id,
                ProductId = 50,
                SourceProductId = 50,
                InterviewType = AIInterviewDefaults.InterviewTypeMockPractice,
                CreatedOnUtc = DateTime.UtcNow.AddDays(-1),
                CompletedOnUtc = DateTime.UtcNow,
                ReportData = "Practice report"
            },
            new()
            {
                Id = 12,
                CustomerId = customer.Id,
                ProductId = 51,
                JobApplicationId = 9,
                InterviewType = AIInterviewDefaults.InterviewTypeJob,
                CreatedOnUtc = DateTime.UtcNow.AddDays(-2),
                CompletedOnUtc = DateTime.UtcNow,
                ReportData = "Job report"
            }
        });
        _productService.Setup(x => x.GetProductByIdAsync(50)).ReturnsAsync(new Product { Id = 50, Name = "Practice Product" });
        _productService.Setup(x => x.GetProductByIdAsync(51)).ReturnsAsync(new Product { Id = 51, Name = "Job Product" });

        var result = await _runtimeController.History();

        Assert.That(result, Is.TypeOf<ViewResult>());
        var model = ((ViewResult)result).Model as IList<InterviewHistoryItemModel>;
        Assert.That(model, Is.Not.Null);
        Assert.That(model.Count, Is.EqualTo(1));
        Assert.That(model[0].SessionId, Is.EqualTo(11));
        Assert.That(model[0].CompletedOnUtc, Is.Not.Null);
    }

    [Test]
    public async Task Report_Filters_Duplicate_And_Pending_Turns_And_Uses_Real_Report_Date()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        var turnService = new Mock<IInterviewTurnService>();
        var createdOnUtc = DateTime.UtcNow.AddDays(-1);
        var session = new InterviewSession
        {
            Id = 76,
            CustomerId = customer.Id,
            ProductId = 50,
            Token = "report-token",
            Difficulty = "Medium",
            QuestionCount = 5,
            CreatedOnUtc = createdOnUtc,
            ReportData = "Practice report",
            QuestionScores = "[80,81,82,83,84,0]"
        };

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 76)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(76)).ReturnsAsync(session);
        _productService.Setup(x => x.GetProductByIdAsync(50)).ReturnsAsync(new Product { Id = 50, Name = "Practice Product" });
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(76)).ReturnsAsync(new List<InterviewTurn>
        {
            new() { Id = 1, InterviewSessionId = 76, SequenceNumber = 1, QuestionText = "Q1", AnswerText = "A1", Score = 80, AskedOnUtc = createdOnUtc, AnsweredOnUtc = createdOnUtc.AddMinutes(1) },
            new() { Id = 2, InterviewSessionId = 76, SequenceNumber = 1, QuestionText = "Q1 duplicate", AskedOnUtc = createdOnUtc.AddMinutes(2) },
            new() { Id = 3, InterviewSessionId = 76, SequenceNumber = 2, QuestionText = "Q2", AnswerText = "A2", Score = 81, AskedOnUtc = createdOnUtc.AddMinutes(3), AnsweredOnUtc = createdOnUtc.AddMinutes(4) },
            new() { Id = 4, InterviewSessionId = 76, SequenceNumber = 3, QuestionText = "Q3", AnswerText = "A3", Score = 82, AskedOnUtc = createdOnUtc.AddMinutes(5), AnsweredOnUtc = createdOnUtc.AddMinutes(6) },
            new() { Id = 5, InterviewSessionId = 76, SequenceNumber = 4, QuestionText = "Q4", AnswerText = "A4", Score = 83, AskedOnUtc = createdOnUtc.AddMinutes(7), AnsweredOnUtc = createdOnUtc.AddMinutes(8) },
            new() { Id = 6, InterviewSessionId = 76, SequenceNumber = 5, QuestionText = "Q5", AnswerText = "A5", Score = 84, AskedOnUtc = createdOnUtc.AddMinutes(9), AnsweredOnUtc = createdOnUtc.AddMinutes(10) },
            new() { Id = 7, InterviewSessionId = 76, SequenceNumber = 6, QuestionText = "Q6 pending", AskedOnUtc = createdOnUtc.AddMinutes(11) }
        });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            turnService.Object,
            _interviewRuntimeService.Object,
            null,
            _nopLogger.Object);

        var result = await controller.Report(76);

        Assert.That(result, Is.TypeOf<ViewResult>());
        var model = ((ViewResult)result).Model as InterviewReportModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model.ReportDateUtc, Is.EqualTo(createdOnUtc));
        Assert.That(model.Turns.Count, Is.EqualTo(5));
        Assert.That(model.Turns.All(turn => !string.IsNullOrWhiteSpace(turn.AnswerText)), Is.True);
        Assert.That(model.Turns.Select(turn => turn.SequenceNumber), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(model.ParsedQuestionScores.Count, Is.EqualTo(5));
    }

    [Test]
    public async Task Runtime_InvalidToken_ReturnsLocalizedError()
    {
        var result = await _runtimeController.SubmitAnswer(null, "Answer");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Invalid or expired session token."));
    }

    [Test]
    public async Task Runtime_Feedback_ValidSolutionOption_PersistsIssueHelpfulnessAndTimestamp()
    {
        var session = new InterviewSession
        {
            Id = 101,
            Token = "feedback-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("feedback-token")).ReturnsAsync(session);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);

        var result = await _runtimeController.Feedback("feedback-token", "AI is not speaking", "helpful", null, null);
        var json = (JsonResult)result;

        Assert.That(GetJsonValue<bool>(json, "success"), Is.True);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s =>
            s.CandidateFeedbackIssue == "AI is not speaking" &&
            s.CandidateFeedbackHelpfulness == "helpful" &&
            string.IsNullOrWhiteSpace(s.CandidateFeedbackComment) &&
            s.CandidateFeedbackAttachmentDownloadId == 0 &&
            s.CandidateFeedbackSubmittedOnUtc.HasValue)), Times.Once);
        _sessionService.Verify(x => x.SendRuntimeFeedbackSubmittedAdminNotificationAsync(It.Is<InterviewSession>(s =>
            s.Id == 101 &&
            s.CandidateFeedbackIssue == "AI is not speaking"), 0), Times.Once);
    }

    [Test]
    public async Task Runtime_Feedback_InvalidIssue_IsRejected()
    {
        _sessionService.Setup(x => x.GetSessionByTokenAsync("feedback-token")).ReturnsAsync(new InterviewSession
        {
            Token = "feedback-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        });

        var result = await _runtimeController.Feedback("feedback-token", "Screen is weird", "helpful", null, null);
        var json = (JsonResult)result;

        Assert.That(GetJsonValue<bool>(json, "success"), Is.False);
        Assert.That(GetJsonValue<string>(json, "message"), Is.EqualTo("Select a valid issue."));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        _sessionService.Verify(x => x.SendRuntimeFeedbackSubmittedAdminNotificationAsync(It.IsAny<InterviewSession>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Feedback_InvalidHelpfulness_IsRejected()
    {
        _sessionService.Setup(x => x.GetSessionByTokenAsync("feedback-token")).ReturnsAsync(new InterviewSession
        {
            Token = "feedback-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        });

        var result = await _runtimeController.Feedback("feedback-token", "Loading issues", "maybe", null, null);
        var json = (JsonResult)result;

        Assert.That(GetJsonValue<bool>(json, "success"), Is.False);
        Assert.That(GetJsonValue<string>(json, "message"), Is.EqualTo("Select a valid helpfulness option."));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        _sessionService.Verify(x => x.SendRuntimeFeedbackSubmittedAdminNotificationAsync(It.IsAny<InterviewSession>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Feedback_OtherIssueWithoutComment_IsRejected()
    {
        _sessionService.Setup(x => x.GetSessionByTokenAsync("feedback-token")).ReturnsAsync(new InterviewSession
        {
            Token = "feedback-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        });

        var result = await _runtimeController.Feedback("feedback-token", "Other issue", null, "  ", null);
        var json = (JsonResult)result;

        Assert.That(GetJsonValue<bool>(json, "success"), Is.False);
        Assert.That(GetJsonValue<string>(json, "message"), Is.EqualTo("Please describe your issue before submitting."));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Feedback_OtherIssueWithComment_PersistsComment()
    {
        var session = new InterviewSession
        {
            Id = 102,
            Token = "feedback-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("feedback-token")).ReturnsAsync(session);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);

        var result = await _runtimeController.Feedback("feedback-token", "Other issue", null, "  Something failed  ", null);
        var json = (JsonResult)result;

        Assert.That(GetJsonValue<bool>(json, "success"), Is.True);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s =>
            s.CandidateFeedbackIssue == "Other issue" &&
            s.CandidateFeedbackHelpfulness == null &&
            s.CandidateFeedbackComment == "Something failed" &&
            s.CandidateFeedbackAttachmentDownloadId == 0 &&
            s.CandidateFeedbackSubmittedOnUtc.HasValue)), Times.Once);
    }

    [Test]
    public async Task Runtime_Feedback_OtherIssueWithAttachment_StoresDownloadId()
    {
        var session = new InterviewSession
        {
            Id = 103,
            Token = "feedback-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        var file = new Mock<IFormFile>();
        file.SetupGet(x => x.FileName).Returns("issue.png");
        file.SetupGet(x => x.ContentType).Returns("image/png");
        file.SetupGet(x => x.Length).Returns(12);
        _sessionService.Setup(x => x.GetSessionByTokenAsync("feedback-token")).ReturnsAsync(session);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        _downloadService.Setup(x => x.GetDownloadBitsAsync(file.Object)).ReturnsAsync(new byte[] { 1, 2, 3 });
        _downloadService.Setup(x => x.InsertDownloadAsync(It.IsAny<Download>()))
            .Callback<Download>(download => download.Id = 456)
            .Returns(Task.CompletedTask);
        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            null,
            null,
            _nopLogger.Object,
            downloadService: _downloadService.Object);

        var result = await controller.Feedback("feedback-token", "Other issue", null, "Upload details", file.Object);
        var json = (JsonResult)result;

        Assert.That(GetJsonValue<bool>(json, "success"), Is.True);
        _downloadService.Verify(x => x.InsertDownloadAsync(It.Is<Download>(download =>
            download.DownloadGuid != Guid.Empty &&
            !download.UseDownloadUrl &&
            download.ContentType == "image/png" &&
            download.Filename == "issue.png" &&
            download.Extension == ".png" &&
            download.IsNew)), Times.Once);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s =>
            s.CandidateFeedbackIssue == "Other issue" &&
            s.CandidateFeedbackComment == "Upload details" &&
            s.CandidateFeedbackAttachmentDownloadId == 456)), Times.Once);
    }

    [Test]
    public async Task Runtime_LocalizationFallback_Works()
    {
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync((Customer)null);
        // Using a trick here by mocking the controller to use a missing resource
        var controller = new TestRuntimeController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object);
        var result = await controller.TestFallback();
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var message = json.Value.GetType().GetProperty("message").GetValue(json.Value, null);
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Fallback text"));
        Assert.That(error, Is.EqualTo("Fallback text"));
    }

    [Test]
    public async Task Admin_TopUp_InvalidAmount_ReturnsError()
    {
        var result = await _adminController.TopUpCredits(1, -10);
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Invalid top-up amount."));
    }

    [Test]
    public async Task Admin_Invite_Validation_EmailRequired()
    {
        var ex = Assert.ThrowsAsync<NopException>(async () => await _inviteServiceImplementation.CreateInviteAsync(1, "", 1, 1, null));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.EmailRequired"));
    }

    [Test]
    public async Task Admin_Invite_Validation_EmailInvalid()
    {
        var ex = Assert.ThrowsAsync<NopException>(async () =>
            await _inviteServiceImplementation.CreateInviteAsync(1, "not-an-email", 1, 1, null));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.EmailInvalid"));
    }

    [Test]
    public async Task Admin_Invite_Validation_InvalidOwnership()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(1)).ReturnsAsync(new Customer { Id = 1, VendorId = 1 });
        _productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Product { Id = 10, VendorId = 2 }); // Owned by vendor 2

        var ex = Assert.ThrowsAsync<NopException>(async () => await _inviteServiceImplementation.CreateInviteAsync(1, "test@test.com", 10, 1, null));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.InvalidOwnership"));
    }

    [Test]
    public async Task Admin_Configure_SavesSettings()
    {
        _aiInterviewSettings.Enabled = true;
        _aiInterviewSettings.ApiKey = "keep";
        _aiInterviewSettings.MinimumScore = 42;
        _aiInterviewSettings.Provider = "keep";
        _aiInterviewSettings.Model = "keep";
        _aiInterviewSettings.Prompt = "keep";
        _aiInterviewSettings.ServiceSettings = "keep";

        var model = new AIInterviewConfigureModel
        {
            Enabled = false
        };

        await _adminController.Configure(model);

        _settingService.Verify(x => x.SaveSettingAsync(It.Is<AIInterviewSettings>(s =>
            s.Enabled == false &&
            s.ApiKey == "keep" &&
            s.MinimumScore == 42 &&
            s.Provider == "keep" &&
            s.Model == "keep" &&
            s.Prompt == "keep" &&
            s.ServiceSettings == "keep")), Times.Once);
    }

    [Test]
    public async Task Admin_TopUp_Successful()
    {
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Admin.TopUp.Remarks"))
            .ReturnsAsync("Admin top-up localized");

        var result = await _adminController.TopUpCredits(1, 100);
        var json = (JsonResult)result;
        var success = (bool)json.Value.GetType().GetProperty("success").GetValue(json.Value, null);

        _creditService.Verify(x => x.AddCreditAsync(1, 100, "Admin top-up localized"), Times.Once);
        Assert.That(success, Is.True);
    }

    [Test]
    public async Task Admin_Invite_Validation_ProductNotFound()
    {
        _productService.Setup(x => x.GetProductByIdAsync(It.IsAny<int>())).ReturnsAsync((Product)null);
        var ex = Assert.ThrowsAsync<NopException>(async () => await _inviteServiceImplementation.CreateInviteAsync(1, "test@test.com", 999, 1, null));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.ProductNotFound"));
    }

    [Test]
    public async Task EmployerManage_Uses_Exhausted_Status_For_Fully_Used_Invite()
    {
        var customer = new Customer { Id = 1, VendorId = 2, Email = "vendor@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _inviteService.Setup(x => x.GetSponsorInvitesAsync(1)).ReturnsAsync(new List<SponsorInvite>
        {
            new SponsorInvite
            {
                Id = 22,
                SponsorId = 1,
                ProductId = 10,
                Email = "candidate@example.com",
                InviteCode = "INV-22",
                MaxAttempts = 2,
                IsActive = true,
                IsAccepted = true,
                ExpiryDateUtc = DateTime.UtcNow.AddDays(1)
            }
        });
        _creditService.Setup(x => x.GetOrCreateWalletAsync(1)).ReturnsAsync(new CreditWallet { Balance = 5 });
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(22)).ReturnsAsync(2);

        var result = await _runtimeController.EmployerManage();

        Assert.That(result, Is.TypeOf<ViewResult>());
        var statuses = _runtimeController.ViewBag.SponsorInviteStatuses as IDictionary<int, string>;
        Assert.That(statuses, Is.Not.Null);
        Assert.That(statuses[22], Is.EqualTo("Plugins.Misc.AIInterview.Employer.Invite.Exhausted"));
    }

    [Test]
    public async Task Runtime_RefreshToken_Successful()
    {
        var session = new InterviewSession { Token = "old-token", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("old-token")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("old-token");
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var newToken = json.Value.GetType().GetProperty("newToken").GetValue(json.Value, null);
        var tokenExpiryUtc = json.Value.GetType().GetProperty("tokenExpiryUtc").GetValue(json.Value, null);

        Assert.That(success, Is.EqualTo(true));
        Assert.That(newToken, Is.EqualTo("old-token"));
        Assert.That(tokenExpiryUtc, Is.EqualTo(session.TokenExpiryUtc.Value));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_RefreshToken_InactiveSession_ReturnsError()
    {
        var session = new InterviewSession { Token = "expired", IsActive = false, TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("expired");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);

        Assert.That(error, Is.EqualTo("Invalid or expired session token."));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_RefreshToken_CompletedSession_ReturnsError()
    {
        var session = new InterviewSession { Token = "completed", IsActive = true, CompletedOnUtc = DateTime.UtcNow.AddMinutes(-5), TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("completed")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("completed");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);

        Assert.That(error, Is.EqualTo("Invalid or expired session token."));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_RefreshToken_ActiveSessionWithoutExpiry_ReturnsError()
    {
        var session = new InterviewSession { Token = "missing-expiry", IsActive = true };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("missing-expiry")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("missing-expiry");
        var json = (JsonResult)result;

        Assert.That(GetJsonValue<bool>(json, "success"), Is.False);
        Assert.That(GetJsonValue<string>(json, "message"), Is.EqualTo("Invalid or expired session token."));
        Assert.That(json.Value.GetType().GetProperty("newToken"), Is.Null);
        Assert.That(json.Value.GetType().GetProperty("tokenExpiryUtc"), Is.Null);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_RefreshToken_ExpiredActiveSession_ReturnsError()
    {
        var session = new InterviewSession
        {
            Id = 77,
            CustomerId = 12,
            ProductId = 44,
            Token = "expired-active",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-active")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("expired-active");
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);

        Assert.That(success, Is.EqualTo(false));
        Assert.That(json.Value.GetType().GetProperty("newToken"), Is.Null);
        Assert.That(json.Value.GetType().GetProperty("tokenExpiryUtc"), Is.Null);
        Assert.That(session.Token, Is.EqualTo("expired-active"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_SubmitAnswer_ExpiredActiveSession_ReturnsInvalidTokenWithoutRuntimeCall()
    {
        var session = new InterviewSession
        {
            Id = 93,
            CustomerId = 1,
            Token = "expired-submit",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-submit")).ReturnsAsync(session);

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var result = await controller.SubmitAnswer("expired-submit", "Answer");

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        Assert.That(GetJsonValue<bool>(json, "success"), Is.False);
        Assert.That(GetJsonValue<string>(json, "message"), Is.EqualTo("Invalid or expired session token."));
        Assert.That(json.Value.GetType().GetProperty("newToken"), Is.Null);
        Assert.That(json.Value.GetType().GetProperty("tokenExpiryUtc"), Is.Null);
        Assert.That(session.Token, Is.EqualTo("expired-submit"));
        _interviewRuntimeService.Verify(x => x.SubmitAnswerAsync(It.IsAny<SubmitInterviewAnswerRequest>()), Times.Never);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Stop_ExpiredActiveSession_ReturnsInvalidTokenWithoutRuntimeCall()
    {
        var session = new InterviewSession
        {
            Id = 94,
            CustomerId = 1,
            Token = "expired-stop",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-stop")).ReturnsAsync(session);

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var result = await controller.Stop("expired-stop");

        var json = (JsonResult)result;
        Assert.That(GetJsonValue<bool>(json, "success"), Is.False);
        Assert.That(GetJsonValue<string>(json, "message"), Is.EqualTo("Invalid or expired session token."));
        Assert.That(json.Value.GetType().GetProperty("newToken"), Is.Null);
        Assert.That(json.Value.GetType().GetProperty("tokenExpiryUtc"), Is.Null);
        Assert.That(session.Token, Is.EqualTo("expired-stop"));
        _interviewRuntimeService.Verify(x => x.CompleteInterviewAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Get_With_Expired_Active_Token_RedirectsToRestartWithoutTokenUpdate()
    {
        var session = new InterviewSession
        {
            Id = 88,
            CustomerId = 15,
            ProductId = 66,
            SessionKey = "session-key",
            Token = "expired-runtime",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-runtime")).ReturnsAsync(session);

        var result = await _runtimeController.Runtime("expired-runtime");

        Assert.That(result, Is.TypeOf<RedirectResult>());
        var redirect = (RedirectResult)result;
        Assert.That(redirect.Url, Is.EqualTo("/"));
        Assert.That(session.Token, Is.EqualTo("expired-runtime"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Preserves_Service_Level_MediaFlags()
    {
        var tokenExpiryUtc = DateTime.UtcNow.AddHours(1);
        var runtimeModel = new InterviewRuntimeModel
        {
            SessionId = 1,
            ProductId = 1,
            QuestionCount = 5,
            SessionKey = "session-key",
            Token = "token",
            ProductName = "Runtime Product",
            CurrentQuestion = "Q1",
            ClientSettings = new RuntimeClientSettingsModel
            {
                QuestionCount = 5,
                SpeechAvailable = false,
                RecordingAvailable = false
            }
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("token")).ReturnsAsync(new InterviewSession
        {
            Id = 1,
            CustomerId = 1,
            IsActive = true,
            Token = "token",
            TokenExpiryUtc = tokenExpiryUtc
        });
        _interviewRuntimeService.Setup(x => x.GetRuntimeModelAsync("token"))
            .ReturnsAsync(runtimeModel);
        _localizationService.Setup(x => x.GetResourceAsync(FinalCompletionSpeechResourceKey))
            .ReturnsAsync(ApprovedFinalCompletionSpeech);

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext ctx) => ctx.RouteName switch
            {
                var name when name == AIInterviewDefaults.MockReportRouteName => "/mockaiinterview/report/1",
                var name when name == AIInterviewDefaults.MockSubmitAnswerRouteName => "/mockaiinterview/submit-answer",
                var name when name == AIInterviewDefaults.MockBeginRouteName => "/mockaiinterview/begin",
                var name when name == AIInterviewDefaults.MockPrepareRouteName => "/mockaiinterview/prepare",
                var name when name == AIInterviewDefaults.MockStopRouteName => "/mockaiinterview/stop",
                var name when name == AIInterviewDefaults.MockRefreshTokenRouteName => "/mockaiinterview/refresh-token",
                var name when name == AIInterviewDefaults.MockFeedbackRouteName => "/mockaiinterview/feedback",
                var name when name == AIInterviewDefaults.MockSpeechTokenRouteName => "/mockaiinterview/speech-token",
                var name when name == AIInterviewDefaults.MockSpeechUsageRouteName => "/mockaiinterview/speech-usage",
                var name when name == AIInterviewDefaults.MockAcknowledgeGuidelinesRouteName => "/mockaiinterview/acknowledge-guidelines",
                var name when name == AIInterviewDefaults.MockRecordingUploadRouteName => "/mockaiinterview/upload-recording",
                var name when name == AIInterviewDefaults.MockRuntimeClientEventRouteName => "/mockaiinterview/runtime-client-event",
                _ => string.Empty
            });
        controller.Url = urlHelper.Object;

        var result = await controller.Runtime("token");
        var viewResult = (ViewResult)result;
        var model = (InterviewRuntimeModel)viewResult.Model;

        Assert.That(model.ClientSettings.SpeechAvailable, Is.False);
        Assert.That(model.ClientSettings.RecordingAvailable, Is.False);
        Assert.That(model.QuestionCount, Is.EqualTo(5));
        Assert.That(model.ClientSettings.QuestionCount, Is.EqualTo(5));
        Assert.That(model.ClientSettings.SubmitAnswerUrl, Is.EqualTo("/mockaiinterview/submit-answer"));
        Assert.That(model.ClientSettings.BeginInterviewUrl, Is.EqualTo("/mockaiinterview/begin"));
        Assert.That(model.ClientSettings.PrepareInterviewUrl, Is.EqualTo("/mockaiinterview/prepare"));
        Assert.That(model.ClientSettings.CompleteInterviewUrl, Is.EqualTo("/mockaiinterview/stop"));
        Assert.That(model.ClientSettings.RefreshTokenUrl, Is.EqualTo("/mockaiinterview/refresh-token"));
        Assert.That(model.ClientSettings.StopInterviewUrl, Is.EqualTo("/mockaiinterview/stop"));
        Assert.That(model.ClientSettings.FeedbackUrl, Is.EqualTo("/mockaiinterview/feedback"));
        Assert.That(model.ClientSettings.SpeechTokenUrl, Is.EqualTo("/mockaiinterview/speech-token"));
        Assert.That(model.ClientSettings.SpeechUsageUrl, Is.EqualTo("/mockaiinterview/speech-usage"));
        Assert.That(model.ClientSettings.AcknowledgeGuidelinesUrl, Is.EqualTo("/mockaiinterview/acknowledge-guidelines"));
        Assert.That(model.ClientSettings.RecordingUploadUrl, Is.EqualTo("/mockaiinterview/upload-recording"));
        Assert.That(model.ClientSettings.ProductName, Is.EqualTo("Runtime Product"));
        Assert.That(model.ClientSettings.Token, Is.EqualTo("token"));
        Assert.That(model.ClientSettings.TokenExpiryUtc, Is.EqualTo(tokenExpiryUtc));
        Assert.That(model.ClientSettings.FinalCompletionSpeech, Is.EqualTo(ApprovedFinalCompletionSpeech));
        Assert.That(model.ReportUrl, Is.Empty);
        Assert.That(model.ClientSettings.ReportUrl, Is.Empty);
        Assert.That(model.ClientSettings.RuntimeClientEventUrl, Is.EqualTo("/mockaiinterview/runtime-client-event"));
    }

    [Test]
    public async Task Runtime_Blank_FinalCompletionSpeech_Localization_UsesApprovedFallback()
    {
        _sessionService.Setup(x => x.GetSessionByTokenAsync("blank-final-speech")).ReturnsAsync(new InterviewSession
        {
            Id = 42,
            CustomerId = 1,
            ProductId = 9,
            SessionKey = "fallback-session",
            Token = "blank-final-speech",
            Difficulty = "Medium",
            QuestionCount = 5,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        _localizationService.Setup(x => x.GetResourceAsync(FinalCompletionSpeechResourceKey))
            .ReturnsAsync("   ");
        _productService.Setup(x => x.GetProductByIdAsync(9)).ReturnsAsync(new Product { Id = 9, Name = "Practice Product" });

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext ctx) => ctx.RouteName switch
            {
                var name when name == AIInterviewDefaults.MockReportRouteName => "/mockaiinterview/report/42",
                var name when name == AIInterviewDefaults.MockSpeechTokenRouteName => "/mockaiinterview/speech-token",
                var name when name == AIInterviewDefaults.MockRecordingUploadRouteName => "/mockaiinterview/upload-recording",
                _ => string.Empty
            });
        _runtimeController.Url = urlHelper.Object;

        var result = await _runtimeController.Runtime("blank-final-speech");

        Assert.That(result, Is.TypeOf<ViewResult>());
        var model = (InterviewRuntimeModel)((ViewResult)result).Model;
        Assert.That(model.ClientSettings.FinalCompletionSpeech, Is.EqualTo(ApprovedFinalCompletionSpeech));
    }

    [Test]
    public async Task Runtime_Fallback_Model_Includes_QuestionCount()
    {
        _sessionService.Setup(x => x.GetSessionByTokenAsync("fallback-question-count")).ReturnsAsync(new InterviewSession
        {
            Id = 41,
            CustomerId = 1,
            ProductId = 9,
            SessionKey = "fallback-session",
            Token = "fallback-question-count",
            Difficulty = "Medium",
            QuestionCount = 5,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        _productService.Setup(x => x.GetProductByIdAsync(9)).ReturnsAsync(new Product { Id = 9, Name = "Practice Product" });

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext ctx) => ctx.RouteName switch
            {
                var name when name == AIInterviewDefaults.MockReportRouteName => "/mockaiinterview/report/41",
                var name when name == AIInterviewDefaults.MockSubmitAnswerRouteName => "/mockaiinterview/submit-answer",
                var name when name == AIInterviewDefaults.MockBeginRouteName => "/mockaiinterview/begin",
                var name when name == AIInterviewDefaults.MockStopRouteName => "/mockaiinterview/stop",
                var name when name == AIInterviewDefaults.MockRefreshTokenRouteName => "/mockaiinterview/refresh-token",
                var name when name == AIInterviewDefaults.MockSpeechTokenRouteName => "/mockaiinterview/speech-token",
                var name when name == AIInterviewDefaults.MockRecordingUploadRouteName => "/mockaiinterview/upload-recording",
                var name when name == AIInterviewDefaults.MockRuntimeClientEventRouteName => "/mockaiinterview/runtime-client-event",
                _ => string.Empty
            });
        _runtimeController.Url = urlHelper.Object;

        var result = await _runtimeController.Runtime("fallback-question-count");

        Assert.That(result, Is.TypeOf<ViewResult>());
        var model = (InterviewRuntimeModel)((ViewResult)result).Model;
        Assert.That(model.QuestionCount, Is.EqualTo(5));
        Assert.That(model.ClientSettings.QuestionCount, Is.EqualTo(5));
    }

    [Test]
    public void RuntimeSource_StartedSessionsKeepPersistedQuestionCount()
    {
        var controllerText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Controllers", "MockAiInterviewController.cs"));
        var runtimeServiceText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Services", "InterviewRuntimeService.cs"));
        var runtimeViewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));

        Assert.That(controllerText, Does.Contain("if (!reusableSession.StartedOnUtc.HasValue && reusableSession.QuestionCount != mockQuestionCount)"));
        Assert.That(controllerText, Does.Contain("QuestionCount = isMockPracticeProduct ? ResolveMockQuestionCount() : await ResolveQuestionCountAsync(productId),"));
        Assert.That(controllerText, Does.Contain("model.QuestionCount = session != null ? NormalizeRuntimeQuestionCount(session)"));
        Assert.That(runtimeServiceText, Does.Contain("var questionCount = GetMaxQuestions(session);"));
        Assert.That(runtimeServiceText, Does.Contain("QuestionCount = questionCount,"));
        Assert.That(runtimeServiceText, Does.Contain("var totalQuestionCount = GetMaxQuestions(session);"));
        Assert.That(runtimeViewText, Does.Contain("const totalQuestions = Math.max(1, Number(config.questionCount || @Model.QuestionCount || 0));"));
        Assert.That(runtimeViewText, Does.Contain("(answered / totalQuestions) * 100"));
        Assert.That(runtimeViewText, Does.Contain("(turnStateBeforeSubmit.answeredCount + 1) >= totalQuestions"));
    }

    [Test]
    public void RuntimeServiceSource_UsesDurableScheduledCompletionRecovery()
    {
        var runtimeServiceText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Services", "InterviewRuntimeService.cs"));
        var startupText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Infrastructure", "PluginNopStartup.cs"));
        var taskText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Services", "InterviewCompletionRecoveryTask.cs"));
        var pluginText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("AIInterviewPlugin.cs"));
        var controllerText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Controllers", "MockAiInterviewController.cs"));
        var migrationText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Data", "InterviewSessionCompletionStateMigration.cs"));

        Assert.That(startupText, Does.Contain("services.AddScoped<IInterviewRuntimeService, InterviewRuntimeService>();"));
        Assert.That(startupText, Does.Contain("services.AddScoped<InterviewCompletionRecoveryTask>();"));
        Assert.That(taskText, Does.Contain("public class InterviewCompletionRecoveryTask : IScheduleTask"));
        Assert.That(taskText, Does.Contain("GetCompletionWorkSessionsAsync"));
        Assert.That(taskText, Does.Contain("ProcessCompletionWorkAsync(session.Id)"));
        Assert.That(pluginText, Does.Contain("AIInterviewDefaults.CompletionRecoveryTaskType"));
        Assert.That(pluginText, Does.Contain("InsertTaskAsync(new ScheduleTask"));
        Assert.That(pluginText, Does.Contain("DeleteTaskAsync(completionTask)"));
        Assert.That(migrationText, Does.Contain("_dataProvider.InsertEntity(new ScheduleTask"));
        Assert.That(runtimeServiceText, Does.Contain("session.CompletionState = InterviewCompletionStates.Queued;"));
        Assert.That(runtimeServiceText, Does.Contain("public async Task<CompleteInterviewResponse> ProcessCompletionWorkAsync"));
        Assert.That(runtimeServiceText, Does.Contain("_dataProvider?.CreateTransactionScope()"));
        Assert.That(runtimeServiceText, Does.Not.Contain("CompletionTasks"));
        Assert.That(runtimeServiceText, Does.Not.Contain("CompletionFailures"));
        Assert.That(runtimeServiceText, Does.Not.Contain("ObserveQueuedCompletionAsync"));
        Assert.That(controllerText, Does.Not.Contain("QueuePendingCompletionAsync"));
        Assert.That(controllerText, Does.Not.Contain("concreteRuntimeService"));
    }

    [Test]
    public async Task Runtime_SubmitAnswer_PendingCompletion_DoesNotReturnReportRoute()
    {
        var session = new InterviewSession
        {
            Id = 71,
            CustomerId = 1,
            Token = "complete-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("complete-token")).ReturnsAsync(session);
        _interviewRuntimeService.Setup(x => x.SubmitAnswerAsync(It.Is<SubmitInterviewAnswerRequest>(request => request.Token == "complete-token" && request.Answer == "Answer")))
            .ReturnsAsync(new SubmitInterviewAnswerResponse
            {
                Success = true,
                IsTerminated = true,
                ReportUrl = string.Empty,
                ReportGenerationInProgress = true,
                EstimatedWaitSeconds = 120
            });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext ctx) => ctx.RouteName == AIInterviewDefaults.MockReportRouteName ? "/mockaiinterview/report/71" : string.Empty);
        controller.Url = urlHelper.Object;

        var result = await controller.SubmitAnswer("complete-token", "Answer");
        var json = (JsonResult)result;
        var reportUrl = json.Value.GetType().GetProperty("ReportUrl")?.GetValue(json.Value, null)?.ToString();

        Assert.That(reportUrl, Is.Empty);
        Assert.That(json.Value.GetType().GetProperty("ReportGenerationInProgress")?.GetValue(json.Value, null), Is.EqualTo(true));
    }

    [Test]
    public async Task Runtime_Stop_ResponseWithoutReportReady_DoesNotReturnReportRoute()
    {
        var session = new InterviewSession
        {
            Id = 72,
            CustomerId = 1,
            Token = "stop-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("stop-token")).ReturnsAsync(session);
        _interviewRuntimeService.Setup(x => x.CompleteInterviewAsync("stop-token", "Stopped by user"))
            .ReturnsAsync(new CompleteInterviewResponse
            {
                Success = true,
                IsTerminated = true,
                ReportUrl = string.Empty,
                ReportReady = false
            });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext ctx) => ctx.RouteName == AIInterviewDefaults.MockReportRouteName ? "/mockaiinterview/report/72" : string.Empty);
        controller.Url = urlHelper.Object;

        var result = await controller.Stop("stop-token");
        var json = (JsonResult)result;
        var reportUrl = json.Value.GetType().GetProperty("ReportUrl")?.GetValue(json.Value, null)?.ToString();

        Assert.That(reportUrl, Is.Empty);
    }

    [Test]
    public async Task Runtime_CompletionStatus_ReturnsPersistedPendingReadyFailedAndRejectsWrongOwner()
    {
        var customer = new Customer { Id = 7, Email = "owner@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _customerService.Setup(x => x.IsRegisteredAsync(customer, true)).ReturnsAsync(true);

        var pending = new InterviewSession { Id = 701, CustomerId = 7, Token = "pending-token", IsActive = false, ReportData = string.Empty, CompletionState = InterviewCompletionStates.Queued };
        var ready = new InterviewSession { Id = 702, CustomerId = 7, Token = "ready-token", IsActive = false, CompletedOnUtc = DateTime.UtcNow, ReportData = "Report persisted.", CompletionState = InterviewCompletionStates.Ready };
        var failed = new InterviewSession
        {
            Id = 703,
            CustomerId = 7,
            Token = "failed-token",
            IsActive = false,
            ReportData = string.Empty,
            CompletionState = InterviewCompletionStates.Failed,
            CompletionFailureMessage = "Safe failure message."
        };
        var wrongOwner = new InterviewSession { Id = 704, CustomerId = 8, Token = "wrong-owner", IsActive = false, ReportData = string.Empty };
        _sessionService.Setup(x => x.GetSessionByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync((string token) => token switch
            {
                "pending-token" => pending,
                "ready-token" => ready,
                "failed-token" => failed,
                "wrong-owner" => wrongOwner,
                _ => null
            });

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext ctx) => ctx.RouteName == AIInterviewDefaults.MockReportRouteName ? "/mockaiinterview/report/702" : string.Empty);
        _runtimeController.Url = urlHelper.Object;
        var freshRuntimeService = new Mock<IInterviewRuntimeService>();
        var freshController = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            eventPublisher: _eventPublisher.Object,
            interviewRuntimeService: freshRuntimeService.Object,
            nopLogger: _nopLogger.Object)
        {
            Url = urlHelper.Object
        };

        var pendingJson = (JsonResult)await _runtimeController.CompletionStatus("pending-token");
        Assert.That(GetJsonValue<bool>(pendingJson, nameof(CompletionStatusResponseModel.ReportGenerationInProgress)), Is.True);
        Assert.That(GetJsonValue<bool>(pendingJson, nameof(CompletionStatusResponseModel.ReportReady)), Is.False);
        Assert.That(GetJsonValue<string>(pendingJson, nameof(CompletionStatusResponseModel.ReportUrl)), Is.Empty);

        var readyJson = (JsonResult)await freshController.CompletionStatus("ready-token");
        Assert.That(GetJsonValue<bool>(readyJson, nameof(CompletionStatusResponseModel.ReportReady)), Is.True);
        Assert.That(GetJsonValue<string>(readyJson, nameof(CompletionStatusResponseModel.ReportUrl)), Is.EqualTo("/mockaiinterview/report/702"));

        var failedJson = (JsonResult)await freshController.CompletionStatus("failed-token");
        Assert.That(GetJsonValue<bool>(failedJson, nameof(CompletionStatusResponseModel.ReportGenerationFailed)), Is.True);
        Assert.That(GetJsonValue<string>(failedJson, nameof(CompletionStatusResponseModel.Message)), Is.EqualTo("Safe failure message."));
        freshRuntimeService.VerifyNoOtherCalls();

        var wrongOwnerJson = (JsonResult)await _runtimeController.CompletionStatus("wrong-owner");
        Assert.That(GetJsonValue<bool>(wrongOwnerJson, "success"), Is.False);
    }

    [Test]
    public async Task RuntimeController_DoesNotReturnFeedbackScoreOrCompletionToRuntimeJson()
    {
        var session = new InterviewSession
        {
            Id = 81,
            CustomerId = 1,
            Token = "runtime-json-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("runtime-json-token")).ReturnsAsync(session);
        _interviewRuntimeService.Setup(x => x.SubmitAnswerAsync(It.Is<SubmitInterviewAnswerRequest>(request => request.Token == "runtime-json-token" && request.Answer == "Answer")))
            .ReturnsAsync(new SubmitInterviewAnswerResponse
            {
                Success = true,
                IsTerminated = false,
                Question = "Q2",
                Message = "Answer saved.",
                Completion = "hidden",
                Feedback = "hidden",
                Score = 88,
                Turn = new InterviewTurnViewModel
                {
                    TurnId = 10,
                    SequenceNumber = 1,
                    QuestionText = "Q1",
                    AnswerText = "Answer",
                    Score = 88,
                    Feedback = "hidden"
                }
            });
        _interviewRuntimeService.Setup(x => x.CompleteInterviewAsync("runtime-json-token", "Stopped by user"))
            .ReturnsAsync(new CompleteInterviewResponse
            {
                Success = true,
                IsTerminated = true,
                Message = "Interview completed.",
                Completion = "hidden",
                Feedback = "hidden",
                Score = 91
            });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var submitResult = (JsonResult)await controller.SubmitAnswer("runtime-json-token", "Answer");
        Assert.That(submitResult.Value.GetType().GetProperty("completion"), Is.Null);
        Assert.That(submitResult.Value.GetType().GetProperty("feedback"), Is.Null);
        Assert.That(submitResult.Value.GetType().GetProperty("score"), Is.Null);

        var stopResult = (JsonResult)await controller.Stop("runtime-json-token");
        Assert.That(stopResult.Value.GetType().GetProperty("Completion"), Is.Null);
        Assert.That(stopResult.Value.GetType().GetProperty("Feedback"), Is.Null);
        Assert.That(stopResult.Value.GetType().GetProperty("Score"), Is.Null);
        Assert.That(stopResult.Value.GetType().GetProperty("Turns"), Is.Not.Null);
    }

    [Test]
    public async Task RuntimeService_SubmitAnswer_FinalScoringFlagOn_BypassesPerAnswerScoring()
    {
        var session = new InterviewSession
        {
            Id = 301,
            Token = "submit-final-on",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10),
            QuestionCount = 2
        };
        var turns = new List<InterviewTurn>
        {
            new() { Id = 1, InterviewSessionId = session.Id, SequenceNumber = 1, QuestionText = "Describe your project architecture.", AskedOnUtc = DateTime.UtcNow },
            new() { Id = 2, InterviewSessionId = session.Id, SequenceNumber = 2, QuestionText = "How did you test it?", AskedOnUtc = DateTime.UtcNow }
        };
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        _sessionService.Setup(x => x.GetSessionByTokenAsync(session.Token)).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(turns);
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>())).Returns(Task.CompletedTask);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        var service = CreateRuntimeService(turnService, aiClient, new AIInterviewSettings { EnableFinalScoringAtCompletion = true, Prompt = "Be concise" });

        var response = await service.SubmitAnswerAsync(new SubmitInterviewAnswerRequest
        {
            Token = session.Token,
            Answer = "I designed APIs with queues, monitoring, and rollback plans.",
            TurnId = 1,
            SequenceNumber = 1
        });

        Assert.That(response.Success, Is.True);
        Assert.That(response.IsTerminated, Is.False);
        Assert.That(response.Question, Is.EqualTo("How did you test it?"));
        Assert.That(turns[0].AnswerText, Is.Not.Empty);
        Assert.That(turns[0].Score, Is.Null);
        aiClient.Verify(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Never);
    }

    [Test]
    public async Task RuntimeService_SubmitAnswer_FinalScoringFlagOff_UsesLegacyPerAnswerScoring()
    {
        var session = new InterviewSession
        {
            Id = 302,
            Token = "submit-final-off",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10),
            QuestionCount = 2
        };
        var turns = new List<InterviewTurn>
        {
            new() { Id = 1, InterviewSessionId = session.Id, SequenceNumber = 1, QuestionText = "Describe a production issue you solved.", AskedOnUtc = DateTime.UtcNow },
            new() { Id = 2, InterviewSessionId = session.Id, SequenceNumber = 2, QuestionText = "How did you prevent regressions?", AskedOnUtc = DateTime.UtcNow }
        };
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        _sessionService.Setup(x => x.GetSessionByTokenAsync(session.Token)).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(turns);
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>())).Returns(Task.CompletedTask);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse
            {
                Success = true,
                Score = 82,
                TechnicalScore = 80,
                CommunicationScore = 82,
                ProfessionalismScore = 84,
                PositiveAttitudeScore = 82,
                Feedback = "Good answer.",
                RubricJson = "{\"score\":82}",
                RawJson = "{\"score\":82}"
            });
        var service = CreateRuntimeService(turnService, aiClient, new AIInterviewSettings { EnableFinalScoringAtCompletion = false, Prompt = "Be concise" });

        var response = await service.SubmitAnswerAsync(new SubmitInterviewAnswerRequest
        {
            Token = session.Token,
            Answer = "I isolated the regression, shipped a fix, and added automated coverage.",
            TurnId = 1,
            SequenceNumber = 1
        });

        Assert.That(response.Success, Is.True);
        Assert.That(response.IsTerminated, Is.False);
        Assert.That(turns[0].Score, Is.EqualTo(82));
        aiClient.Verify(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()), Times.Once);
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Never);
    }

    [Test]
    public async Task RuntimeService_ScheduledCompletion_FinalBatchScoring_UsesAnsweredTurnsOnly()
    {
        var session = new InterviewSession
        {
            Id = 303,
            Token = "complete-partial",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10),
            QuestionCount = 3
        };
        var turns = new List<InterviewTurn>
        {
            new() { Id = 1, InterviewSessionId = session.Id, SequenceNumber = 1, QuestionText = "Q1", AnswerText = "A1 with concrete project details.", AskedOnUtc = DateTime.UtcNow, AnsweredOnUtc = DateTime.UtcNow },
            new() { Id = 2, InterviewSessionId = session.Id, SequenceNumber = 2, QuestionText = "Q2", AnswerText = "A2 with testing and monitoring details.", AskedOnUtc = DateTime.UtcNow, AnsweredOnUtc = DateTime.UtcNow },
            new() { Id = 3, InterviewSessionId = session.Id, SequenceNumber = 3, QuestionText = "Q3 pending", AskedOnUtc = DateTime.UtcNow }
        };
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        _sessionService.Setup(x => x.GetSessionByTokenAsync(session.Token)).ReturnsAsync(session);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(turns);
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>())).Returns(Task.CompletedTask);
        aiClient.Setup(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()))
            .ReturnsAsync(new AIInterviewFinalScoringResponse
            {
                Success = true,
                Completion = "Final summary.",
                Turns = new List<AIInterviewFinalScoringTurnResult>
                {
                    BuildFinalScore(1, 80),
                    BuildFinalScore(2, 90)
                }
            });
        var strengthsText = "The candidate showed clear ownership of API design and delivery by explaining queue-based architecture, monitoring, rollback planning, testing, and regression prevention. The answers connected implementation details to reliability, team execution, and production outcomes.";
        aiClient.Setup(x => x.GenerateStrengthsSummaryAsync(It.IsAny<AIInterviewStrengthsSummaryRequest>()))
            .ReturnsAsync(new AIInterviewStrengthsSummaryResponse
            {
                Success = true,
                StrengthsText = strengthsText,
                EvidenceTurnNumbers = new List<int> { 1, 2 }
            });
        var service = CreateRuntimeService(turnService, aiClient, new AIInterviewSettings { EnableFinalScoringAtCompletion = true, Prompt = "Be concise" });

        var accepted = await service.CompleteInterviewAsync(session.Token, "Stopped by user");

        Assert.That(accepted.Success, Is.True);
        Assert.That(accepted.ReportGenerationInProgress, Is.True);
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Queued));
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Never);
        var response = await service.ProcessCompletionWorkAsync(session.Id);

        Assert.That(response.Success, Is.True);
        Assert.That(response.IsTerminated, Is.True);
        Assert.That(response.ReportGenerationInProgress, Is.False);
        Assert.That(response.Turns.Count, Is.EqualTo(2));
        Assert.That(session.IsActive, Is.False);
        Assert.That(session.Score, Is.EqualTo(85));
        Assert.That(session.ReportData, Does.Contain($"Strengths: {strengthsText}"));
        Assert.That(session.ReportData, Does.Not.Contain("No scored strengths were identified from the submitted answers."));
        Assert.That(session.ReportData, Does.Contain("Stopped by user"));
        var scores = System.Text.Json.JsonSerializer.Deserialize<List<decimal>>(session.QuestionScores);
        Assert.That(scores, Is.EqualTo(new List<decimal> { 80, 90 }));
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.Is<AIInterviewFinalScoringRequest>(request =>
            request.Turns.Count == 2 &&
            request.Turns.All(turn => turn.SequenceNumber == 1 || turn.SequenceNumber == 2))), Times.Once);
        aiClient.Verify(x => x.GenerateStrengthsSummaryAsync(It.Is<AIInterviewStrengthsSummaryRequest>(request =>
            request.Turns.Count == 2 &&
            request.Turns.All(turn => turn.Score.HasValue && !string.IsNullOrWhiteSpace(turn.Feedback)))), Times.Once);
        aiClient.Verify(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
    }

    [Test]
    public async Task RuntimeService_CompleteInterview_InvalidStrengthsSummary_FallsBackWithoutFailingCompletion()
    {
        var session = new InterviewSession
        {
            Id = 305,
            Token = "complete-strengths-fallback",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10),
            QuestionCount = 1
        };
        var turns = new List<InterviewTurn>
        {
            new() { Id = 1, InterviewSessionId = session.Id, SequenceNumber = 1, QuestionText = "Q1", AnswerText = "I designed Azure APIs with queues, monitoring, testing, and rollback plans for production reliability.", AskedOnUtc = DateTime.UtcNow, AnsweredOnUtc = DateTime.UtcNow }
        };
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        _sessionService.Setup(x => x.GetSessionByTokenAsync(session.Token)).ReturnsAsync(session);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(turns);
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>())).Returns(Task.CompletedTask);
        aiClient.Setup(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()))
            .ReturnsAsync(new AIInterviewFinalScoringResponse
            {
                Success = true,
                Completion = "Final summary.",
                Turns = new List<AIInterviewFinalScoringTurnResult>
                {
                    BuildFinalScore(1, 88) with { Feedback = "Strong answer with clear structure." }
                }
            });
        aiClient.Setup(x => x.GenerateStrengthsSummaryAsync(It.IsAny<AIInterviewStrengthsSummaryRequest>()))
            .ReturnsAsync(new AIInterviewStrengthsSummaryResponse
            {
                Success = true,
                StrengthsText = "Too short."
            });
        var service = CreateRuntimeService(turnService, aiClient, new AIInterviewSettings { EnableFinalScoringAtCompletion = true, Prompt = "Be concise" });

        var accepted = await service.CompleteInterviewAsync(session.Token, "Stopped by user");
        Assert.That(accepted.ReportGenerationInProgress, Is.True);
        var response = await service.ProcessCompletionWorkAsync(session.Id);

        Assert.That(response.Success, Is.True);
        Assert.That(session.ReportData, Does.Contain("Strengths: Demonstrated clear structure and communication."));
        Assert.That(session.ReportData, Does.Not.Contain("Too short."));
        aiClient.Verify(x => x.GenerateStrengthsSummaryAsync(It.IsAny<AIInterviewStrengthsSummaryRequest>()), Times.Once);
    }

    [Test]
    public async Task RuntimeService_CompleteInterview_FinalBatchScoringRejectsIncompleteTurnScores()
    {
        var session = new InterviewSession
        {
            Id = 304,
            Token = "complete-incomplete",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10),
            QuestionCount = 2
        };
        var turns = new List<InterviewTurn>
        {
            new() { Id = 1, InterviewSessionId = session.Id, SequenceNumber = 1, QuestionText = "Q1", AnswerText = "A1 with real detail.", AskedOnUtc = DateTime.UtcNow, AnsweredOnUtc = DateTime.UtcNow },
            new() { Id = 2, InterviewSessionId = session.Id, SequenceNumber = 2, QuestionText = "Q2", AnswerText = "A2 with real detail.", AskedOnUtc = DateTime.UtcNow, AnsweredOnUtc = DateTime.UtcNow }
        };
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        _sessionService.Setup(x => x.GetSessionByTokenAsync(session.Token)).ReturnsAsync(session);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(turns);
        aiClient.Setup(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()))
            .ReturnsAsync(new AIInterviewFinalScoringResponse
            {
                Success = true,
                Turns = new List<AIInterviewFinalScoringTurnResult>
                {
                    BuildFinalScore(1, 80)
                }
            });
        var service = CreateRuntimeService(turnService, aiClient, new AIInterviewSettings { EnableFinalScoringAtCompletion = true, Prompt = "Be concise" });

        var accepted = await service.CompleteInterviewAsync(session.Token, "Stopped by user");
        Assert.That(accepted.Success, Is.True);
        Assert.That(accepted.ReportGenerationInProgress, Is.True);
        var response = await service.ProcessCompletionWorkAsync(session.Id);

        Assert.That(response.Success, Is.False);
        Assert.That(response.IsTerminated, Is.True);
        Assert.That(response.ReportGenerationFailed, Is.True);
        Assert.That(session.IsActive, Is.False);
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Failed));
        Assert.That(session.CompletionFailureMessage, Is.Not.Empty);
        Assert.That(turns.All(turn => turn.Score == null), Is.True);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.AtLeastOnce);
    }

    [Test]
    public void RuntimeView_UsesApprovedReportGenerationCopyAndClearsWaitingTimerOnFailure()
    {
        var runtimeViewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));

        Assert.That(runtimeViewText, Does.Contain("const reportGenerationMessage = 'Generating your report...';"));
        Assert.That(runtimeViewText, Does.Contain("startReportGenerationWaitingState(getValue(result, 'estimatedWaitSeconds', 'EstimatedWaitSeconds') || completionWaitSeconds);"));
        Assert.That(runtimeViewText, Does.Contain("setHeaderStatus(`${reportGenerationMessage} Time remaining: ${formatCountdown(remainingSeconds)}`, false);"));
        Assert.That(runtimeViewText, Does.Contain("clearCompletedRedirectState();"));
        Assert.That(runtimeViewText, Does.Contain("Unable to stop interview."));
        Assert.That(runtimeViewText, Does.Contain("Unable to submit answer."));
    }

    [Test]
    public void RuntimeView_Contains_Recording_And_Upload_Hooks()
    {
        var runtimeViewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));
        var beginInterviewStart = runtimeViewText.IndexOf("const beginInterview = async () =>", StringComparison.Ordinal);
        var beginInterviewScreenShareIndex = runtimeViewText.IndexOf("if (!(await requestScreenShareForInterviewStart()))", beginInterviewStart, StringComparison.Ordinal);
        var beginInterviewFixedExpiryGateIndex = runtimeViewText.IndexOf("if (!(await ensureRuntimeTokenFresh()))", beginInterviewStart, StringComparison.Ordinal);
        var beginInterviewEndpointGateIndex = runtimeViewText.IndexOf("if (!config.beginInterviewUrl)", beginInterviewFixedExpiryGateIndex, StringComparison.Ordinal);
        var beginInterviewPostIndex = runtimeViewText.IndexOf("postForm(config.beginInterviewUrl", beginInterviewEndpointGateIndex, StringComparison.Ordinal);
        var beginInterviewFixedExpiryGateBlock = beginInterviewEndpointGateIndex > beginInterviewFixedExpiryGateIndex && beginInterviewFixedExpiryGateIndex >= 0
            ? runtimeViewText.Substring(beginInterviewFixedExpiryGateIndex, beginInterviewEndpointGateIndex - beginInterviewFixedExpiryGateIndex)
            : string.Empty;
        var onScreenShareInterruptedStart = runtimeViewText.IndexOf("const onScreenShareInterrupted = async () =>", StringComparison.Ordinal);
        var onScreenShareInterruptedEnd = runtimeViewText.IndexOf("const updateGuidelinesAcknowledgementState = () =>", onScreenShareInterruptedStart, StringComparison.Ordinal);
        var onScreenShareInterruptedBlock = runtimeViewText.Substring(onScreenShareInterruptedStart, onScreenShareInterruptedEnd - onScreenShareInterruptedStart);

        Assert.That(runtimeViewText, Does.Contain("recordingUploadUrl"));
        Assert.That(runtimeViewText, Does.Contain("MediaRecorder"));
        Assert.That(runtimeViewText, Does.Contain("toggle-recording"));
        Assert.That(runtimeViewText, Does.Contain("uploadRecording"));
        Assert.That(runtimeViewText, Does.Contain("getUserMedia"));
        Assert.That(runtimeViewText, Does.Contain("SpeechSDK"));
        Assert.That(runtimeViewText, Does.Contain("speechTokenUrl"));
        Assert.That(runtimeViewText, Does.Contain("beginInterviewUrl"));
        Assert.That(runtimeViewText, Does.Contain("runtimeClientEventUrl"));
        Assert.That(runtimeViewText, Does.Contain("const reportRuntimeClientRequestFailure = (event) =>"));
        Assert.That(runtimeViewText, Does.Contain("event.requestName === 'runtime-client-event'"));
        Assert.That(runtimeViewText, Does.Contain("failureKind: 'http-status'"));
        Assert.That(runtimeViewText, Does.Contain("failureKind: 'invalid-json'"));
        Assert.That(runtimeViewText, Does.Contain("failureKind: 'non-json-response'"));
        Assert.That(runtimeViewText, Does.Contain("failureKind: 'fetch-exception'"));
        Assert.That(runtimeViewText, Does.Contain("submitAnswer"));
        Assert.That(runtimeViewText, Does.Contain("stopInterview"));
        Assert.That(runtimeViewText, Does.Contain("runtime-question-count"));
        Assert.That(runtimeViewText, Does.Contain("config.questionCount"));
        Assert.That(runtimeViewText, Does.Contain("(answered / totalQuestions) * 100"));
        Assert.That(runtimeViewText, Does.Contain("id=\"runtime-tab-conversation\""));
        Assert.That(runtimeViewText, Does.Contain("id=\"runtime-tab-details\""));
        Assert.That(runtimeViewText, Does.Contain("data-runtime-panel=\"conversation\""));
        Assert.That(runtimeViewText, Does.Contain("data-runtime-panel=\"details\""));
        Assert.That(runtimeViewText, Does.Contain("id=\"runtime-video-caption\""));
        Assert.That(runtimeViewText, Does.Contain("id=\"runtime-video-caption-speaker\""));
        Assert.That(runtimeViewText, Does.Contain("id=\"runtime-video-caption-text\""));
        Assert.That(runtimeViewText, Does.Contain("<textarea id=\"runtime-answer\""));
        Assert.That(runtimeViewText, Does.Contain("id=\"submit-answer\" class=\"button-1 runtime-composer-send runtime-js-hidden\" disabled"));
        Assert.That(runtimeViewText, Does.Contain("<div class=\"runtime-answer\">"));
        Assert.That(runtimeViewText, Does.Contain("const updateAnswerInputState = () =>"));
        Assert.That(runtimeViewText, Does.Not.Contain("runtime-answer-hidden"));
        Assert.That(runtimeViewText, Does.Not.Contain("answerPanel?.classList.toggle"));
        Assert.That(runtimeViewText, Does.Contain("answerBox.disabled = !canAcceptTypedAnswer();"));
        Assert.That(runtimeViewText, Does.Contain("const canUseSpeechRecognition = () => canAcceptTypedAnswer()"));
        Assert.That(runtimeViewText, Does.Contain("const setRuntimeCaption = (speaker, text) =>"));
        Assert.That(runtimeViewText, Does.Contain("const syncAnswerCaption = () =>"));
        Assert.That(runtimeViewText, Does.Contain("videoCaptionSpeaker.textContent = `${speaker}:`;"));
        Assert.That(runtimeViewText, Does.Contain("setRuntimeCaption('Interviewer', currentQuestionText());"));
        Assert.That(runtimeViewText, Does.Contain("setRuntimeCaption('You', currentAnswer);"));
        Assert.That(runtimeViewText, Does.Not.Contain("console.log(config)"));
        Assert.That(runtimeViewText, Does.Not.Contain("Settings Config"));
        Assert.That(runtimeViewText, Does.Not.Contain("Body: ${text}"));
        Assert.That(runtimeViewText, Does.Contain("const ensureRuntimeTokenFresh = async () =>"));
        Assert.That(runtimeViewText, Does.Contain("Interview session token expired."));
        Assert.That(runtimeViewText, Does.Not.Contain("Interview token refresh failed."));
        Assert.That(runtimeViewText, Does.Not.Contain("refreshTokenWithRetry"));
        Assert.That(runtimeViewText, Does.Not.Contain("scheduleTokenRefresh"));
        Assert.That(runtimeViewText, Does.Not.Contain("tokenRefreshPromise"));
        Assert.That(runtimeViewText, Does.Not.Contain("tokenRefreshInFlight"));
        Assert.That(runtimeViewText, Does.Not.Contain("applyTokenUpdate"));
        Assert.That(runtimeViewText, Does.Not.Contain("updateRuntimeUrlToken"));
        Assert.That(runtimeViewText, Does.Contain("showUnavailableQuestionState"));
        Assert.That(runtimeViewText, Does.Contain("normalized === 'AI service unavailable. Please try again later.'"));
        Assert.That(runtimeViewText, Does.Contain("const hasActiveQuestion = () => !interviewUnavailable && !isPlaceholderSpeechText(currentQuestionText());"));
        Assert.That(runtimeViewText, Does.Contain("const disableSubmit = answerBox.disabled"));
        Assert.That(runtimeViewText, Does.Contain("|| !interviewStarted"));
        Assert.That(runtimeViewText, Does.Contain("|| runtimeStoppedOrCompleted"));
        Assert.That(runtimeViewText, Does.Contain("|| stopInProgress"));
        Assert.That(runtimeViewText, Does.Contain("|| !isCameraActive()"));
        Assert.That(runtimeViewText, Does.Contain("|| !isMicActive()"));
        Assert.That(runtimeViewText, Does.Contain("|| isScreenShareBlockingInterview()"));
        Assert.That(runtimeViewText, Does.Contain("interviewUnavailable = true;"));
        Assert.That(runtimeViewText, Does.Contain("let runtimeStoppedOrCompleted = false;"));
        Assert.That(runtimeViewText, Does.Contain("let stopInProgress = false;"));
        Assert.That(runtimeViewText, Does.Contain("let screenShareRequired = true;"));
        Assert.That(runtimeViewText, Does.Contain("let screenShareActive = false;"));
        Assert.That(runtimeViewText, Does.Contain("let screenShareInterrupted = false;"));
        Assert.That(runtimeViewText, Does.Contain("if (runtimeStoppedOrCompleted || stopInProgress)"));
        Assert.That(runtimeViewText, Does.Contain("const autoSubmitDelaySeconds = 15;"));
        Assert.That(runtimeViewText, Does.Contain("const clearAnswerTimers = () =>"));
        Assert.That(runtimeViewText, Does.Not.Contain("const clearTokenRefreshTimer = () =>"));
        Assert.That(runtimeViewText, Does.Contain("const clearAllRuntimeTimers = () =>"));
        Assert.That(runtimeViewText, Does.Contain("Submit Answer (${countdownValue})"));
        Assert.That(runtimeViewText, Does.Contain("answerBox.addEventListener('input', () => {"));
        Assert.That(runtimeViewText, Does.Contain("resetTimers();"));
        Assert.That(runtimeViewText, Does.Not.Contain("answerStageTimer = setTimeout(() => {"));
        Assert.That(runtimeViewText, Does.Not.Contain("}, autoSubmitDelaySeconds * 1000);"));
        Assert.That(runtimeViewText, Does.Contain("Please speak or type something."));
        Assert.That(runtimeViewText, Does.Contain("stopInProgress = true;"));
        Assert.That(runtimeViewText, Does.Contain("runtimeStoppedOrCompleted = true;"));
        Assert.That(runtimeViewText, Does.Not.Contain("clearTokenRefreshTimer();"));
        Assert.That(runtimeViewText, Does.Contain("if (!config.recordingUploadUrl || !blob || recordingUploadInFlight)"));
        Assert.That(runtimeViewText, Does.Contain("if (!interviewStarted) {"));
        Assert.That(runtimeViewText, Does.Contain("<div class=\"runtime-conversation\" id=\"conversation\">"));
        Assert.That(runtimeViewText, Does.Contain("id=\"conversation-empty-state\""));
        Assert.That(runtimeViewText, Does.Contain("runtime-chat-placeholder"));
        Assert.That(runtimeViewText, Does.Contain("runtime-chat-message"));
        Assert.That(runtimeViewText, Does.Contain("runtime-chat-avatar"));
        Assert.That(runtimeViewText, Does.Not.Contain("Questions and answers appear here in order."));
        Assert.That(runtimeViewText, Does.Not.Contain("startButton.textContent = 'Next Question';"));
        Assert.That(runtimeViewText, Does.Contain("primaryActionButton"));
        Assert.That(runtimeViewText, Does.Contain("setButtonLabel(primaryActionButton, 'Submit Answer');"));
        Assert.That(runtimeViewText, Does.Contain("const updateStartButtonState = () =>"));
        Assert.That(runtimeViewText, Does.Contain("const normalizeTurn = (turn, index = 0) =>"));
        Assert.That(runtimeViewText, Does.Not.Contain("score: getValue(turn, 'score', 'Score')"));
        Assert.That(runtimeViewText, Does.Not.Contain("feedback: getValue(turn, 'feedback', 'Feedback')"));
        Assert.That(runtimeViewText, Does.Contain("id=\"stop-interview-top\" class=\"button-2 runtime-stop-button\" disabled"));
        Assert.That(runtimeViewText, Does.Contain("id=\"stop-interview\" class=\"button-2 runtime-js-hidden\" disabled"));
        Assert.That(runtimeViewText, Does.Contain("const updateStopButtonsState = () =>"));
        Assert.That(runtimeViewText, Does.Contain("const disableStop = !interviewStarted || runtimeStoppedOrCompleted || stopInProgress;"));
        Assert.That(runtimeViewText, Does.Not.Contain("Score: ${normalizedTurn.score ?? '-'}"));
        Assert.That(runtimeViewText, Does.Contain("await stopRecording(true);"));
        Assert.That(runtimeViewText, Does.Contain("let completionRecordingCleanupPromise = null;"));
        Assert.That(runtimeViewText, Does.Contain("const finalizeRecordingBeforeCompletion = async () =>"));
        Assert.That(runtimeViewText, Does.Contain("if (completionRecordingCleanupPromise)"));
        Assert.That(runtimeViewText, Does.Contain("Final recording upload before completion started."));
        Assert.That(runtimeViewText, Does.Contain("const minimumFinalizationWaitMs = recordingUploadTimeoutMs + 5000;"));
        Assert.That(runtimeViewText, Does.Contain("const finalizationWaitTimeoutMs = Math.max(receivedFinalizationWaitTimeoutMs, minimumFinalizationWaitMs);"));
        Assert.That(runtimeViewText, Does.Contain("finalizeRecordingBeforeCompletion()"));
        Assert.That(runtimeViewText, Does.Not.Contain("const startReportGenerationTimer = (reportUrl) =>"));
        Assert.That(runtimeViewText, Does.Not.Contain(".finally(() => startReportGenerationTimer(reportUrl));"));
        Assert.That(runtimeViewText, Does.Contain("updateReportButton(reportUrl);"));
        Assert.That(runtimeViewText, Does.Contain("updateReportButton('');"));
        Assert.That(runtimeViewText, Does.Contain("const navigateToReport = (reportUrl) =>"));
        Assert.That(runtimeViewText, Does.Contain("let reportNavigationStarted = false;"));
        Assert.That(runtimeViewText, Does.Contain("let speechTokenCache = null;"));
        Assert.That(runtimeViewText, Does.Contain("let speechTokenRequestPromise = null;"));
        Assert.That(runtimeViewText, Does.Contain("const reportRuntimeClientStageTiming = (stageName, elapsedMilliseconds, success = true, token = null) =>"));
        Assert.That(runtimeViewText, Does.Contain("success: success === true ? 'true' : 'false'"));
        Assert.That(runtimeViewText, Does.Contain("Preparing your next question..."));
        Assert.That(runtimeViewText, Does.Not.Contain("clearAllRuntimeTimers();\r\n            let originalText = ''").And.Not.Contain("clearAllRuntimeTimers();\n            let originalText = ''"));
        Assert.That(runtimeViewText, Does.Contain("clearAnswerTimers();"));
        Assert.That(runtimeViewText, Does.Contain("if (interviewStarted && hasActiveQuestion() && !answerNeedsEditAfterFailure)\r\n                    resetTimers();").Or.Contain("if (interviewStarted && hasActiveQuestion() && !answerNeedsEditAfterFailure)\n                    resetTimers();"));
        Assert.That(runtimeViewText, Does.Contain("window.addEventListener('pagehide', () => {"));
        Assert.That(runtimeViewText, Does.Contain("const shouldWarnBeforeUnload = () => interviewStarted && !runtimeStoppedOrCompleted && !stopInProgress;"));
        Assert.That(runtimeViewText, Does.Contain("window.addEventListener('beforeunload', (event) => {"));
        Assert.That(runtimeViewText, Does.Contain("if (!shouldWarnBeforeUnload())"));
        Assert.That(runtimeViewText, Does.Contain("event.returnValue = '';"));
        Assert.That(runtimeViewText, Does.Contain("Camera permission was denied. Camera access is required for this interview."));
        Assert.That(runtimeViewText, Does.Contain("Microphone permission was denied. Microphone access is required for this interview."));
        Assert.That(runtimeViewText, Does.Contain("Recording is waiting for screen share because camera or microphone permission was denied."));
        Assert.That(runtimeViewText, Does.Contain("Recording remains available with screen share."));
        Assert.That(runtimeViewText, Does.Contain("runtime-log-panel"));
        Assert.That(runtimeViewText, Does.Contain("runtimeLog.style.display = debugRuntime ? 'block' : 'none';"));
        Assert.That(runtimeViewText, Does.Contain("id=\"screen-share-status\""));
        Assert.That(runtimeViewText, Does.Contain("id=\"screen-share-interruption-warning\""));
        Assert.That(runtimeViewText, Does.Contain("Resume screen share to continue."));
        Assert.That(runtimeViewText, Does.Contain("setScreenShareInterruptionWarning(true);"));
        Assert.That(runtimeViewText, Does.Contain("setScreenShareInterruptionWarning(false);"));
        Assert.That(runtimeViewText, Does.Not.Contain("Plugins.Misc.AIInterview.Runtime.ScreenSharingOptional"));
        Assert.That(runtimeViewText, Does.Contain("Plugins.Misc.AIInterview.Runtime.Guidelines.Title"));
        Assert.That(runtimeViewText, Does.Contain("id=\"guidelines-acknowledge\""));
        Assert.That(runtimeViewText, Does.Contain("I consent to use my camera, microphone, and other media devices for this interview."));
        Assert.That(runtimeViewText, Does.Contain("Before starting the interview, we need to test your system to ensure optimal performance."));
        Assert.That(runtimeViewText, Does.Contain("Camera and microphone access for video communication"));
        Assert.That(runtimeViewText, Does.Contain("Internet connection speed for stable interview experience"));
        Assert.That(runtimeViewText, Does.Contain("Speech recognition readiness for voice interaction"));
        Assert.That(runtimeViewText, Does.Contain("let guidelinesAcknowledged = false;"));
        Assert.That(runtimeViewText, Does.Contain("primaryActionButton.disabled = !guidelinesAcknowledged;"));
        Assert.That(runtimeViewText, Does.Contain("setButtonLabel(primaryActionButton, 'Start Interview');"));
        Assert.That(runtimeViewText, Does.Contain("guidelinesModalTimer = setTimeout(openGuidelinesModal, 3000);"));
        Assert.That(runtimeViewText, Does.Contain("guidelinesAcknowledgeLabel.addEventListener('click', (event) => {"));
        Assert.That(runtimeViewText, Does.Contain("guidelinesCheckbox.addEventListener('keydown', (event) => {"));
        Assert.That(runtimeViewText, Does.Contain("navigator.mediaDevices?.getDisplayMedia"));
        Assert.That(runtimeViewText, Does.Contain("screenShareStream = await navigator.mediaDevices.getDisplayMedia({"));
        Assert.That(runtimeViewText, Does.Contain("audio: true,"));
        Assert.That(runtimeViewText, Does.Contain("systemAudio: 'include'"));
        Assert.That(runtimeViewText, Does.Contain("surfaceSwitching: 'include'"));
        Assert.That(runtimeViewText, Does.Not.Contain("screenShareStream = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: false });"));
        Assert.That(runtimeViewText, Does.Contain("Screen sharing is required to start the interview."));
        Assert.That(runtimeViewText, Does.Contain("let screenShareRequired = true;"));
        Assert.That(runtimeViewText, Does.Contain("setScreenShareStatus('Screen sharing active', 'active');"));
        Assert.That(runtimeViewText, Does.Contain("Screen sharing active without system or tab audio."));
        Assert.That(runtimeViewText, Does.Contain("logActivity('Screen sharing started without a system or tab audio track.')"));
        Assert.That(runtimeViewText, Does.Contain("Screen sharing ended. Resume screen sharing to continue the interview."));
        Assert.That(runtimeViewText, Does.Contain("setScreenShareStatus('Screen sharing ended. Resume screen sharing to continue.', 'warning');"));
        Assert.That(runtimeViewText, Does.Contain("setScreenShareStatus('Screen sharing resumed', 'active');"));
        Assert.That(runtimeViewText, Does.Contain("Screen sharing resumed. You can continue the interview."));
        Assert.That(runtimeViewText, Does.Contain("function isScreenShareBlockingInterview()"));
        Assert.That(runtimeViewText, Does.Contain("screenShareInterrupted = true;"));
        Assert.That(runtimeViewText, Does.Contain("screenShareInterrupted = false;"));
        Assert.That(runtimeViewText, Does.Contain("await stopSpeechRecognition();"));
        Assert.That(runtimeViewText, Does.Contain("logActivity(`${auto ? 'Auto-submit' : 'Manual submit'} blocked; screen sharing is inactive.`);"));
        Assert.That(runtimeViewText, Does.Contain("Resume screen sharing to continue the interview."));
        Assert.That(beginInterviewStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(beginInterviewScreenShareIndex, Is.GreaterThan(beginInterviewStart));
        Assert.That(beginInterviewFixedExpiryGateIndex, Is.GreaterThan(beginInterviewStart));
        Assert.That(beginInterviewScreenShareIndex, Is.LessThan(beginInterviewFixedExpiryGateIndex));
        Assert.That(beginInterviewEndpointGateIndex, Is.GreaterThan(beginInterviewFixedExpiryGateIndex));
        Assert.That(beginInterviewPostIndex, Is.GreaterThan(beginInterviewEndpointGateIndex));
        Assert.That(beginInterviewFixedExpiryGateBlock, Does.Contain("stopScreenShare();"));
        Assert.That(beginInterviewFixedExpiryGateBlock, Does.Contain("interviewStarted = false;"));
        Assert.That(beginInterviewFixedExpiryGateBlock, Does.Contain("updateStartButtonState();"));
        Assert.That(beginInterviewFixedExpiryGateBlock, Does.Contain("updateSubmitAvailability();"));
        Assert.That(beginInterviewFixedExpiryGateBlock, Does.Contain("return;"));
        Assert.That(beginInterviewFixedExpiryGateBlock, Does.Not.Contain("setStatus("));
        Assert.That(runtimeViewText, Does.Contain("tracks.push(...screenShareStream.getTracks().filter("));
        Assert.That(runtimeViewText, Does.Contain("let preservedRecordingSegments = [];"));
        Assert.That(runtimeViewText, Does.Contain("await stopRecording(false, { preserveSegment: true, statusMessage: 'Recording paused until screen sharing resumes.' });"));
        Assert.That(onScreenShareInterruptedBlock, Does.Not.Contain("await syncRecording();"));
        Assert.That(runtimeViewText, Does.Contain("const canStartRecording = () => {"));
        Assert.That(runtimeViewText, Does.Contain("if (screenShareRequired && (!screenShareActive || screenShareInterrupted))"));
        Assert.That(runtimeViewText, Does.Contain("const segments = [...preservedRecordingSegments];"));
        Assert.That(runtimeViewText, Does.Contain("const preservedBlob = preservedRecordingSegments.length === 1"));
        Assert.That(runtimeViewText, Does.Contain("await stopRecording(false, { preserveSegment: true, statusMessage: 'Recording restarting with resumed screen share.' });"));
        Assert.That(runtimeViewText, Does.Contain("Enable screen share, camera, or microphone before recording."));
        Assert.That(runtimeViewText, Does.Not.Contain("setRecordingStatus('Recording waiting for screen share, camera, or microphone.', false);"));
        Assert.That(runtimeViewText, Does.Contain("Recording paused until screen sharing resumes."));
        Assert.That(runtimeViewText, Does.Contain("speechRecognizer.recognizing = (_, e) => {"));
        Assert.That(runtimeViewText, Does.Contain("const interimText = (e.result?.text || '').trim();"));
        Assert.That(runtimeViewText, Does.Contain("const committedText = (answerBox.value || '').trim();"));
        Assert.That(runtimeViewText, Does.Contain("const combinedText = `${committedText ? `${committedText} ` : ''}${interimText}`.trim();"));
        Assert.That(runtimeViewText, Does.Contain("setRuntimeCaption('You', combinedText);"));
        Assert.That(runtimeViewText, Does.Contain("speechRecognizer.recognized = (_, e) => {"));
        Assert.That(runtimeViewText, Does.Contain("answerBox.value = `${answerBox.value ? `${answerBox.value.trim()} ` : ''}${e.result.text}`.trim();"));
        Assert.That(runtimeViewText, Does.Contain("syncAnswerCaption();"));
        Assert.That(runtimeViewText, Does.Contain("updateSubmitAvailability();"));
        Assert.That(runtimeViewText, Does.Contain("answerBox.addEventListener('input', () => {"));
        Assert.That(runtimeViewText, Does.Contain("const trimmedAnswer = (answerBox.value || '').trim();"));
        Assert.That(runtimeViewText, Does.Contain("updateAnswerInputState();"));
        Assert.That(runtimeViewText, Does.Contain("const voiceInputUnavailableMessage = 'Voice input is unavailable. Please continue by typing your answer.';"));
        Assert.That(runtimeViewText, Does.Contain("const voicePlaybackUnavailableMessage = 'Voice playback is unavailable. Please continue with the text question.';"));
        Assert.That(runtimeViewText, Does.Contain("speechRecognizer.canceled = async (_, eventArgs) => {"));
        Assert.That(runtimeViewText, Does.Contain("await disableSpeechForRuntime(voiceInputUnavailableMessage);"));
        Assert.That(runtimeViewText, Does.Contain("synthesizer.SynthesisCanceled = handleSynthesisCanceled;"));
        Assert.That(runtimeViewText, Does.Contain("synthesizer.synthesisCanceled = handleSynthesisCanceled;"));
        Assert.That(runtimeViewText, Does.Contain("if (!config.speechAvailable) {"));
        Assert.That(runtimeViewText, Does.Contain("setHeaderStatus(voicePlaybackUnavailableMessage, true);"));
        Assert.That(runtimeViewText, Does.Contain("clearRecoveredMediaBlockingStatus();"));
        Assert.That(runtimeViewText, Does.Contain("const currentHeaderStatus = (headerStatusBox?.textContent || '').trim();"));
        Assert.That(runtimeViewText, Does.Contain("Recording upload request start. blobBytes="));
        Assert.That(runtimeViewText, Does.Contain("Recording upload response success. url="));
        Assert.That(runtimeViewText, Does.Contain("Recording chunk captured. chunkCount="));
        Assert.That(runtimeViewText, Does.Contain("MediaRecorder support confirmed. requestedMimeType="));
        Assert.That(runtimeViewText, Does.Contain("acknowledgeGuidelinesUrl"));
        Assert.That(runtimeViewText, Does.Contain("sendGuidelinesAcknowledgementAudit"));
        Assert.That(runtimeViewText, Does.Contain("console.info('[AIInterview Runtime] Guidelines acknowledged', payload);"));
        Assert.That(runtimeViewText, Does.Contain("console.warn('[AIInterview Runtime] Guidelines acknowledgement audit failed.', result);"));
        Assert.That(runtimeViewText, Does.Contain("stopScreenShare();"));
        Assert.That(runtimeViewText, Does.Contain("const beginResult = await postForm(config.beginInterviewUrl"));
        Assert.That(runtimeViewText, Does.Contain("showUnavailableQuestionState(beginMessage);"));
        Assert.That(runtimeViewText, Does.Contain("questionBox.textContent = firstQuestion;"));
        Assert.That(runtimeViewText, Does.Contain("const reportGenerationMessage = 'Generating your report...';"));
        Assert.That(runtimeViewText, Does.Contain("const completionWaitSeconds = 120;"));
        Assert.That(runtimeViewText, Does.Contain("const formatCountdown = (remainingSeconds) =>"));
        Assert.That(runtimeViewText, Does.Contain("timer.textContent = `Time remaining: ${formatCountdown(remainingSeconds)}`;"));
        Assert.That(runtimeViewText, Does.Not.Contain("completedRedirectTimer = setTimeout(() =>"));
        Assert.That(runtimeViewText, Does.Not.Contain("getValue(result, 'feedback', 'Feedback') || getRuntimeMessage(result, '') || '';"));

        Assert.That(runtimeViewText, Does.Not.Contain("AgoraRTC"));
        Assert.That(runtimeViewText, Does.Not.Contain("download.agora.io"));
        Assert.That(runtimeViewText, Does.Not.Contain("ensureAgoraSession"));
        Assert.That(runtimeViewText, Does.Not.Contain("renewAgoraToken"));
        Assert.That(runtimeViewText, Does.Not.Contain("leaveAgoraSession"));
        Assert.That(runtimeViewText, Does.Not.Contain("agora-token"));
        Assert.That(runtimeViewText, Does.Not.Contain("live interviewer"));
        Assert.That(runtimeViewText, Does.Not.Contain("participant flow"));
        Assert.That(runtimeViewText, Does.Not.Contain("mobileDetect"));
        Assert.That(runtimeViewText, Does.Not.Contain("userAgentData.mobile"));
        Assert.That(runtimeViewText, Does.Contain("fa-solid fa-robot"));
        Assert.That(runtimeViewText, Does.Contain("fa-solid fa-user"));
        Assert.That(runtimeViewText, Does.Contain("toggle-screen-share"));
        Assert.That(runtimeViewText, Does.Contain("repeat-question"));
        Assert.That(runtimeViewText, Does.Contain("runtime-back"));
        Assert.That(runtimeViewText, Does.Contain("id=\"runtime-message\" class=\"runtime-message is-info runtime-js-hidden\""));
        Assert.That(runtimeViewText, Does.Contain("id=\"runtime-status\" class=\"runtime-status runtime-js-hidden\""));
        Assert.That(runtimeViewText, Does.Contain("id=\"recording-status\" role=\"status\" aria-live=\"polite\""));
    }

    [Test]
    public void RuntimeView_Speaks_Final_Completion_Message_Once()
    {
        var runtimeViewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));

        var speechFunctionStart = runtimeViewText.IndexOf("const getFinalCompletionSpeechText = () =>", StringComparison.Ordinal);
        var speechFunctionEnd = runtimeViewText.IndexOf("const hasAnswerText", speechFunctionStart, StringComparison.Ordinal);
        var speechFunction = runtimeViewText.Substring(speechFunctionStart, speechFunctionEnd - speechFunctionStart);
        var prefetchFunctionStart = runtimeViewText.IndexOf("const prefetchFinalCompletionSpeechToken = async () =>", StringComparison.Ordinal);
        var prefetchFunctionEnd = runtimeViewText.IndexOf("const preloadSpeechResources = () =>", prefetchFunctionStart, StringComparison.Ordinal);
        var prefetchFunction = runtimeViewText.Substring(prefetchFunctionStart, prefetchFunctionEnd - prefetchFunctionStart);
        var speakTextStart = runtimeViewText.IndexOf("const speakText = async", StringComparison.Ordinal);
        var speakTextEnd = runtimeViewText.IndexOf("const setCamera = async", speakTextStart, StringComparison.Ordinal);
        var speakTextBlock = runtimeViewText.Substring(speakTextStart, speakTextEnd - speakTextStart);
        var submitAnswerStart = runtimeViewText.IndexOf("const submitAnswer = async", StringComparison.Ordinal);
        var submitAnswerEnd = runtimeViewText.IndexOf("const stopInterview = async", submitAnswerStart, StringComparison.Ordinal);
        var submitAnswerBlock = runtimeViewText.Substring(submitAnswerStart, submitAnswerEnd - submitAnswerStart);
        var terminalBranchStart = submitAnswerBlock.IndexOf("if (isTerminated) {", StringComparison.Ordinal);
        var terminalBranchEnd = submitAnswerBlock.IndexOf("updateStartButtonState();", terminalBranchStart, StringComparison.Ordinal);
        var terminalBranch = submitAnswerBlock.Substring(terminalBranchStart, terminalBranchEnd - terminalBranchStart);
        var prefetchCall = submitAnswerBlock.IndexOf("finalCompletionSpeechTokenResult = await prefetchFinalCompletionSpeechToken();", StringComparison.Ordinal);
        var submitPost = submitAnswerBlock.IndexOf("const result = await postForm(config.submitAnswerUrl", StringComparison.Ordinal);
        var submitSuccessGuard = submitAnswerBlock.IndexOf("if (!isSuccess(result))", submitPost, StringComparison.Ordinal);
        var latchCheck = terminalBranch.IndexOf("if (!finalCompletionSpoken)", StringComparison.Ordinal);
        var latchSet = terminalBranch.IndexOf("finalCompletionSpoken = true;", latchCheck, StringComparison.Ordinal);
        var completedState = terminalBranch.IndexOf("await setCompletedState(result, finalRecordingUploadPromise);", StringComparison.Ordinal);
        var speechCall = terminalBranch.IndexOf("await speakText(getFinalCompletionSpeechText(), 'completion', finalCompletionSpeechTokenResult)", latchSet, StringComparison.Ordinal);
        var speechCatch = terminalBranch.IndexOf(".catch(() => logActivity('Final completion speech failed.'));", speechCall, StringComparison.Ordinal);

        Assert.That(runtimeViewText, Does.Contain("let finalCompletionSpoken = false;"));
        Assert.That(runtimeViewText, Does.Contain("const finalCompletionSpeechStorageKey = 'aiinterview.runtime.finalCompletionSpeech.@Model.SessionId';"));
        Assert.That(runtimeViewText, Does.Contain("finalCompletionSpoken = readLocalStorageValue(finalCompletionSpeechStorageKey) === '1';"));
        Assert.That(runtimeViewText, Does.Contain("writeLocalStorageValue(finalCompletionSpeechStorageKey, '1');"));
        Assert.That(runtimeViewText, Does.Contain($"const fallbackFinalCompletionSpeech = '{ApprovedFinalCompletionSpeech}';"));
        Assert.That(runtimeViewText, Does.Contain("const finalCompletionSpeech = (config.finalCompletionSpeech || '').trim() || fallbackFinalCompletionSpeech;"));
        Assert.That(runtimeViewText, Does.Contain("const getFinalCompletionSpeechText = () => finalCompletionSpeech;"));
        Assert.That(speechFunction, Does.Not.Contain("result"));
        Assert.That(speechFunction, Does.Not.Contain("getValue("));
        Assert.That(speechFunction, Does.Not.Contain("'Completion'"));
        Assert.That(runtimeViewText, Does.Not.Contain("defaultFinalCompletionMessage"));
        Assert.That(runtimeViewText, Does.Not.Contain("getValue(result, 'completion', 'Completion')"));
        Assert.That(runtimeViewText, Does.Not.Contain("getValue(result, \"completion\", \"Completion\")"));
        Assert.That(runtimeViewText.Split("speakText(getFinalCompletionSpeechText()", StringSplitOptions.None).Length - 1, Is.EqualTo(1));
        Assert.That(runtimeViewText, Does.Contain("const shouldResumeRecognition = purpose !== 'completion' && shouldStopRecognitionForPlayback;"));
        Assert.That(runtimeViewText, Does.Contain("if (shouldResumeRecognition && !runtimeStoppedOrCompleted && !speechUnavailable && isMicActive())"));
        Assert.That(runtimeViewText, Does.Contain("const speakText = async (text, purpose = 'question', prefetchedTokenResult = undefined) =>"));
        Assert.That(speakTextBlock, Does.Contain("const tokenResult = prefetchedTokenResult !== undefined"));
        Assert.That(speakTextBlock, Does.Contain(": await requestSpeechToken();"));
        Assert.That(speakTextBlock, Does.Contain("logActivity('Final completion speech failed: Speech SDK unavailable.');"));
        Assert.That(speakTextBlock, Does.Contain("logActivity(`Final completion speech failed: ${failureReason}`);"));
        Assert.That(speakTextBlock, Does.Contain("logActivity(`Final completion speech failed: ${reason}`);"));
        Assert.That(speakTextBlock, Does.Contain("await speakTextWithDefaultOutput(speechConfig, text, reportTtsStartedOnce, { suppressCandidateMessage: purpose === 'completion' });"));
        Assert.That(prefetchFunction, Does.Contain("await requestSpeechToken({ suppressCandidateMessage: true });"));
        Assert.That(prefetchFunction, Does.Contain("logActivity(`Final completion speech prefetch failed:"));
        Assert.That(prefetchFunction, Does.Contain("return null;"));
        Assert.That(prefetchCall, Is.GreaterThanOrEqualTo(0));
        Assert.That(submitPost, Is.GreaterThanOrEqualTo(0));
        Assert.That(prefetchCall, Is.LessThan(submitPost));
        Assert.That(submitSuccessGuard, Is.GreaterThan(submitPost));
        Assert.That(terminalBranchStart, Is.GreaterThan(submitSuccessGuard));
        Assert.That(latchCheck, Is.GreaterThanOrEqualTo(0));
        Assert.That(latchSet, Is.GreaterThan(latchCheck));
        Assert.That(speechCall, Is.GreaterThan(latchSet));
        Assert.That(speechCatch, Is.GreaterThan(speechCall));
        Assert.That(completedState, Is.GreaterThanOrEqualTo(0));
        Assert.That(completedState, Is.LessThan(latchCheck));
        Assert.That(terminalBranch, Does.Contain("await speakText(getFinalCompletionSpeechText(), 'completion', finalCompletionSpeechTokenResult)"));
        Assert.That(terminalBranch, Does.Not.Contain("requestSpeechToken("));
        Assert.That(completedState, Is.LessThan(speechCall));
        Assert.That(speakTextBlock, Does.Contain("if (purpose === 'completion')\r\n                    return;").Or.Contain("if (purpose === 'completion')\n                    return;"));
        Assert.That(speakTextBlock, Does.Contain("if (purpose !== 'completion')\r\n                    reportRuntimeClientStageTiming('tts-completed'").Or.Contain("if (purpose !== 'completion')\n                    reportRuntimeClientStageTiming('tts-completed'"));
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("score").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("strengths").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("improvement areas").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("selection").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("rejection").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("recruiter").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("contact").IgnoreCase);
        Assert.That(ApprovedFinalCompletionSpeech, Does.Not.Contain("guaranteed").IgnoreCase);
    }

    [Test]
    public void RuntimeView_StageTimingAndLatencyMarkers_AreOrderedByActualSuccess()
    {
        var runtimeViewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));

        var beginStart = runtimeViewText.IndexOf("const beginResult = await postForm(config.beginInterviewUrl", StringComparison.Ordinal);
        var beginSuccessGuard = runtimeViewText.IndexOf("if (!isSuccess(beginResult))", beginStart, StringComparison.Ordinal);
        var firstQuestionRead = runtimeViewText.IndexOf("const firstQuestion = getValue(beginResult", beginSuccessGuard, StringComparison.Ordinal);
        var firstQuestionGuard = runtimeViewText.IndexOf("if (!firstQuestion)", firstQuestionRead, StringComparison.Ordinal);
        var beginTiming = runtimeViewText.IndexOf("reportRuntimeClientStageTiming('begin-response'", firstQuestionGuard, StringComparison.Ordinal);
        Assert.That(beginTiming, Is.GreaterThan(firstQuestionGuard));

        var controlledTts = runtimeViewText.Substring(
            runtimeViewText.IndexOf("const synthesizeSpeechAudioData = async", StringComparison.Ordinal),
            runtimeViewText.IndexOf("const playControlledSpeechAudio = async", StringComparison.Ordinal) - runtimeViewText.IndexOf("const synthesizeSpeechAudioData = async", StringComparison.Ordinal));
        Assert.That(controlledTts.IndexOf("onSynthesisStarted?.();", StringComparison.Ordinal), Is.GreaterThan(controlledTts.IndexOf("synthesizer.speakTextAsync(text", StringComparison.Ordinal)));

        var defaultTts = runtimeViewText.Substring(
            runtimeViewText.IndexOf("const speakTextWithDefaultOutput = async", StringComparison.Ordinal),
            runtimeViewText.IndexOf("const speakText = async", StringComparison.Ordinal) - runtimeViewText.IndexOf("const speakTextWithDefaultOutput = async", StringComparison.Ordinal));
        Assert.That(defaultTts.IndexOf("onSynthesisStarted?.();", StringComparison.Ordinal), Is.GreaterThan(defaultTts.IndexOf("synthesizer.speakTextAsync(text", StringComparison.Ordinal)));

        Assert.That(runtimeViewText, Does.Contain("let ttsStartedReported = false;"));
        Assert.That(runtimeViewText, Does.Contain("const reportTtsStartedOnce = () =>"));
        Assert.That(runtimeViewText, Does.Contain("if (ttsStartedReported)"));
        Assert.That(runtimeViewText, Does.Contain("await synthesizeSpeechAudioData(speechConfig, text, reportTtsStartedOnce);"));
        Assert.That(runtimeViewText, Does.Contain("await speakTextWithDefaultOutput(speechConfig, text, reportTtsStartedOnce, { suppressCandidateMessage: purpose === 'completion' });"));
    }

    [Test]
    public void RuntimeView_PrepareCompletesBeforeInitialSpeechPreloadAndStageTimingUsesPostResponseToken()
    {
        var runtimeViewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));
        var prepareStart = runtimeViewText.IndexOf("const startPrepareInterview = () =>", StringComparison.Ordinal);
        var prepareEnd = runtimeViewText.IndexOf("const postMultipart = async", prepareStart, StringComparison.Ordinal);
        Assert.That(prepareStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(prepareEnd, Is.GreaterThan(prepareStart));

        var prepareBlock = runtimeViewText.Substring(prepareStart, prepareEnd - prepareStart);
        var ensureFresh = prepareBlock.IndexOf("if (!(await ensureRuntimeTokenFresh()))", StringComparison.Ordinal);
        var tokenSnapshot = prepareBlock.IndexOf("const prepareToken = sessionToken;", StringComparison.Ordinal);
        var preparePost = prepareBlock.IndexOf("const result = await postForm(config.prepareInterviewUrl, new URLSearchParams({ token: prepareToken }));", StringComparison.Ordinal);
        var readyBranch = prepareBlock.IndexOf("if (isSuccess(result) && ready) {", StringComparison.Ordinal);
        var telemetryTokenSnapshot = prepareBlock.IndexOf("const prepareTelemetryToken = sessionToken;", readyBranch, StringComparison.Ordinal);
        var speechPreload = prepareBlock.IndexOf("preloadSpeechResources();", readyBranch, StringComparison.Ordinal);
        var prepareTiming = prepareBlock.IndexOf("reportRuntimeClientStageTiming('prepare-response'", readyBranch, StringComparison.Ordinal);

        Assert.That(ensureFresh, Is.GreaterThanOrEqualTo(0));
        Assert.That(tokenSnapshot, Is.GreaterThan(ensureFresh));
        Assert.That(preparePost, Is.GreaterThan(tokenSnapshot));
        Assert.That(readyBranch, Is.GreaterThan(preparePost));
        Assert.That(telemetryTokenSnapshot, Is.GreaterThan(readyBranch));
        Assert.That(speechPreload, Is.GreaterThan(telemetryTokenSnapshot));
        Assert.That(prepareTiming, Is.GreaterThan(speechPreload));
        Assert.That(prepareBlock.Split("preloadSpeechResources();", StringSplitOptions.None).Length - 1, Is.EqualTo(1));
        Assert.That(prepareBlock, Does.Contain("reportRuntimeClientStageTiming('prepare-response', performance.now() - prepareStartedAt, true, prepareTelemetryToken);"));

        var postFormStart = runtimeViewText.IndexOf("const postForm = async", StringComparison.Ordinal);
        var postFormEnd = runtimeViewText.IndexOf("let prepareInterviewPromise = null;", postFormStart, StringComparison.Ordinal);
        var postFormBlock = runtimeViewText.Substring(postFormStart, postFormEnd - postFormStart);
        Assert.That(postFormBlock, Does.Not.Contain("applyTokenUpdate(result);"));
        Assert.That(postFormBlock, Does.Contain("return result;"));

        var stageTimingStart = runtimeViewText.IndexOf("const reportRuntimeClientStageTiming = (stageName, elapsedMilliseconds, success = true, token = null)", StringComparison.Ordinal);
        var stageTimingEnd = runtimeViewText.IndexOf("if (completionUploadTimeoutMismatch)", stageTimingStart, StringComparison.Ordinal);
        var stageTimingBlock = runtimeViewText.Substring(stageTimingStart, stageTimingEnd - stageTimingStart);
        Assert.That(stageTimingBlock, Does.Contain("token: token || sessionToken"));
    }

    [Test]
    public void RuntimeView_RetainsLatencyRecordingAndNavigationRegressions()
    {
        var runtimeViewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));

        Assert.That(runtimeViewText, Does.Contain("if (speechTokenRequestPromise)\r\n                return speechTokenRequestPromise;").Or.Contain("if (speechTokenRequestPromise)\n                return speechTokenRequestPromise;"));
        Assert.That(runtimeViewText, Does.Contain("expiresAt: Date.now() + Math.max(30000, (expiresInSeconds - 60) * 1000)"));
        Assert.That(runtimeViewText, Does.Contain("})().finally(() => {\r\n                speechTokenRequestPromise = null;").Or.Contain("})().finally(() => {\n                speechTokenRequestPromise = null;"));
        var renderCompletionStart = runtimeViewText.IndexOf("const renderCompletionWaitState = (remainingSeconds) =>", StringComparison.Ordinal);
        var renderCompletionEnd = runtimeViewText.IndexOf("const stopCompletionStatusPolling", renderCompletionStart, StringComparison.Ordinal);
        var renderCompletionBlock = runtimeViewText.Substring(renderCompletionStart, renderCompletionEnd - renderCompletionStart);
        var completionWaitStart = runtimeViewText.IndexOf("const startReportGenerationWaitingState = (estimatedWaitSeconds = completionWaitSeconds) =>", StringComparison.Ordinal);
        var completionWaitEnd = runtimeViewText.IndexOf("const finalizeCompletedState = async", completionWaitStart, StringComparison.Ordinal);
        var completionWaitBlock = runtimeViewText.Substring(completionWaitStart, completionWaitEnd - completionWaitStart);

        Assert.That(runtimeViewText, Does.Contain("const completionWaitSeconds = 120;"));
        Assert.That(renderCompletionBlock, Does.Contain("setHeaderStatus(`${reportGenerationMessage} Time remaining: ${formatCountdown(remainingSeconds)}`, false);"));
        Assert.That(renderCompletionBlock, Does.Contain("completionBox.replaceChildren();"));
        Assert.That(renderCompletionBlock, Does.Contain("updateReportButton('');"));
        Assert.That(renderCompletionBlock, Does.Not.Contain("Elapsed"));
        Assert.That(completionWaitBlock, Does.Contain("let remaining = Math.max(0, Number(estimatedWaitSeconds || completionWaitSeconds));"));
        Assert.That(completionWaitBlock, Does.Contain("renderCompletionWaitState(remaining);"));
        Assert.That(completionWaitBlock, Does.Contain("startCompletionStatusPolling(generation);"));
        Assert.That(completionWaitBlock, Does.Contain("if (remaining <= 0)"));
        Assert.That(completionWaitBlock, Does.Contain("clearInterval(completedCountdown);"));
        Assert.That(completionWaitBlock, Does.Not.Contain("navigateToReport"));
        Assert.That(runtimeViewText, Does.Not.Contain("reportGenerationStartedAt"));
        Assert.That(runtimeViewText, Does.Not.Contain("completedRedirectDelaySeconds"));
        Assert.That(runtimeViewText, Does.Not.Contain("completedRedirectTimer"));
        Assert.That(runtimeViewText, Does.Not.Contain("startReportGenerationTimer"));
        Assert.That(runtimeViewText, Does.Contain("const reportReady = getValue(result, 'reportReady', 'ReportReady') === true;"));
        Assert.That(runtimeViewText, Does.Contain("if (reportReady && reportUrl)"));
        Assert.That(runtimeViewText, Does.Contain("updateReportButton(reportUrl);"));

        var finalizationStart = runtimeViewText.IndexOf("logActivity('Final recording upload before completion started.')", StringComparison.Ordinal);
        Assert.That(finalizationStart, Is.LessThan(runtimeViewText.IndexOf("await stopLiveMediaForCompletion();", finalizationStart, StringComparison.Ordinal)));
        Assert.That(runtimeViewText, Does.Contain("const finalRecordingUploadPromise = willCompleteOnSubmit\r\n                ? finalizeRecordingBeforeCompletion()\r\n                : null;").Or.Contain("const finalRecordingUploadPromise = willCompleteOnSubmit\n                ? finalizeRecordingBeforeCompletion()\n                : null;"));
        Assert.That(runtimeViewText, Does.Contain("await setCompletedState(result, finalRecordingUploadPromise);"));

        Assert.That(runtimeViewText, Does.Contain("reportButton.onclick = () => navigateToReport(reportUrl);"));
        Assert.That(runtimeViewText, Does.Contain("if (!reportUrl || reportNavigationStarted)"));
        Assert.That(runtimeViewText, Does.Contain("navigateToReport(reportUrl);"));
        Assert.That(runtimeViewText, Does.Contain("setHeaderStatus(`${message} Elapsed ${formatElapsedTime(elapsedSeconds)}`, false);"));
        Assert.That(runtimeViewText, Does.Contain("id=\"recording-status\" role=\"status\" aria-live=\"polite\""));
    }

    [Test]
    public void RuntimeView_Uses_Contextual_Title_And_Separates_Candidate_Details()
    {
        var runtimeViewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));
        var runtimeCssText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Content", "css", "aiinterview-public.css"));

        Assert.That(runtimeViewText, Does.Contain("Model.IsPracticeInterview"));
        Assert.That(runtimeViewText, Does.Contain("Interview on {(!string.IsNullOrWhiteSpace(practiceSkill) ? practiceSkill : \"Resume Practice\")}{(!string.IsNullOrWhiteSpace(Model.Difficulty) ? $\" - {Model.Difficulty}\" : string.Empty)}"));
        Assert.That(runtimeViewText, Does.Contain("Interview for {runtimeTopic}"));
        Assert.That(runtimeViewText, Does.Contain("<span class=\"runtime-candidate-chip\">@Model.CandidateName</span>"));
        Assert.That(runtimeViewText, Does.Contain("<span class=\"runtime-detail-label\">Candidate</span>"));
        Assert.That(runtimeViewText, Does.Not.Contain("Interview on {Model.ProductName} - {Model.CandidateName}"));
        Assert.That(runtimeViewText, Does.Contain("id=\"runtime-question-counter\" class=\"runtime-question-counter runtime-js-hidden\" aria-label=\"Interview question count\" hidden"));
        Assert.That(runtimeViewText, Does.Contain("id=\"runtime-video-caption\" class=\"runtime-video-caption runtime-js-hidden\" aria-live=\"polite\" hidden"));
        Assert.That(runtimeViewText, Does.Contain("videoCaption.hidden = true;"));
        Assert.That(runtimeViewText, Does.Contain("videoCaption.hidden = false;"));
        Assert.That(runtimeViewText, Does.Contain("questionCounter.hidden = activeQuestionNumber <= 0;"));
        Assert.That(runtimeViewText, Does.Contain("panel.hidden = !isActive;"));
        Assert.That(runtimeCssText, Does.Contain(".runtime-js-hidden {\r\n    display: none !important;").Or.Contain(".runtime-js-hidden {\n    display: none !important;"));
        Assert.That(runtimeCssText, Does.Contain(".runtime-question-counter[hidden],"));
        Assert.That(runtimeCssText, Does.Contain(".runtime-video-caption[hidden] {"));
        Assert.That(runtimeCssText, Does.Contain("@media (min-width: 1025px)"));
        Assert.That(runtimeCssText, Does.Contain(".runtime-video {\r\n        min-height: min(560px, 64vh);").Or.Contain(".runtime-video {\n        min-height: min(560px, 64vh);"));
        Assert.That(runtimeCssText, Does.Contain(".runtime-modal-card {\r\n    width: min(720px, 100%);\r\n    pointer-events: auto;\r\n    position: relative;\r\n    z-index: 1;").Or.Contain(".runtime-modal-card {\n    width: min(720px, 100%);\n    pointer-events: auto;\n    position: relative;\n    z-index: 1;"));
        Assert.That(runtimeCssText, Does.Contain(".runtime-guidelines-ack {\r\n    display: flex;").Or.Contain(".runtime-guidelines-ack {\n    display: flex;"));
        Assert.That(runtimeCssText, Does.Contain("pointer-events: auto;"));
        Assert.That(runtimeCssText, Does.Contain(".runtime-modal-actions .button-1,"));
    }

    [Test]
    public async Task RuntimeService_PracticeRuntime_UsesStoredSelectedSkill_ForDisplay()
    {
        var session = new InterviewSession
        {
            Id = 201,
            CustomerId = 8,
            ProductId = 44,
            Token = "practice-runtime-token",
            SessionKey = "practice-runtime-session",
            Difficulty = "Low",
            QuestionCount = 5,
            InterviewType = AIInterviewDefaults.InterviewTypeMockPractice,
            SelectedProductAttributesJson = "{\"attributes\":[{\"attributeId\":111,\"attributeName\":\"Practice Setup\",\"textPrompt\":\"Difficulty\",\"valueId\":501,\"value\":\"Low\"},{\"attributeId\":112,\"attributeName\":\"Practice Focus\",\"textPrompt\":\"Skill\",\"valueId\":502,\"value\":\"JAVA\"}]}"
        };
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();

        _sessionService.Setup(x => x.GetSessionByTokenAsync("practice-runtime-token")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(new List<InterviewTurn>());
        _productService.Setup(x => x.GetProductByIdAsync(session.ProductId)).ReturnsAsync(new Product { Id = session.ProductId, Name = "AI-Mock-Interview" });
        _customerService.Setup(x => x.GetCustomerByIdAsync(session.CustomerId)).ReturnsAsync(new Customer { Id = session.CustomerId, FirstName = "Sateesh", LastName = "Munagala" });

        var service = new InterviewRuntimeService(
            _sessionService.Object,
            turnService.Object,
            aiClient.Object,
            _productService.Object,
            _customerService.Object,
            new Mock<IApplicationService>().Object,
            new Mock<IResumeProfileService>().Object,
            new Mock<IAzureUsageService>().Object,
            _localizationService.Object,
            new AIInterviewSettings { Prompt = "Be concise" },
            new MockAIInterviewSettings { UseMockResponses = true },
            new Mock<System.Net.Http.IHttpClientFactory>().Object,
            _workContext.Object,
            _eventPublisher.Object,
            _nopLogger.Object);

        var model = await service.GetRuntimeModelAsync("practice-runtime-token");

        Assert.That(model, Is.Not.Null);
        Assert.That(model.IsPracticeInterview, Is.True);
        Assert.That(model.PracticeSkill, Is.EqualTo("JAVA"));
        Assert.That(model.RuntimeTopic, Is.EqualTo("JAVA"));
        Assert.That(model.Difficulty, Is.EqualTo("Low"));
        Assert.That(model.ProductName, Is.EqualTo("AI-Mock-Interview"));
    }

    [Test]
    public async Task RuntimeService_PracticeRuntime_UsesFirstNonDifficultyValue_WhenStoredLabelsAreGeneric()
    {
        var session = new InterviewSession
        {
            Id = 203,
            CustomerId = 8,
            ProductId = 46,
            Token = "practice-runtime-generic-token",
            SessionKey = "practice-runtime-generic-session",
            Difficulty = "Low",
            QuestionCount = 5,
            InterviewType = AIInterviewDefaults.InterviewTypeMockPractice,
            SelectedProductAttributesJson = "{\"attributes\":[{\"attributeId\":211,\"attributeName\":\"Practice Setup\",\"textPrompt\":\"Level\",\"valueId\":601,\"value\":\"Low\"},{\"attributeId\":212,\"attributeName\":\"Practice Focus\",\"textPrompt\":\"Primary focus\",\"valueId\":602,\"value\":\"JAVA\"}]}"
        };
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();

        _sessionService.Setup(x => x.GetSessionByTokenAsync("practice-runtime-generic-token")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(new List<InterviewTurn>());
        _productService.Setup(x => x.GetProductByIdAsync(session.ProductId)).ReturnsAsync(new Product { Id = session.ProductId, Name = "AI-Mock-Interview" });
        _customerService.Setup(x => x.GetCustomerByIdAsync(session.CustomerId)).ReturnsAsync(new Customer { Id = session.CustomerId, FirstName = "Sateesh", LastName = "Munagala" });

        var service = new InterviewRuntimeService(
            _sessionService.Object,
            turnService.Object,
            aiClient.Object,
            _productService.Object,
            _customerService.Object,
            new Mock<IApplicationService>().Object,
            new Mock<IResumeProfileService>().Object,
            new Mock<IAzureUsageService>().Object,
            _localizationService.Object,
            new AIInterviewSettings { Prompt = "Be concise" },
            new MockAIInterviewSettings { UseMockResponses = true },
            new Mock<System.Net.Http.IHttpClientFactory>().Object,
            _workContext.Object,
            _eventPublisher.Object,
            _nopLogger.Object);

        var model = await service.GetRuntimeModelAsync("practice-runtime-generic-token");

        Assert.That(model, Is.Not.Null);
        Assert.That(model.IsPracticeInterview, Is.True);
        Assert.That(model.PracticeSkill, Is.EqualTo("JAVA"));
        Assert.That(model.RuntimeTopic, Is.EqualTo("JAVA"));
        Assert.That(model.Difficulty, Is.EqualTo("Low"));
    }

    [Test]
    public async Task RuntimeService_JobRuntime_UsesJobTitleWithoutPracticeDifficultyFormatting()
    {
        var session = new InterviewSession
        {
            Id = 202,
            CustomerId = 8,
            ProductId = 45,
            Token = "job-runtime-token",
            SessionKey = "job-runtime-session",
            Difficulty = "Hard",
            QuestionCount = 5,
            InterviewType = AIInterviewDefaults.InterviewTypeJob
        };
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();

        _sessionService.Setup(x => x.GetSessionByTokenAsync("job-runtime-token")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(new List<InterviewTurn>());
        _productService.Setup(x => x.GetProductByIdAsync(session.ProductId)).ReturnsAsync(new Product { Id = session.ProductId, Name = "Senior Java Developer" });
        _customerService.Setup(x => x.GetCustomerByIdAsync(session.CustomerId)).ReturnsAsync(new Customer { Id = session.CustomerId, FirstName = "Sateesh", LastName = "Munagala" });

        var service = new InterviewRuntimeService(
            _sessionService.Object,
            turnService.Object,
            aiClient.Object,
            _productService.Object,
            _customerService.Object,
            new Mock<IApplicationService>().Object,
            new Mock<IResumeProfileService>().Object,
            new Mock<IAzureUsageService>().Object,
            _localizationService.Object,
            new AIInterviewSettings { Prompt = "Be concise" },
            new MockAIInterviewSettings { UseMockResponses = true },
            new Mock<System.Net.Http.IHttpClientFactory>().Object,
            _workContext.Object,
            _eventPublisher.Object,
            _nopLogger.Object);

        var model = await service.GetRuntimeModelAsync("job-runtime-token");

        Assert.That(model, Is.Not.Null);
        Assert.That(model.IsPracticeInterview, Is.False);
        Assert.That(model.PracticeSkill, Is.EqualTo(string.Empty));
        Assert.That(model.RuntimeTopic, Is.EqualTo("Senior Java Developer"));
        Assert.That(model.Difficulty, Is.EqualTo("Hard"));
    }

    [Test]
    public async Task Runtime_Get_WithExistingUnansweredTurn_DoesNotExposeQuestionInInitialModel()
    {
        var runtimeModel = new InterviewRuntimeModel
        {
            SessionId = 15,
            ProductId = 1,
            SessionKey = "session-15",
            Token = "token15",
            CurrentQuestion = string.Empty,
            Turns = Array.Empty<InterviewTurnViewModel>(),
            ClientSettings = new RuntimeClientSettingsModel()
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("token15")).ReturnsAsync(new InterviewSession
        {
            Id = 15,
            CustomerId = 1,
            IsActive = true,
            Token = "token15",
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        _interviewRuntimeService.Setup(x => x.GetRuntimeModelAsync("token15")).ReturnsAsync(runtimeModel);

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var result = (ViewResult)await controller.Runtime("token15");
        var model = (InterviewRuntimeModel)result.Model;

        Assert.That(model.CurrentQuestion, Is.Empty);
        Assert.That(model.Turns, Is.Empty);
    }

    [Test]
    public async Task Runtime_AcknowledgeGuidelines_LogsAuditTrail()
    {
        var session = new InterviewSession
        {
            Id = 77,
            CustomerId = 12,
            ProductId = 34,
            Token = "guidelines-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("guidelines-token")).ReturnsAsync(session);

        var result = await _runtimeController.AcknowledgeGuidelines("guidelines-token", "2026-06-14T10:15:00Z", "test-agent", "1920x1080", "1280x720");

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success")?.GetValue(json.Value, null);
        Assert.That(success, Is.EqualTo(true));
        _nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Information,
            "AI Interview runtime guidelines acknowledged",
            It.Is<string>(message =>
                message.Contains("Event=RuntimeGuidelinesAcknowledged") &&
                message.Contains("Token=guidel...") &&
                message.Contains("ReasonCode=valid") &&
                message.Contains("SessionId=77") &&
                message.Contains("CustomerId=12") &&
                message.Contains("ProductId=34") &&
                message.Contains("AcknowledgedTimestamp=2026-06-14T10:15:00Z") &&
                message.Contains("UserAgent=test-agent") &&
                message.Contains("ScreenSize=1920x1080") &&
                message.Contains("ViewportSize=1280x720")),
            It.IsAny<Customer>()), Times.Once);
    }

    [Test]
    public async Task Runtime_AcknowledgeGuidelines_MissingSession_LogsSafeReasonWithoutRawToken()
    {
        var rawToken = "raw-guidelines-token-secret";
        _sessionService.Setup(x => x.GetSessionByTokenAsync(rawToken)).ReturnsAsync((InterviewSession)null);

        var result = await _runtimeController.AcknowledgeGuidelines(rawToken, "2026-06-14T10:15:00Z", "test-agent", "1920x1080", "1280x720");

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        Assert.That(GetJsonValue<bool>(json, "success"), Is.False);
        Assert.That(GetJsonValue<string>(json, "message"), Is.EqualTo("Invalid or expired session token."));
        _nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Information,
            "AI Interview runtime guidelines acknowledged",
            It.Is<string>(message =>
                message.Contains("Event=RuntimeGuidelinesAcknowledged") &&
                message.Contains("Token=raw-gu...") &&
                message.Contains("ReasonCode=session-not-found") &&
                message.Contains("SessionId=0") &&
                message.Contains("CustomerId=0") &&
                message.Contains("ProductId=0") &&
                !message.Contains(rawToken)),
            It.IsAny<Customer>()), Times.Once);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task RuntimeClientEvent_ValidSession_WritesNetworkFailureActivity()
    {
        var session = new InterviewSession
        {
            Id = 78,
            CustomerId = 13,
            ProductId = 35,
            Token = "client-event-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        var customer = new Customer { Id = session.CustomerId, Email = "candidate@example.com" };
        var activityService = new Mock<ICustomerActivityService>();
        string activityComment = null;

        _sessionService.Setup(x => x.GetSessionByTokenAsync("client-event-token")).ReturnsAsync(session);
        _customerService.Setup(x => x.GetCustomerByIdAsync(session.CustomerId)).ReturnsAsync(customer);
        activityService.Setup(x => x.InsertActivityAsync(
                customer,
                "AIInterview.Runtime.NetworkRequestFailed",
                It.IsAny<string>(),
                It.IsAny<BaseEntity>()))
            .Callback<Customer, string, string, BaseEntity>((_, _, comment, _) => activityComment = comment)
            .ReturnsAsync(new ActivityLog());

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            nopLogger: _nopLogger.Object,
            customerActivityService: activityService.Object);

        var result = await controller.RuntimeClientEvent(
            "client-event-token",
            "network-request-failed",
            "submit-answer",
            500,
            "Candidate typed answer should not be logged",
            "http-status",
            1800);

        Assert.That(result, Is.TypeOf<JsonResult>());
        Assert.That(((JsonResult)result).Value.GetType().GetProperty("success")?.GetValue(((JsonResult)result).Value, null), Is.EqualTo(true));
        Assert.That(activityComment, Does.Contain("SessionId=78"));
        Assert.That(activityComment, Does.Contain("CustomerId=13"));
        Assert.That(activityComment, Does.Contain("ProductId=35"));
        Assert.That(activityComment, Does.Contain("Request=submit-answer"));
        Assert.That(activityComment, Does.Contain("StatusCode=500"));
        Assert.That(activityComment, Does.Contain("FailureKind=http-status"));
        Assert.That(activityComment, Does.Contain("ElapsedMs=1800"));
        Assert.That(activityComment, Does.Contain("Message=Runtime request returned HTTP 500."));
        Assert.That(activityComment, Does.Not.Contain("client-event-token"));
        Assert.That(activityComment, Does.Not.Contain("Candidate typed answer"));
        activityService.Verify(x => x.InsertActivityAsync(
            customer,
            "AIInterview.Runtime.NetworkRequestFailed",
            It.IsAny<string>(),
            It.IsAny<BaseEntity>()), Times.Once);
        _nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview runtime client request failure",
            It.Is<string>(message => message == activityComment),
            customer), Times.Once);
    }

    [Test]
    public async Task RuntimeClientEvent_ExpiredActiveSession_IgnoresEventWithoutRotatingToken()
    {
        var originalToken = "expired-client-event-token";
        var originalExpiry = DateTime.UtcNow.AddMinutes(-3);
        var session = new InterviewSession
        {
            Id = 83,
            CustomerId = 18,
            ProductId = 40,
            Token = originalToken,
            IsActive = true,
            TokenExpiryUtc = originalExpiry
        };
        var customer = new Customer { Id = session.CustomerId, Email = "candidate@example.com" };
        var activityService = new Mock<ICustomerActivityService>();

        _sessionService.Setup(x => x.GetSessionByTokenAsync(originalToken)).ReturnsAsync(session);
        _customerService.Setup(x => x.GetCustomerByIdAsync(session.CustomerId)).ReturnsAsync(customer);

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            nopLogger: _nopLogger.Object,
            customerActivityService: activityService.Object);

        var result = await controller.RuntimeClientEvent(
            originalToken,
            "network-request-failed",
            "prepare",
            400,
            null,
            "http-status",
            55);

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        Assert.That(GetJsonValue<bool>(json, "success"), Is.False);
        Assert.That(GetJsonValue<string>(json, "message"), Is.EqualTo("Runtime client event ignored for invalid session."));
        Assert.That(json.Value.GetType().GetProperty("newToken"), Is.Null);
        Assert.That(json.Value.GetType().GetProperty("tokenExpiryUtc"), Is.Null);
        Assert.That(session.Token, Is.EqualTo(originalToken));
        Assert.That(session.TokenExpiryUtc, Is.EqualTo(originalExpiry));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        activityService.Verify(x => x.InsertActivityAsync(
            It.IsAny<Customer>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<BaseEntity>()), Times.Never);
        _nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview runtime client request failure",
            It.Is<string>(message =>
                message.Contains("Event=RuntimeClientRequestFailed") &&
                message.Contains("Token=expire...") &&
                message.Contains("ReasonCode=token-expired") &&
                message.Contains("Request=prepare") &&
                message.Contains("StatusCode=400") &&
                message.Contains("FailureKind=http-status") &&
                message.Contains("Message=Runtime request returned HTTP 400.") &&
                !message.Contains(originalToken)),
            It.IsAny<Customer>()), Times.Once);
    }

    [Test]
    public async Task RuntimeClientEvent_InvalidStageTiming_UsesIgnoredDiagnosticWithoutNetworkFailureLog()
    {
        var token = "raw-stage-token-secret";
        _sessionService.Setup(x => x.GetSessionByTokenAsync(token)).ReturnsAsync((InterviewSession)null);
        var activityService = new Mock<ICustomerActivityService>();

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            nopLogger: _nopLogger.Object,
            customerActivityService: activityService.Object);

        var result = await controller.RuntimeClientEvent(
            token,
            "stage-timing",
            "prepare-response",
            null,
            "Candidate answer must not be logged",
            "http-status",
            88,
            true);

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        Assert.That(GetJsonValue<bool>(json, "success"), Is.False);
        Assert.That(GetJsonValue<string>(json, "message"), Is.EqualTo("Runtime client event ignored for invalid session."));
        activityService.Verify(x => x.InsertActivityAsync(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BaseEntity>()), Times.Never);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        _nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Information,
            "AI Interview runtime client stage timing ignored",
            It.Is<string>(message =>
                message.Contains("Token=raw-st...") &&
                message.Contains("Stage=prepare-response") &&
                message.Contains("ElapsedMs=88") &&
                message.Contains("ReasonCode=session-not-found") &&
                !message.Contains("Request=") &&
                !message.Contains("StatusCode=") &&
                !message.Contains("FailureKind=") &&
                !message.Contains(token) &&
                !message.Contains("Candidate answer")),
            It.IsAny<Customer>()), Times.Once);
        _nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview runtime client request failure",
            It.IsAny<string>(),
            It.IsAny<Customer>()), Times.Never);
    }

    [Test]
    public async Task RuntimeClientEvent_UploadRecording413_WritesActionableSizeLimitMessage()
    {
        var session = new InterviewSession
        {
            Id = 79,
            CustomerId = 14,
            ProductId = 36,
            Token = "upload-event-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        var customer = new Customer { Id = session.CustomerId, Email = "candidate@example.com" };
        var activityService = new Mock<ICustomerActivityService>();
        string activityComment = null;

        _sessionService.Setup(x => x.GetSessionByTokenAsync("upload-event-token")).ReturnsAsync(session);
        _customerService.Setup(x => x.GetCustomerByIdAsync(session.CustomerId)).ReturnsAsync(customer);
        activityService.Setup(x => x.InsertActivityAsync(
                customer,
                "AIInterview.Runtime.NetworkRequestFailed",
                It.IsAny<string>(),
                It.IsAny<BaseEntity>()))
            .Callback<Customer, string, string, BaseEntity>((_, _, comment, _) => activityComment = comment)
            .ReturnsAsync(new ActivityLog());

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            nopLogger: _nopLogger.Object,
            customerActivityService: activityService.Object);

        var result = await controller.RuntimeClientEvent(
            "upload-event-token",
            "network-request-failed",
            "upload-recording",
            413,
            null,
            "http-status",
            1200);

        Assert.That(result, Is.TypeOf<JsonResult>());
        Assert.That(activityComment, Does.Contain("Request=upload-recording"));
        Assert.That(activityComment, Does.Contain("StatusCode=413"));
        Assert.That(activityComment, Does.Contain("Recording upload exceeded the configured request limit or an upstream host proxy size limit."));
    }

    [Test]
    public async Task RuntimeClientEvent_StageTiming_NormalizesStageAndPersistsTrueSuccess()
    {
        var session = new InterviewSession
        {
            Id = 80,
            CustomerId = 15,
            ProductId = 37,
            Token = "stage-event-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        var customer = new Customer { Id = session.CustomerId, Email = "candidate@example.com" };
        var activityService = new Mock<ICustomerActivityService>();
        var activityComments = new List<string>();

        _sessionService.Setup(x => x.GetSessionByTokenAsync("stage-event-token")).ReturnsAsync(session);
        _customerService.Setup(x => x.GetCustomerByIdAsync(session.CustomerId)).ReturnsAsync(customer);
        activityService.Setup(x => x.InsertActivityAsync(
                customer,
                "AIInterview.Runtime.ClientStageTiming",
                It.IsAny<string>(),
                It.IsAny<BaseEntity>()))
            .Callback<Customer, string, string, BaseEntity>((_, _, comment, _) => activityComments.Add(comment))
            .ReturnsAsync(new ActivityLog());

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            nopLogger: _nopLogger.Object,
            customerActivityService: activityService.Object);

        var accepted = await controller.RuntimeClientEvent("stage-event-token", "stage-timing", "begin-response", null, null, null, 1234, true);
        var unknown = await controller.RuntimeClientEvent("stage-event-token", "stage-timing", "resume-text-secret", null, null, null, 99, false);

        Assert.That(accepted, Is.TypeOf<JsonResult>());
        Assert.That(unknown, Is.TypeOf<JsonResult>());
        Assert.That(activityComments, Has.Count.EqualTo(2));
        Assert.That(activityComments[0], Does.Contain("Stage=begin-response"));
        Assert.That(activityComments[0], Does.Contain("ElapsedMs=1234"));
        Assert.That(activityComments[0], Does.Contain("Success=true"));
        Assert.That(activityComments[1], Does.Contain("Stage=unknown"));
        Assert.That(activityComments[1], Does.Contain("ElapsedMs=99"));
        Assert.That(activityComments[1], Does.Contain("Success=false"));
        Assert.That(activityComments[1], Does.Not.Contain("resume-text-secret"));
    }

    [TestCase("prepare-response")]
    [TestCase("begin-response")]
    [TestCase("first-question-rendered")]
    [TestCase("speech-token-ready")]
    [TestCase("tts-started")]
    [TestCase("tts-completed")]
    [TestCase("recording-started")]
    public async Task RuntimeClientEvent_StageTiming_AcceptsOnlyRuntimeStages(string stageName)
    {
        var session = new InterviewSession
        {
            Id = 81,
            CustomerId = 16,
            ProductId = 38,
            Token = "stage-allow-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        var customer = new Customer { Id = session.CustomerId, Email = "candidate@example.com" };
        var activityService = new Mock<ICustomerActivityService>();
        string activityComment = null;

        _sessionService.Setup(x => x.GetSessionByTokenAsync("stage-allow-token")).ReturnsAsync(session);
        _customerService.Setup(x => x.GetCustomerByIdAsync(session.CustomerId)).ReturnsAsync(customer);
        activityService.Setup(x => x.InsertActivityAsync(
                customer,
                "AIInterview.Runtime.ClientStageTiming",
                It.IsAny<string>(),
                It.IsAny<BaseEntity>()))
            .Callback<Customer, string, string, BaseEntity>((_, _, comment, _) => activityComment = comment)
            .ReturnsAsync(new ActivityLog());

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            nopLogger: _nopLogger.Object,
            customerActivityService: activityService.Object);

        await controller.RuntimeClientEvent("stage-allow-token", "stage-timing", stageName, null, null, null, 10, true);

        Assert.That(activityComment, Does.Contain($"Stage={stageName}"));
        Assert.That(activityComment, Does.Contain("Success=true"));
    }

    [TestCase("feedback")]
    [TestCase("submit-answer")]
    [TestCase("upload-recording")]
    [TestCase("arbitrary-stage")]
    [TestCase("token=super-secret-stage")]
    public async Task RuntimeClientEvent_StageTiming_RejectsNonStageNamesWithoutRawValue(string rawStageName)
    {
        var session = new InterviewSession
        {
            Id = 82,
            CustomerId = 17,
            ProductId = 39,
            Token = "stage-deny-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        var customer = new Customer { Id = session.CustomerId, Email = "candidate@example.com" };
        var activityService = new Mock<ICustomerActivityService>();
        string activityComment = null;

        _sessionService.Setup(x => x.GetSessionByTokenAsync("stage-deny-token")).ReturnsAsync(session);
        _customerService.Setup(x => x.GetCustomerByIdAsync(session.CustomerId)).ReturnsAsync(customer);
        activityService.Setup(x => x.InsertActivityAsync(
                customer,
                "AIInterview.Runtime.ClientStageTiming",
                It.IsAny<string>(),
                It.IsAny<BaseEntity>()))
            .Callback<Customer, string, string, BaseEntity>((_, _, comment, _) => activityComment = comment)
            .ReturnsAsync(new ActivityLog());

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            nopLogger: _nopLogger.Object,
            customerActivityService: activityService.Object);

        await controller.RuntimeClientEvent("stage-deny-token", "stage-timing", rawStageName, null, null, null, 11, false);

        Assert.That(activityComment, Does.Contain("Stage=unknown"));
        Assert.That(activityComment, Does.Contain("Success=false"));
        Assert.That(activityComment, Does.Not.Contain(rawStageName));
        Assert.That(activityComment, Does.Not.Contain("super-secret-stage"));
    }

    [Test]
    public async Task RuntimeClientEvent_InvalidToken_IsHandledSafely()
    {
        _sessionService.Setup(x => x.GetSessionByTokenAsync("raw-token-secret")).ReturnsAsync((InterviewSession)null);
        var activityService = new Mock<ICustomerActivityService>();

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            nopLogger: _nopLogger.Object,
            customerActivityService: activityService.Object);

        var result = await controller.RuntimeClientEvent(
            "raw-token-secret",
            "network-request-failed",
            "submit-answer?token=raw-token-secret",
            0,
            "Candidate typed answer should not be logged",
            "fetch-exception",
            25);

        Assert.That(result, Is.TypeOf<JsonResult>());
        var success = ((JsonResult)result).Value.GetType().GetProperty("success")?.GetValue(((JsonResult)result).Value, null);
        Assert.That(success, Is.EqualTo(false));
        activityService.Verify(x => x.InsertActivityAsync(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BaseEntity>()), Times.Never);
        _nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview runtime client request failure",
            It.Is<string>(message =>
                message.Contains("Token=raw-to...") &&
                message.Contains("Request=unknown") &&
                message.Contains("FailureKind=fetch-exception") &&
                message.Contains("Unable to reach the interview service.") &&
                !message.Contains("raw-token-secret") &&
                !message.Contains("Candidate typed answer")),
            It.IsAny<Customer>()), Times.Once);
    }

    [Test]
    public async Task Runtime_SpeechToken_ExpiredOrInactive_ReturnsSafeJson()
    {
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired")).ReturnsAsync(new InterviewSession
        {
            Token = "expired",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        _sessionService.Setup(x => x.GetSessionByTokenAsync("inactive")).ReturnsAsync(new InterviewSession
        {
            Token = "inactive",
            IsActive = false,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object,
            null,
            _nopLogger.Object);

        var expired = await controller.SpeechToken("expired");
        var inactive = await controller.SpeechToken("inactive");

        var expiredValue = ((JsonResult)expired).Value;
        var expiredError = expiredValue.GetType().GetProperty("error").GetValue(expiredValue, null)?.ToString();
        var expiredMessage = expiredValue.GetType().GetProperty("message").GetValue(expiredValue, null)?.ToString();
        var serializedExpired = System.Text.Json.JsonSerializer.Serialize(expiredValue);

        Assert.That(expiredError, Is.EqualTo("Invalid or expired session token."));
        Assert.That(expiredMessage, Is.EqualTo("Invalid or expired session token."));
        Assert.That(serializedExpired, Does.Not.Contain("do-not-leak"));
        Assert.That(serializedExpired, Does.Not.Contain("secret.example"));
        Assert.That(serializedExpired, Does.Not.Contain("StackTrace"));

        Assert.That(((JsonResult)inactive).Value.GetType().GetProperty("error").GetValue(((JsonResult)inactive).Value, null), Is.EqualTo("Invalid or expired session token."));
        _interviewRuntimeService.Verify(x => x.GetSpeechTokenAsync(It.IsAny<string>()), Times.Never);
        _nopLogger.Verify(x => x.InsertLogAsync(
            It.IsAny<LogLevel>(),
            "AI Interview speech token failure",
            It.IsAny<string>(),
            It.IsAny<Customer>()), Times.Never);
    }

    [Test]
    public async Task Admin_TestAzureOpenAiConnection_Success_ReturnsConciseJson()
    {
        var settings = new AIInterviewSettings
        {
            AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
            AzureOpenAiApiKey = "secret-key",
            AzureOpenAiDeploymentOrModel = "deployment"
        };
        _settingService.Setup(service => service.LoadSettingAsync<AIInterviewSettings>(0))
            .ReturnsAsync(settings);
        var controller = CreateAiInterviewAdminController(settings, new AzureOpenAiChatCompletionResult
        {
            Success = true,
            Content = "{\"ok\":true}",
            Endpoint = "https://example.openai.azure.com/",
            EndpointHost = "example.openai.azure.com",
            DeploymentOrModel = "deployment"
        });

        var result = (JsonResult)await controller.TestAzureOpenAiConnection();

        Assert.That(GetJsonValue<bool>(result, "success"), Is.True);
        Assert.That(GetJsonValue<string>(result, "message"), Is.EqualTo("Azure OpenAI connection succeeded."));
    }

    [Test]
    public async Task Admin_TestAzureOpenAiConnection_ConfigIncomplete_ReturnsFailureWithoutAdapterCall()
    {
        _workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(new Customer { Id = 77, Email = "admin@example.com" });
        var controller = CreateAiInterviewAdminController(new AIInterviewSettings
        {
            AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
            AzureOpenAiDeploymentOrModel = "deployment"
        }, new AzureOpenAiChatCompletionResult { Success = true });

        var result = (JsonResult)await controller.TestAzureOpenAiConnection();

        Assert.That(GetJsonValue<bool>(result, "success"), Is.False);
        Assert.That(GetJsonValue<string>(result, "message"), Is.EqualTo("Azure OpenAI settings are incomplete. Save endpoint, API key, and deployment/model first."));
        _nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure test connection configuration invalid",
            It.Is<string>(message =>
                message.Contains("Operation=llm-test-connection") &&
                message.Contains("FailureKind=azure-openai-configuration-incomplete") &&
                message.Contains("EndpointHost=example.openai.azure.com") &&
                message.Contains("Deployment=deployment") &&
                message.Contains("Reason=configuration incomplete")),
            It.Is<Customer>(customer => customer.Id == 77)), Times.Once);
    }

    [Test]
    public async Task Admin_TestAzureOpenAiConnection_OperationEndpointShape_PassesPreValidation()
    {
        var controller = CreateAiInterviewAdminController(new AIInterviewSettings
        {
            AzureOpenAiEndpointUrl = "https://example.cognitiveservices.azure.com/openai/responses?api-version=2025-04-01-preview",
            AzureOpenAiApiKey = "secret-key",
            AzureOpenAiDeploymentOrModel = "deployment"
        }, new AzureOpenAiChatCompletionResult { Success = true });

        var result = (JsonResult)await controller.TestAzureOpenAiConnection();

        Assert.That(GetJsonValue<bool>(result, "success"), Is.True);
        Assert.That(GetJsonValue<string>(result, "message"), Is.EqualTo("Azure OpenAI connection succeeded."));
    }

    [Test]
    public async Task Admin_TestAzureOpenAiConnection_UnsupportedEndpointHost_ReturnsFailFastMessage()
    {
        var controller = CreateAiInterviewAdminController(new AIInterviewSettings
        {
            AzureOpenAiEndpointUrl = "https://example.azurewebsites.net/",
            AzureOpenAiApiKey = "secret-key",
            AzureOpenAiDeploymentOrModel = "deployment"
        }, new AzureOpenAiChatCompletionResult { Success = true });

        var result = (JsonResult)await controller.TestAzureOpenAiConnection();

        Assert.That(GetJsonValue<bool>(result, "success"), Is.False);
        Assert.That(GetJsonValue<string>(result, "message"), Does.Contain("openai.azure.com").And.Contain("cognitiveservices.azure.com"));
        _nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure test connection configuration invalid",
            It.Is<string>(message =>
                message.Contains("Operation=llm-test-connection") &&
                message.Contains("FailureKind=azure-openai-configuration-invalid") &&
                message.Contains("EndpointHost=example.azurewebsites.net") &&
                message.Contains("Reason=Azure OpenAI endpoint host")),
            It.IsAny<Customer>()), Times.Once);
    }

    [Test]
    public void Gpt5RequestContract_UsesMaxCompletionTokensOnlyInActiveCallPaths()
    {
        var adapterText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Services", "AzureOpenAiChatCompletionAdapter.cs"));
        var runtimeClientText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Services", "InterviewRuntimeService.cs"));
        var resumePlanningText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Services", "InterviewAiClient.ResumePlanning.cs"));
        var adminControllerText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Controllers", "AIInterviewAdminController.cs"));

        Assert.That(adapterText, Does.Contain("[\"max_completion_tokens\"] = request?.MaxCompletionTokens ?? 0"));
        Assert.That(adapterText, Does.Not.Contain("GetChatClient"));
        Assert.That(adapterText, Does.Not.Contain("ChatCompletionOptions"));
        Assert.That(adapterText, Does.Not.Contain("CompleteChatAsync(messages"));
        Assert.That(adapterText, Does.Not.Contain("MaxOutputTokenCount"));
        Assert.That(runtimeClientText, Does.Contain("private const int GenerateMaxCompletionTokens = 400"));
        Assert.That(runtimeClientText, Does.Contain("private const int ScoreMaxCompletionTokens = 1200"));
        Assert.That(runtimeClientText, Does.Contain("private const int ScoreLengthRetryMaxCompletionTokens = 2000"));
        Assert.That(runtimeClientText, Does.Contain("MaxCompletionTokens = maxCompletionTokens"));
        Assert.That(resumePlanningText, Does.Contain("MaxCompletionTokens = maxTokens"));
        Assert.That(adminControllerText, Does.Contain("MaxCompletionTokens = 32"));
        Assert.That(adapterText + runtimeClientText + resumePlanningText + adminControllerText, Does.Not.Contain("MaxTokens ="));
        Assert.That(adapterText + runtimeClientText + resumePlanningText + adminControllerText, Does.Not.Contain("Temperature ="));
    }

    [Test]
    public void RuntimeClientEvent_RouteAndDiagnosticsStrings_RemainMappedAndSafe()
    {
        var routeProviderText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Infrastructure", "RouteProvider.cs"));
        var controllerText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Controllers", "MockAiInterviewController.cs"));
        var runtimeViewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));
        var startupText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Infrastructure", "PluginNopStartup.cs"));

        Assert.That(routeProviderText, Does.Contain("pattern: \"mockaiinterview/runtime-client-event\""));
        Assert.That(routeProviderText, Does.Contain("action = \"RuntimeClientEvent\""));
        Assert.That(controllerText, Does.Contain("model.ClientSettings.RuntimeClientEventUrl = Url?.RouteUrl(AIInterviewDefaults.MockRuntimeClientEventRouteName);"));
        Assert.That(controllerText, Does.Contain("[RequestSizeLimit(MaxRecordingUploadBytes)]"));
        Assert.That(controllerText, Does.Contain("[RequestFormLimits(MultipartBodyLengthLimit = MaxRecordingUploadBytes)]"));
        Assert.That(startupText, Does.Contain("options.MultipartBodyLengthLimit = Math.Max(options.MultipartBodyLengthLimit, MockAiInterviewController.MaxRecordingUploadBytes);"));
        Assert.That(runtimeViewText, Does.Contain("event.requestName === 'runtime-client-event'"));
        Assert.That(runtimeViewText, Does.Contain("failureKind: 'fetch-exception'"));
    }

    [Test]
    public async Task Admin_TestAzureOpenAiConnection_AdapterFailure_ReturnsSafeFailure()
    {
        _workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(new Customer { Id = 88, Email = "admin@example.com" });
        var settings = new AIInterviewSettings
        {
            AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
            AzureOpenAiApiKey = "secret-key",
            AzureOpenAiDeploymentOrModel = "deployment"
        };
        var controller = CreateAiInterviewAdminController(settings, new AzureOpenAiChatCompletionResult
        {
            Success = false,
            FailureKind = "azure-openai-http-failure",
            Reason = "http failure",
            StatusCode = 401,
            ErrorCode = "Unauthorized",
            ErrorMessage = "api-key=secret-key failed",
            ResponseBody = "api-key=secret-key failed",
            Endpoint = "https://example.openai.azure.com/",
            EndpointHost = "example.openai.azure.com",
            DeploymentOrModel = "deployment"
        });

        var result = (JsonResult)await controller.TestAzureOpenAiConnection();
        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Value);

        Assert.That(GetJsonValue<bool>(result, "success"), Is.False);
        Assert.That(GetJsonValue<string>(result, "message"), Does.Contain("Azure OpenAI connection failed."));
        Assert.That(GetJsonValue<string>(result, "message"), Does.Contain("HTTP 401"));
        Assert.That(serialized, Does.Not.Contain("secret-key"));
        Assert.That(serialized, Does.Not.Contain("api-key=secret-key"));
        _nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure test connection failed",
            It.Is<string>(message =>
                message.Contains("Operation=llm-test-connection") &&
                message.Contains("FailureKind=azure-openai-http-failure") &&
                message.Contains("HttpStatus=401") &&
                message.Contains("EndpointHost=example.openai.azure.com") &&
                message.Contains("Deployment=deployment") &&
                message.Contains("ErrorCode=Unauthorized") &&
                !message.Contains("secret-key") &&
                !message.Contains("api-key=secret-key")),
            It.Is<Customer>(customer => customer.Id == 88)), Times.Once);
    }

    [Test]
    public async Task Admin_TestAzureOpenAiConnection_AdapterException_LogsWarningSafely()
    {
        var settings = new AIInterviewSettings
        {
            AzureOpenAiEndpointUrl = "https://example.openai.azure.com/openai/responses?api-version=2025-04-01-preview",
            AzureOpenAiApiKey = "secret-key",
            AzureOpenAiDeploymentOrModel = "deployment"
        };
        var controller = CreateAiInterviewAdminController(
            settings,
            adapter: new ThrowingAzureOpenAiChatCompletionAdapter(new InvalidOperationException("authorization: secret-token failed; sig=secret-signature")));

        var result = (JsonResult)await controller.TestAzureOpenAiConnection();

        Assert.That(GetJsonValue<bool>(result, "success"), Is.False);
        _nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure test connection failed",
            It.Is<string>(message =>
                message.Contains("Operation=llm-test-connection") &&
                message.Contains("FailureKind=azure-openai-exception") &&
                message.Contains("EndpointHost=example.openai.azure.com") &&
                message.Contains("Deployment=deployment") &&
                message.Contains("ExceptionType=InvalidOperationException") &&
                message.Contains("authorization=<redacted>") &&
                message.Contains("sig=<redacted>") &&
                !message.Contains("secret-token") &&
                !message.Contains("secret-signature")),
            It.IsAny<Customer>()), Times.Once);
    }

    [TestCase("api-key=secret-key", "secret-key")]
    [TestCase("authorization: secret-auth", "secret-auth")]
    [TestCase("bearer=secret-bearer", "secret-bearer")]
    [TestCase("https://example.test/path?sig=secret-signature", "secret-signature")]
    [TestCase("client_secret=secret-client", "secret-client")]
    public async Task Admin_TestAzureOpenAiConnection_VariedAdapterFailures_DoNotExposeSecrets(string secretBearingReason, string secret)
    {
        var settings = new AIInterviewSettings
        {
            AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
            AzureOpenAiApiKey = "settings-secret-key",
            AzureOpenAiDeploymentOrModel = "deployment"
        };
        var controller = CreateAiInterviewAdminController(settings, new AzureOpenAiChatCompletionResult
        {
            Success = false,
            FailureKind = "azure-openai-exception",
            Reason = secretBearingReason,
            StatusCode = 400,
            ErrorCode = secretBearingReason,
            ErrorMessage = $"detail {secretBearingReason}",
            ResponseBody = $"detail {secretBearingReason}",
            Endpoint = "https://example.openai.azure.com/",
            EndpointHost = "example.openai.azure.com",
            DeploymentOrModel = "deployment"
        });

        var result = (JsonResult)await controller.TestAzureOpenAiConnection();
        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Value);

        Assert.That(GetJsonValue<bool>(result, "success"), Is.False);
        Assert.That(serialized, Does.Not.Contain(secret));
        Assert.That(serialized, Does.Not.Contain("settings-secret-key"));
        Assert.That(serialized, Does.Not.Contain($"detail {secretBearingReason}"));
    }

    [Test]
    public async Task InterviewAiClient_GenerateQuestion_AdapterFailure_ReturnsUnavailableAndLogsSafeDetails()
    {
        var nopLogger = new Mock<ILogger>();
        nopLogger.Setup(logger => logger.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret-key",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            nopLogger: nopLogger.Object,
            azureOpenAiChatCompletionAdapter: new FakeAzureOpenAiChatCompletionAdapter(new AzureOpenAiChatCompletionResult
            {
                Success = false,
                FailureKind = "azure-openai-http-failure",
                Reason = "http failure",
                StatusCode = 429,
                ReasonPhrase = "Too Many Requests",
                ResponseBody = "{\"error\":{\"code\":\"rate_limit\",\"message\":\"api-key=secret-key throttled\"}}",
                Endpoint = "https://example.openai.azure.com/",
                EndpointHost = "example.openai.azure.com",
                DeploymentOrModel = "deployment"
            }));

        var response = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            Prompt = "Prompt"
        });

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorMessage, Does.Contain("AI service unavailable"));
        Assert.That(response.ErrorMessage, Does.Contain("api-key=<redacted>"));
        Assert.That(response.ErrorMessage, Does.Not.Contain("secret-key"));
        nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure OpenAI HTTP failure",
            It.Is<string>(message =>
                message.Contains("FailureKind=azure-openai-http-failure") &&
                message.Contains("EndpointHost=example.openai.azure.com") &&
                !message.Contains("secret-key")),
            null), Times.Once);
    }

    [Test]
    public async Task InterviewAiClient_GenerateQuestion_EmptyContentContractFailure_LogsDeploymentFromAdapterResult()
    {
        var nopLogger = new Mock<ILogger>();
        nopLogger.Setup(logger => logger.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);
        var responseBody = "{\"id\":\"resp_empty\",\"model\":\"gpt-5\",\"choices\":[{\"message\":{\"content\":\"\"}}]}";
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret-key",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            nopLogger: nopLogger.Object,
            azureOpenAiChatCompletionAdapter: new FakeAzureOpenAiChatCompletionAdapter(new AzureOpenAiChatCompletionResult
            {
                Success = true,
                Content = string.Empty,
                ResponseBody = responseBody,
                Endpoint = "https://example.openai.azure.com/",
                EndpointHost = "example.openai.azure.com",
                DeploymentOrModel = "deployment"
            }));

        var response = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            Prompt = "Prompt"
        });

        Assert.That(response.Success, Is.False);
        nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure OpenAI contract failure",
            It.Is<string>(message =>
                message.Contains("FailureKind=azure-openai-contract-failure") &&
                message.Contains("Reason=empty response content") &&
                message.Contains("EndpointHost=example.openai.azure.com") &&
                message.Contains("Deployment=deployment") &&
                !message.Contains("Deployment=<empty>") &&
                !message.Contains("ResponseLength=0") &&
                message.Contains("resp_empty")),
            null), Times.Once);
    }

    [Test]
    public async Task InterviewAiClient_ScoreAnswer_LengthTruncatedEmptyContent_RetriesWithHigherTokenBudgetAndSucceeds()
    {
        var nopLogger = new Mock<ILogger>();
        nopLogger.Setup(logger => logger.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);
        var adapter = new SequenceAzureOpenAiChatCompletionAdapter(
            new AzureOpenAiChatCompletionResult
            {
                Success = true,
                Content = string.Empty,
                ResponseBody = "{\"id\":\"resp_length\",\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"\"}}]}",
                Endpoint = "https://example.openai.azure.com/",
                EndpointHost = "example.openai.azure.com",
                DeploymentOrModel = "gpt-5-mini",
                FinishReason = "length",
                IsLengthTruncated = true
            },
            new AzureOpenAiChatCompletionResult
            {
                Success = true,
                Content = "{\"technicalScore\":80,\"communicationScore\":75,\"professionalismScore\":85,\"positiveAttitudeScore\":90,\"score\":82.5,\"feedback\":\"Good answer.\",\"complete\":false,\"rubricJson\":{\"technicalScore\":80,\"communicationScore\":75,\"professionalismScore\":85,\"positiveAttitudeScore\":90,\"score\":82.5}}",
                ResponseBody = "{\"id\":\"resp_retry\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"valid\"}}]}",
                Endpoint = "https://example.openai.azure.com/",
                EndpointHost = "example.openai.azure.com",
                DeploymentOrModel = "gpt-5-mini",
                FinishReason = "stop"
            });
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-5-mini"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            nopLogger: nopLogger.Object,
            azureOpenAiChatCompletionAdapter: adapter);

        var response = await client.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Question = "Describe a production incident.",
            Answer = "I identified the failed dependency, rolled traffic back, and coordinated the postmortem."
        });

        Assert.That(response.Success, Is.True);
        Assert.That(response.Score, Is.EqualTo(82.5m));
        Assert.That(adapter.Requests.Count, Is.EqualTo(2));
        Assert.That(adapter.Requests[0].MaxCompletionTokens, Is.EqualTo(1200));
        Assert.That(adapter.Requests[1].MaxCompletionTokens, Is.EqualTo(2000));
        nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Information,
            "AI Interview Azure OpenAI truncation retry initiated",
            It.Is<string>(message =>
                message.Contains("Outcome=retry initiated due to truncation") &&
                message.Contains("Reason=empty response content (finish_reason=length)") &&
                message.Contains("Deployment=gpt-5-mini") &&
                message.Contains("MaxCompletionTokens=1200") &&
                message.Contains("RetryMaxCompletionTokens=2000")),
            null), Times.Once);
        nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Information,
            "AI Interview Azure OpenAI truncation retry recovered",
            It.Is<string>(message =>
                message.Contains("Outcome=retry recovered") &&
                message.Contains("Deployment=gpt-5-mini") &&
                message.Contains("MaxCompletionTokens=2000")),
            null), Times.Once);
        nopLogger.Verify(logger => logger.InsertLogAsync(
            It.IsAny<LogLevel>(),
            "AI Interview Azure OpenAI contract failure",
            It.IsAny<string>(),
            It.IsAny<Customer>()), Times.Never);
    }

    [Test]
    public async Task InterviewAiClient_ScoreAnswer_LengthTruncatedRetryExhausted_LogsExplicitContractFailure()
    {
        var nopLogger = new Mock<ILogger>();
        nopLogger.Setup(logger => logger.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);
        var adapter = new SequenceAzureOpenAiChatCompletionAdapter(
            new AzureOpenAiChatCompletionResult
            {
                Success = true,
                Content = string.Empty,
                ResponseBody = "{\"id\":\"resp_length_1\",\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"\"}}]}",
                Endpoint = "https://example.openai.azure.com/",
                EndpointHost = "example.openai.azure.com",
                DeploymentOrModel = "gpt-5-mini",
                FinishReason = "length",
                IsLengthTruncated = true
            },
            new AzureOpenAiChatCompletionResult
            {
                Success = true,
                Content = string.Empty,
                ResponseBody = "{\"id\":\"resp_length_2\",\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"\"}}]}",
                Endpoint = "https://example.openai.azure.com/",
                EndpointHost = "example.openai.azure.com",
                DeploymentOrModel = "gpt-5-mini",
                FinishReason = "length",
                IsLengthTruncated = true
            });
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-5-mini"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            nopLogger: nopLogger.Object,
            azureOpenAiChatCompletionAdapter: adapter);

        var response = await client.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Question = "Describe a production incident.",
            Answer = "I identified the failed dependency, rolled traffic back, and coordinated the postmortem."
        });

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorMessage, Does.Contain("empty response content (finish_reason=length)"));
        Assert.That(adapter.Requests.Count, Is.EqualTo(2));
        Assert.That(adapter.Requests[0].MaxCompletionTokens, Is.EqualTo(1200));
        Assert.That(adapter.Requests[1].MaxCompletionTokens, Is.EqualTo(2000));
        nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure OpenAI truncation retry exhausted",
            It.Is<string>(message =>
                message.Contains("Outcome=retry exhausted") &&
                message.Contains("Reason=empty response content (finish_reason=length)") &&
                message.Contains("Deployment=gpt-5-mini")),
            null), Times.Once);
        nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure OpenAI contract failure",
            It.Is<string>(message =>
                message.Contains("Reason=empty response content (finish_reason=length)") &&
                message.Contains("Deployment=gpt-5-mini") &&
                !message.Contains("Deployment=<empty>") &&
                !message.Contains("ResponseLength=0")),
            null), Times.Once);
    }

    [TestCase("length")]
    [TestCase("max_output_tokens")]
    public async Task InterviewAiClient_QuestionPlan_TruncatedEmptyContent_RetriesWithHigherTokenBudgetAndSucceeds(string finishReason)
    {
        var nopLogger = new Mock<ILogger>();
        nopLogger.Setup(logger => logger.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);
        var adapter = new SequenceAzureOpenAiChatCompletionAdapter(
            new AzureOpenAiChatCompletionResult
            {
                Success = true,
                Content = string.Empty,
                ResponseBody = $"{{\"id\":\"resp_{finishReason}\",\"choices\":[{{\"finish_reason\":\"{finishReason}\",\"message\":{{\"content\":\"\"}}}}]}}",
                Endpoint = "https://example.openai.azure.com/",
                EndpointHost = "example.openai.azure.com",
                DeploymentOrModel = "gpt-5-mini",
                FinishReason = finishReason,
                IsLengthTruncated = true
            },
            new AzureOpenAiChatCompletionResult
            {
                Success = true,
                Content = """
{
  "questions": [
    {"sequenceNumber":1,"category":"skill","question":"How do you design resilient APIs?","resumeEvidence":"API work","expectedSignals":["resilience"],"rubric":{"technical":"Depth","communication":"Clear","professionalism":"Practical","positiveAttitude":"Constructive"}},
    {"sequenceNumber":2,"category":"behavioral","question":"How did you handle a production incident?","resumeEvidence":"Incident work","expectedSignals":["ownership"],"rubric":{"technical":"Diagnosis","communication":"Coordination","professionalism":"Accountability","positiveAttitude":"Calm"}}
  ]
}
""",
                ResponseBody = "{\"id\":\"resp_retry\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"valid\"}}]}",
                Endpoint = "https://example.openai.azure.com/",
                EndpointHost = "example.openai.azure.com",
                DeploymentOrModel = "gpt-5-mini",
                FinishReason = "stop"
            });
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-5-mini"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            nopLogger: nopLogger.Object,
            azureOpenAiChatCompletionAdapter: adapter);

        var response = await client.GenerateQuestionPlanAsync(new AIInterviewQuestionPlanRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            QuestionCount = 2,
            TotalQuestionCount = 2,
            Prompt = "Ask practical questions"
        });

        Assert.That(response.Success, Is.True);
        Assert.That(response.Questions.Count, Is.EqualTo(2));
        Assert.That(adapter.Requests.Count, Is.EqualTo(2));
        Assert.That(adapter.Requests[0].Mode, Is.EqualTo("question-plan"));
        Assert.That(adapter.Requests[0].MaxCompletionTokens, Is.EqualTo(2200));
        Assert.That(adapter.Requests[1].MaxCompletionTokens, Is.EqualTo(3000));
        Assert.That(adapter.Requests[1].SystemPrompt, Is.EqualTo(adapter.Requests[0].SystemPrompt));
        Assert.That(adapter.Requests[1].UserPrompt, Is.EqualTo(adapter.Requests[0].UserPrompt));
        nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Information,
            "AI Interview question plan truncation retry initiated",
            It.Is<string>(message =>
                message.Contains("Mode=question-plan") &&
                message.Contains("Operation=llm-question-plan") &&
                message.Contains("InitialMaxCompletionTokens=2200") &&
                message.Contains("RetryMaxCompletionTokens=3000") &&
                message.Contains($"FinishReason={finishReason}") &&
                message.Contains("Deployment=gpt-5-mini")),
            null), Times.Once);
        nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Information,
            "AI Interview question plan truncation retry recovered",
            It.Is<string>(message =>
                message.Contains("Mode=question-plan") &&
                message.Contains("Operation=llm-question-plan") &&
                message.Contains("InitialMaxCompletionTokens=2200") &&
                message.Contains("RetryMaxCompletionTokens=3000") &&
                message.Contains("Deployment=gpt-5-mini")),
            null), Times.Once);
        nopLogger.Verify(logger => logger.InsertLogAsync(
            It.IsAny<LogLevel>(),
            "AI Interview question plan contract failure",
            It.IsAny<string>(),
            It.IsAny<Customer>()), Times.Never);
    }

    [TestCase("length")]
    [TestCase("max_output_tokens")]
    public async Task InterviewAiClient_QuestionPlan_TruncatedRetryExhausted_LogsFinishReasonAndDeployment(string finishReason)
    {
        var nopLogger = new Mock<ILogger>();
        nopLogger.Setup(logger => logger.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);
        var adapter = new SequenceAzureOpenAiChatCompletionAdapter(
            new AzureOpenAiChatCompletionResult
            {
                Success = true,
                Content = string.Empty,
                ResponseBody = $"{{\"id\":\"resp_{finishReason}_1\",\"choices\":[{{\"finish_reason\":\"{finishReason}\",\"message\":{{\"content\":\"\"}}}}]}}",
                Endpoint = "https://example.openai.azure.com/",
                EndpointHost = "example.openai.azure.com",
                DeploymentOrModel = "gpt-5-mini",
                FinishReason = finishReason,
                IsLengthTruncated = true
            },
            new AzureOpenAiChatCompletionResult
            {
                Success = true,
                Content = string.Empty,
                ResponseBody = $"{{\"id\":\"resp_{finishReason}_2\",\"choices\":[{{\"finish_reason\":\"{finishReason}\",\"message\":{{\"content\":\"\"}}}}]}}",
                Endpoint = "https://example.openai.azure.com/",
                EndpointHost = "example.openai.azure.com",
                DeploymentOrModel = "gpt-5-mini",
                FinishReason = finishReason,
                IsLengthTruncated = true
            });
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-5-mini"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            nopLogger: nopLogger.Object,
            azureOpenAiChatCompletionAdapter: adapter);

        var response = await client.GenerateQuestionPlanAsync(new AIInterviewQuestionPlanRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            QuestionCount = 2,
            TotalQuestionCount = 2,
            Prompt = "Ask practical questions"
        });

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorMessage, Does.Contain($"empty response content (finish_reason={finishReason})"));
        Assert.That(adapter.Requests.Count, Is.EqualTo(2));
        nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview question plan truncation retry exhausted",
            It.Is<string>(message =>
                message.Contains("Mode=question-plan") &&
                message.Contains("Operation=llm-question-plan") &&
                message.Contains("InitialMaxCompletionTokens=2200") &&
                message.Contains("RetryMaxCompletionTokens=3000") &&
                message.Contains($"FinishReason={finishReason}") &&
                message.Contains("Deployment=gpt-5-mini")),
            null), Times.Once);
        nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview question plan contract failure",
            It.Is<string>(message =>
                message.Contains($"Reason=empty response content (finish_reason={finishReason})") &&
                message.Contains("Deployment=gpt-5-mini") &&
                !message.Contains("Deployment=<empty>") &&
                message.Contains($"resp_{finishReason}_2")),
            null), Times.Once);
    }

    [Test]
    public async Task InterviewAiClient_ScoreAnswer_AdapterFailure_ReturnsUnavailableAndDoesNotLeakSecret()
    {
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret-key",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            azureOpenAiChatCompletionAdapter: new FakeAzureOpenAiChatCompletionAdapter(new AzureOpenAiChatCompletionResult
            {
                Success = false,
                FailureKind = "azure-openai-exception",
                Reason = "RequestFailedException",
                ErrorMessage = "authorization=secret-key failed",
                ResponseBody = "authorization=secret-key failed",
                Endpoint = "https://example.openai.azure.com/",
                EndpointHost = "example.openai.azure.com",
                DeploymentOrModel = "deployment"
            }));

        var response = await client.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Question = "Q1",
            Answer = "A1"
        });

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorMessage, Does.Contain("AI service unavailable"));
        Assert.That(response.ErrorMessage, Does.Contain("authorization=<redacted>"));
        Assert.That(response.ErrorMessage, Does.Not.Contain("secret-key"));
    }

    [Test]
    public void AiService_View_Shows_AzureOpenAi_ConfigurationHints()
    {
        var viewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Admin", "AiService.cshtml"));

        Assert.That(viewText, Does.Contain("Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiEndpointUrl.Hint"));
        Assert.That(viewText, Does.Contain("Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiDeploymentOrModel.Hint"));
        Assert.That(viewText, Does.Contain("TestAzureOpenAiConnection"));
    }

    [Test]
    public async Task Admin_Invite_Validation_InvalidAttempts()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(1)).ReturnsAsync(new Customer { Id = 1, VendorId = 1 });
        _productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Product { Id = 10, VendorId = 1 });

        var ex = Assert.ThrowsAsync<NopException>(async () => await _inviteServiceImplementation.CreateInviteAsync(1, "test@test.com", 10, 0, null));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.InvalidAttempts"));
    }

    [Test]
    public async Task Admin_Invite_Validation_InvalidExpiry()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(1)).ReturnsAsync(new Customer { Id = 1, VendorId = 1 });
        _productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Product { Id = 10, VendorId = 1 });

        var ex = Assert.ThrowsAsync<NopException>(async () => await _inviteServiceImplementation.CreateInviteAsync(1, "test@test.com", 10, 1, DateTime.UtcNow.AddMinutes(-1)));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.InvalidExpiry"));
    }

    [Test]
    public void AdminController_Has_ConfigureAction()
    {
        var method = typeof(MockAiInterviewAdminController).GetMethod("Configure", new[] { typeof(AIInterviewConfigureModel) });
        Assert.That(method, Is.Not.Null);
    }

    [Test]
    public void MockConfigure_View_Uses_Localized_Informational_Admin_Layout()
    {
        var viewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterviewAdmin", "Configure.cshtml"));

        Assert.That(viewText, Does.Contain("aiinterview-admin-config-shell"));
        Assert.That(viewText, Does.Contain("aiinterview-admin-summary"));
        Assert.That(viewText, Does.Contain("aiinterview-admin-card"));
        Assert.That(viewText, Does.Contain("Plugins.Misc.AIInterview.Admin.MockConfigure.Subtitle"));
        Assert.That(viewText, Does.Contain("Plugins.Misc.AIInterview.Admin.MockConfigure.General.Summary"));
        Assert.That(viewText, Does.Contain("Plugins.Misc.AIInterview.Admin.MockConfigure.Service.Body"));
        Assert.That(viewText, Does.Contain("Plugins.Misc.AIInterview.Admin.MockConfigure.CreditPack.Body"));
        Assert.That(viewText, Does.Not.Contain("Mock Configuration Page"));
        Assert.That(viewText, Does.Not.Contain("Mock administration workspace"));
        Assert.That(viewText, Does.Not.Contain("Informational only"));
    }

    [Test]
    public void Admin_Polish_Views_Keep_Labeled_Action_And_Link_Buttons()
    {
        var applicantCredits = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Admin", "ApplicantCredits.cshtml"));
        var scoreboard = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Admin", "Scoreboard.cshtml"));

        Assert.That(applicantCredits, Does.Contain("class=\"btn btn-primary btn-search aiinterview-admin-action-button\""));
        Assert.That(applicantCredits, Does.Contain("aria-label=\"@T(\\\"Admin.Common.Search\\\")\"".Replace("\\\"", "\"")));
        Assert.That(applicantCredits, Does.Contain("title=\"@T(\\\"Plugins.Misc.AIInterview.Admin.Credits.TopUp\\\")\"".Replace("\\\"", "\"")));
        Assert.That(applicantCredits, Does.Contain("aiinterview-admin-link-button"));
        Assert.That(applicantCredits, Does.Contain("Plugins.Misc.AIInterview.Admin.Credits.Activity.ViewLedger"));

        Assert.That(scoreboard, Does.Contain("class=\"btn btn-secondary aiinterview-admin-link-button\""));
        Assert.That(scoreboard, Does.Contain("class=\"btn btn-primary btn-search aiinterview-admin-action-button\""));
        Assert.That(scoreboard, Does.Contain("title=\"@T(\\\"Plugins.Misc.AIInterview.Admin.Scoreboard.Filter\\\")\"".Replace("\\\"", "\"")));
        Assert.That(scoreboard, Does.Contain("Plugins.Misc.AIInterview.Admin.Scoreboard.Report"));
        Assert.That(scoreboard, Does.Contain("aiinterview-admin-link-button"));
    }

    [Test]
    public void MockAiInterviewController_Has_EmployerActions()
    {
        var createMethod = typeof(MockAiInterviewController).GetMethod("CreateInvite");
        var deactivateMethod = typeof(MockAiInterviewController).GetMethod("DeactivateInvite");
        Assert.That(createMethod, Is.Not.Null);
        Assert.That(deactivateMethod, Is.Not.Null);
    }

    [Test]
    public async Task Runtime_InvalidSession_ReturnsVisibleErrorView()
    {
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired"))
            .ReturnsAsync(new InterviewSession
            {
                Token = "expired",
                IsActive = false,
                TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
            });

        var result = await _runtimeController.Runtime("expired");

        Assert.That(result, Is.TypeOf<RedirectResult>());
        Assert.That(((RedirectResult)result).Url, Is.EqualTo("/"));
    }

    [Test]
    public async Task Runtime_ExpiredProductSession_RedirectsToProductUrlWithExpiredFlag()
    {
        var urlRecordService = new Mock<global::Nop.Services.Seo.IUrlRecordService>();
        urlRecordService.Setup(x => x.GetSeNameAsync(It.IsAny<Product>())).ReturnsAsync("sample-job");
        _productService.Setup(x => x.GetProductByIdAsync(42)).ReturnsAsync(new Product { Id = 42, Name = "Sample Job" });
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-product"))
            .ReturnsAsync(new InterviewSession
            {
                Token = "expired-product",
                ProductId = 42,
                IsActive = false,
                TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
            });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            null,
            null,
            urlRecordService.Object);

        var result = await controller.Runtime("expired-product");

        Assert.That(result, Is.TypeOf<RedirectResult>());
        Assert.That(((RedirectResult)result).Url, Is.EqualTo("/sample-job?interviewError=expired"));
    }

    [Test]
    public async Task Stop_PublishesCompletionOnlyOnce()
    {
        var session = new InterviewSession
        {
            Id = 11,
            Token = "valid",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("valid")).ReturnsAsync(session);
        _workContext.Setup(x => x.GetWorkingLanguageAsync())
            .ReturnsAsync(new Nop.Core.Domain.Localization.Language { Id = 2 });

        var firstResult = await _runtimeController.Stop("valid");
        var secondResult = await _runtimeController.Stop("valid");

        Assert.That(firstResult, Is.TypeOf<JsonResult>());
        Assert.That(secondResult, Is.TypeOf<JsonResult>());
        _eventPublisher.Verify(x => x.PublishAsync(It.Is<MockAiInterviewCompletedEvent>(message =>
            message.Session.Id == 11 && message.LanguageId == 2)), Times.Once);
    }

    private class TestRuntimeController : MockAiInterviewController
    {
        public TestRuntimeController(IInterviewSessionService sessionService, ILocalizationService localizationService, IWorkContext workContext, ISponsorInviteService inviteService, ICreditService creditService, ICustomerService customerService, IProductService productService, global::Nop.Services.Vendors.IVendorService vendorService, IApplicationService applicationService)
            : base(sessionService, localizationService, workContext, inviteService, creditService, customerService, productService, vendorService, applicationService) { }

        public Task TestApplyRuntimeClientSettingsAsync(InterviewRuntimeModel model, InterviewSession session)
        {
            return ApplyRuntimeClientSettingsAsync(model, session);
        }

        public async Task<IActionResult> TestFallback()
        {
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Missing", "Fallback text");
        }
    }

    private async Task<RuntimeClientSettingsModel> ApplyRuntimeClientSettingsForExpiryAsync(DateTime? tokenExpiryUtc)
    {
        var controller = new TestRuntimeController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object);
        var model = new InterviewRuntimeModel
        {
            ProductName = "Runtime Product",
            ClientSettings = new RuntimeClientSettingsModel()
        };
        var session = new InterviewSession
        {
            Id = 90,
            Token = "runtime-expiry-token",
            IsActive = true,
            TokenExpiryUtc = tokenExpiryUtc,
            QuestionCount = 5
        };

        await controller.TestApplyRuntimeClientSettingsAsync(model, session);

        return model.ClientSettings;
    }

    private static T GetJsonValue<T>(JsonResult json, string propertyName)
    {
        return (T)json.Value.GetType().GetProperty(propertyName).GetValue(json.Value, null);
    }

    private InterviewRuntimeService CreateRuntimeService(Mock<IInterviewTurnService> turnService, Mock<IAIInterviewClient> aiClient, AIInterviewSettings settings)
    {
        return new InterviewRuntimeService(
            _sessionService.Object,
            turnService.Object,
            aiClient.Object,
            _productService.Object,
            _customerService.Object,
            new Mock<IApplicationService>().Object,
            new Mock<IResumeProfileService>().Object,
            new Mock<IAzureUsageService>().Object,
            _localizationService.Object,
            settings,
            new MockAIInterviewSettings { UseMockResponses = false },
            new Mock<System.Net.Http.IHttpClientFactory>().Object,
            _workContext.Object,
            _eventPublisher.Object,
            _nopLogger.Object);
    }

    private static AIInterviewFinalScoringTurnResult BuildFinalScore(int sequenceNumber, decimal score)
    {
        return new AIInterviewFinalScoringTurnResult
        {
            SequenceNumber = sequenceNumber,
            TechnicalScore = score,
            CommunicationScore = score,
            ProfessionalismScore = score,
            PositiveAttitudeScore = score,
            Score = score,
            Feedback = $"Feedback {sequenceNumber}.",
            RubricJson = $"{{\"score\":{score}}}"
        };
    }

    private AIInterviewAdminController CreateAiInterviewAdminController(AIInterviewSettings settings, AzureOpenAiChatCompletionResult adapterResult = null, IAzureOpenAiChatCompletionAdapter adapter = null)
    {
        _settingService.Setup(service => service.LoadSettingAsync<AIInterviewSettings>(0))
            .ReturnsAsync(settings);
        _localizationService.Setup(service => service.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key);

        return new AIInterviewAdminController(
            _creditService.Object,
            _inviteService.Object,
            new Mock<IApplicationService>().Object,
            _sessionService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _localizationService.Object,
            _notificationService.Object,
            new Mock<IDateTimeHelper>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<AIInterviewAdminController>>().Object,
            _workContext.Object,
            _settingService.Object,
            new Mock<IRepository<Customer>>().Object,
            new Mock<IRepository<CreditWallet>>().Object,
            new Mock<IRepository<CreditLedgerEntry>>().Object,
            new Mock<IRepository<CreditPurchaseGrant>>().Object,
            settings,
            _mockAIInterviewSettings,
            azureOpenAiChatCompletionAdapter: adapter ?? new FakeAzureOpenAiChatCompletionAdapter(adapterResult),
            nopLogger: _nopLogger.Object);
    }

    [Test]
    public async Task Runtime_SubmitAnswer_Empty_ReturnsLocalizedJsonError()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        sessionService.Setup(x => x.GetSessionByTokenAsync("token")).ReturnsAsync(new InterviewSession
        {
            Token = "token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        var controller = new TestRuntimeController(sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object);
        var result = await controller.SubmitAnswer("token", "");
        var json = (JsonResult)result;

        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var message = json.Value.GetType().GetProperty("message").GetValue(json.Value, null);
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);

        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Answer cannot be empty."));
        Assert.That(error, Is.EqualTo("Answer cannot be empty."));
    }

    [Test]
    public async Task LocalizedErrorAsync_SetsStatusCode_WhenHttpContextExists()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var controller = new TestRuntimeController(sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.SubmitAnswer(null, null); // invalid token & answer -> triggering LocalizedErrorAsync

        Assert.That(httpContext.Response.StatusCode, Is.EqualTo(400));
        var json = (JsonResult)result;
        Assert.That(json.Value.GetType().GetProperty("success").GetValue(json.Value, null), Is.False);
    }

    [Test]
    public void MockAiInterviewController_MaskToken_Works()
    {
        var controller = new TestRuntimeController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object);

        var maskMethod = controller.GetType().GetMethod("MaskToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var maskedShort = maskMethod.Invoke(controller, new object[] { "12345" });
        var maskedLong = maskMethod.Invoke(controller, new object[] { "1234567890" });

        Assert.That(maskedShort, Is.EqualTo("*****"));
        Assert.That(maskedLong, Is.EqualTo("123456..."));
    }

    [Test]
    public void Runtime_NoAgoraSdkUsage()
    {
        var path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "MockAiInterview", "Runtime.cshtml");
        if (!System.IO.File.Exists(path))
            path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "MockAiInterview", "Runtime.cshtml"); // CI/CD path fallback

        var content = System.IO.File.ReadAllText(path);
        Assert.That(content.Contains("AgoraRTC"), Is.False, "Runtime should not contain AgoraRTC usage.");
    }

    [Test]
    public void Plugin_DoesNotShip_AIReferenceFiles()
    {
        var projectText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Nop.Plugin.Misc.AIInterview.csproj"));

        Assert.That(projectText, Does.Contain("<Compile Remove=\"AI_ReferenceFiles\\**\\*\" />"));
        Assert.That(projectText, Does.Contain("<Content Remove=\"AI_ReferenceFiles\\**\\*\" />"));
        Assert.That(projectText, Does.Contain("<None Remove=\"AI_ReferenceFiles\\**\\*\" />"));
    }

    [Test]
    public void Plugin_Copies_JobCard_Assets_To_Output()
    {
        var projectText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Nop.Plugin.Misc.AIInterview.csproj"));
        var jobCardScript = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Content", "js", "aiinterview-job-card.js"));

        Assert.That(projectText, Does.Contain("<None Remove=\"Content\\js\\aiinterview-job-card.js\" />"));
        Assert.That(projectText, Does.Contain("<Content Include=\"Content\\js\\aiinterview-job-card.js\">"));
        Assert.That(projectText, Does.Contain("<None Remove=\"Views\\Shared\\Components\\AIInterviewJobProductCard\\Default.cshtml\" />"));
        Assert.That(projectText, Does.Contain("<Content Include=\"Views\\Shared\\Components\\AIInterviewJobProductCard\\Default.cshtml\">"));
        Assert.That(jobCardScript, Does.Contain("data-ai-job-preview-open"));
        Assert.That(jobCardScript, Does.Contain("data-toggle-url"));
        Assert.That(jobCardScript, Does.Contain("data-ai-job-save-status"));
    }

    [Test]
    public void JobCard_SaveToggle_Uses_ServerBacked_Json_Flow()
    {
        var jobCardScript = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Content", "js", "aiinterview-job-card.js"));

        Assert.That(jobCardScript, Does.Contain("data-toggle-url"));
        Assert.That(jobCardScript, Does.Contain("productId: parseInt(button.getAttribute('data-product-id'), 10) || 0"));
        Assert.That(jobCardScript, Does.Contain("save: shouldSave"));
        Assert.That(jobCardScript, Does.Contain("setSavedState(button.getAttribute('data-product-id'), response.isSaved === true, response.wishlistItemId || 0);"));
        Assert.That(jobCardScript, Does.Not.Contain("fetch('/wishlist'"));
        Assert.That(jobCardScript, Does.Not.Contain("DOMParser"));
        Assert.That(jobCardScript, Does.Not.Contain("querySelectorAll('a[href]')"));
        Assert.That(jobCardScript, Does.Not.Contain("lookupWishlistItemId"));
        Assert.That(jobCardScript, Does.Not.Contain("Saved jobs are temporarily unavailable."));
        Assert.That(jobCardScript, Does.Not.Contain("The selected job could not be found."));
        Assert.That(jobCardScript, Does.Not.Contain("The selected product is not an AI interview job."));
    }

    [Test]
    public void JobCard_Drawer_Loads_Server_Rendered_Detail_Content()
    {
        var jobCardScript = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Content", "js", "aiinterview-job-card.js"));

        Assert.That(jobCardScript, Does.Contain("data-ai-job-drawer-body"));
        Assert.That(jobCardScript, Does.Contain("fetch(drawerUrl"));
        Assert.That(jobCardScript, Does.Contain("executeScripts(drawerBody);"));
        Assert.That(jobCardScript, Does.Contain("drawer.dataset.loaded = 'true';"));
        Assert.That(jobCardScript, Does.Contain("var productUrl = drawer.getAttribute('data-product-url');"));
        Assert.That(jobCardScript, Does.Contain("var jobAiAction = event.target.closest('[data-job-ai-action]');"));
        Assert.That(jobCardScript, Does.Contain("handleJobAiAction(getJobAiPanel(jobAiAction), jobAiAction.getAttribute('data-job-ai-action'));"));
        Assert.That(jobCardScript, Does.Contain("window.location.href = result.runtimeUrl;"));
        Assert.That(jobCardScript, Does.Contain("data-request-error"));
        Assert.That(jobCardScript, Does.Contain("ai-job-preview-fallback-link"));
        Assert.That(jobCardScript, Does.Contain("var drawerErrorText = drawer.getAttribute('data-error-text') || '';"));
        Assert.That(jobCardScript, Does.Not.Contain("Unable to load job details."));
        Assert.That(jobCardScript, Does.Not.Contain("Model.PreviewDescription"));
    }

    [Test]
    public void AdminCandidateDetailsView_Uses_Tabbed_Dashboard_Layout()
    {
        var viewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Areas", "Admin", "Views", "AIInterviewAdmin", "CandidateDetails.cshtml"));
        var cssText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Content", "css", "aiinterview-admin.css"));

        Assert.That(viewText, Does.Contain("candidate-overview-tab"));
        Assert.That(viewText, Does.Contain("candidate-analysis-tab"));
        Assert.That(viewText, Does.Contain("candidate-questions-tab"));
        Assert.That(viewText, Does.Contain("candidate-dashboard-shell"));
        Assert.That(viewText, Does.Contain("candidate-dashboard-question-timeline"));
        Assert.That(viewText, Does.Contain("Internal Session Token"));
        Assert.That(viewText, Does.Contain("Question-by-Question Breakdown"));
        Assert.That(viewText, Does.Contain("data-bs-toggle=\"tab\"").Or.Contain("data-toggle=\"tab\""));
        Assert.That(cssText, Does.Contain(".html-aiinterview-admin-candidate-page"));
        Assert.That(cssText, Does.Contain(".candidate-dashboard-badge.is-success"));
        Assert.That(cssText, Does.Contain(".candidate-dashboard-badge.is-danger"));
        Assert.That(cssText, Does.Contain(".candidate-dashboard-badge.is-warning"));
        Assert.That(cssText, Does.Contain("word-break: break-word"));
        Assert.That(cssText, Does.Contain("overflow-x: auto"));
    }
}
