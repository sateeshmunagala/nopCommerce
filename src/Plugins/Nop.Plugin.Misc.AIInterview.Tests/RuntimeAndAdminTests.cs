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
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class RuntimeAndAdminTests
{
    private Mock<IInterviewSessionService> _sessionService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<IWorkContext> _workContext;
    private Mock<ICustomerService> _customerService;
    private Mock<Nop.Core.Events.IEventPublisher> _eventPublisher;
    private Mock<ILogger> _nopLogger;
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

    [SetUp]
    public void SetUp()
    {
        _sessionService = new Mock<IInterviewSessionService>();
        _localizationService = new Mock<ILocalizationService>();
        _workContext = new Mock<IWorkContext>();
        _customerService = new Mock<ICustomerService>();
        _eventPublisher = new Mock<Nop.Core.Events.IEventPublisher>();
        _nopLogger = new Mock<ILogger>();
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
    public async Task Runtime_Start_Unauthorized_ReturnsError()
    {
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync((Customer)null);
        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()));
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Unauthorized runtime request."));
    }

    [Test]
    public async Task Runtime_RefreshToken_ExpiredActiveSession_RenewsAndUpdatesToken()
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
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);

        var result = await _runtimeController.RefreshToken("expired-active");

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var newToken = json.Value.GetType().GetProperty("newToken").GetValue(json.Value, null)?.ToString();

        Assert.That(success, Is.EqualTo(true));
        Assert.That(newToken, Is.Not.Null);
        Assert.That(newToken, Is.Not.EqualTo("expired-active"));
        Assert.That(json.Value.GetType().GetProperty("tokenExpiryUtc").GetValue(json.Value, null), Is.Not.Null);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s =>
            s.Id == 91 &&
            s.Token == newToken &&
            s.IsActive &&
            s.CompletedOnUtc == null)), Times.Once);
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

        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), 1, "Medium", "inactive-token");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session => session.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>()), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>()), Times.Never);
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
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), 1, "Medium", "exhausted-token");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session => session.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>()), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_NoCredits_ReturnsLocalizedInlineError()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(false);

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

        var model = new ConfigurationModel
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

        Assert.That(success, Is.EqualTo(true));
        Assert.That(newToken, Is.Not.EqualTo("old-token"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Token == (string)newToken)), Times.Once);
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
    public async Task Runtime_RefreshToken_ExpiredActiveSession_Renews()
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
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);

        var result = await _runtimeController.RefreshToken("expired-active");
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var newToken = json.Value.GetType().GetProperty("newToken").GetValue(json.Value, null)?.ToString();

        Assert.That(success, Is.EqualTo(true));
        Assert.That(newToken, Is.Not.Null);
        Assert.That(newToken, Is.Not.EqualTo("expired-active"));
        Assert.That(json.Value.GetType().GetProperty("tokenExpiryUtc").GetValue(json.Value, null), Is.Not.Null);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Token == newToken && s.IsActive)), Times.Once);
    }

    [Test]
    public async Task Runtime_SubmitAnswer_ExpiredActiveSession_ReturnsRenewedToken()
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
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        _interviewRuntimeService.Setup(x => x.SubmitAnswerAsync(It.IsAny<SubmitInterviewAnswerRequest>()))
            .ReturnsAsync(new SubmitInterviewAnswerResponse
            {
                Success = true,
                Question = "Next question"
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

        var result = await controller.SubmitAnswer("expired-submit", "Answer");

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        var successProperty = json.Value.GetType().GetProperty("success");
        var duplicateSuccessProperty = json.Value.GetType().GetProperty("Success");
        var newTokenProperty = json.Value.GetType().GetProperty("newToken");
        var duplicateReportUrlProperty = json.Value.GetType().GetProperty("ReportUrl");
        Assert.That(successProperty, Is.Not.Null);
        Assert.That(duplicateSuccessProperty, Is.Null);
        Assert.That(newTokenProperty, Is.Not.Null, "Expected renewed submit-answer response to include a token update.");
        Assert.That(duplicateReportUrlProperty, Is.Null);
        var success = successProperty.GetValue(json.Value, null);
        Assert.That(success, Is.EqualTo(true));
        var newToken = newTokenProperty.GetValue(json.Value, null)?.ToString();
        Assert.That(newToken, Is.Not.Null);
        Assert.That(newToken, Is.Not.EqualTo("expired-submit"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Token == newToken)), Times.Once);
    }

    [Test]
    public async Task Runtime_Stop_ExpiredActiveSession_ReturnsRenewedToken_WithSuccess()
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
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        _interviewRuntimeService.Setup(x => x.CompleteInterviewAsync(It.IsAny<string>(), "Stopped by user"))
            .ReturnsAsync(new CompleteInterviewResponse { Success = true, IsTerminated = true, Completion = "done" });

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
        var successProperty = json.Value.GetType().GetProperty("success");
        var duplicateSuccessProperty = json.Value.GetType().GetProperty("Success");
        var duplicateReportUrlProperty = json.Value.GetType().GetProperty("ReportUrl");
        var newTokenProperty = json.Value.GetType().GetProperty("newToken");
        Assert.That(successProperty, Is.Not.Null);
        Assert.That(duplicateSuccessProperty, Is.Null);
        Assert.That(duplicateReportUrlProperty, Is.Null);
        Assert.That(newTokenProperty, Is.Not.Null);

        var success = successProperty.GetValue(json.Value, null);
        var newToken = newTokenProperty.GetValue(json.Value, null)?.ToString();

        Assert.That(success, Is.EqualTo(true));
        Assert.That(newToken, Is.Not.Null.And.Not.EqualTo("expired-stop"));
    }

    [Test]
    public async Task Runtime_Get_With_Expired_Active_Token_Renews_And_Redirects()
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
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);

        var result = await _runtimeController.Runtime("expired-runtime");

        Assert.That(result, Is.TypeOf<RedirectToActionResult>());
        var redirect = (RedirectToActionResult)result;
        Assert.That(redirect.ActionName, Is.EqualTo(nameof(MockAiInterviewController.Runtime)));
        Assert.That(redirect.RouteValues["token"], Is.Not.EqualTo("expired-runtime"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Once);
    }

    [Test]
    public async Task Runtime_Preserves_Service_Level_MediaFlags()
    {
        var runtimeModel = new InterviewRuntimeModel
        {
            SessionId = 1,
            ProductId = 1,
            QuestionCount = 5,
            SessionKey = "session-key",
            Token = "token",
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
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        _interviewRuntimeService.Setup(x => x.GetRuntimeModelAsync("token"))
            .ReturnsAsync(runtimeModel);

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
                var name when name == AIInterviewDefaults.MockStopRouteName => "/mockaiinterview/stop",
                var name when name == AIInterviewDefaults.MockRefreshTokenRouteName => "/mockaiinterview/refresh-token",
                var name when name == AIInterviewDefaults.MockSpeechTokenRouteName => "/mockaiinterview/speech-token",
                var name when name == AIInterviewDefaults.MockRecordingUploadRouteName => "/mockaiinterview/upload-recording",
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
        Assert.That(model.ReportUrl, Is.EqualTo("/mockaiinterview/report/1"));
        Assert.That(model.ClientSettings.ReportUrl, Is.EqualTo("/mockaiinterview/report/1"));
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
    public async Task Runtime_SubmitAnswer_CompletedResponse_UsesMockReportRoute()
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
                Completion = "done"
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

        Assert.That(reportUrl, Is.EqualTo("/mockaiinterview/report/71"));
    }

    [Test]
    public async Task Runtime_Stop_CompletedResponse_UsesMockReportRoute()
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
                ReportUrl = string.Empty
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

        Assert.That(reportUrl, Is.EqualTo("/mockaiinterview/report/72"));
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
    public void RuntimeView_Contains_Recording_And_Upload_Hooks()
    {
        var runtimeViewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));
        var beginInterviewStart = runtimeViewText.IndexOf("const beginInterview = async () =>", StringComparison.Ordinal);
        var beginInterviewScreenShareIndex = runtimeViewText.IndexOf("if (!(await requestScreenShareForInterviewStart()))", beginInterviewStart, StringComparison.Ordinal);
        var beginInterviewTokenRefreshIndex = runtimeViewText.IndexOf("if (!(await ensureRuntimeTokenFresh()))", beginInterviewStart, StringComparison.Ordinal);
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
        Assert.That(runtimeViewText, Does.Contain("answerBox.disabled = !canEditAnswer;"));
        Assert.That(runtimeViewText, Does.Contain("const setRuntimeCaption = (speaker, text) =>"));
        Assert.That(runtimeViewText, Does.Contain("const syncAnswerCaption = () =>"));
        Assert.That(runtimeViewText, Does.Contain("videoCaptionSpeaker.textContent = `${speaker}:`;"));
        Assert.That(runtimeViewText, Does.Contain("setRuntimeCaption('Interviewer', currentQuestionText());"));
        Assert.That(runtimeViewText, Does.Contain("setRuntimeCaption('You', currentAnswer);"));
        Assert.That(runtimeViewText, Does.Not.Contain("console.log(config)"));
        Assert.That(runtimeViewText, Does.Not.Contain("Settings Config"));
        Assert.That(runtimeViewText, Does.Not.Contain("Body: ${text}"));
        Assert.That(runtimeViewText, Does.Contain("tokenRefreshPromise"));
        Assert.That(runtimeViewText, Does.Contain("if (tokenRefreshPromise)"));
        Assert.That(runtimeViewText, Does.Not.Contain("tokenRefreshInFlight"));
        Assert.That(runtimeViewText, Does.Contain("const updateRuntimeUrlToken = (newToken) =>"));
        Assert.That(runtimeViewText, Does.Contain("if (!newToken || !window.history?.replaceState)"));
        Assert.That(runtimeViewText, Does.Contain("url.searchParams.set('token', newToken);"));
        Assert.That(runtimeViewText, Does.Contain("window.history.replaceState(window.history.state, document.title, url.toString());"));
        Assert.That(runtimeViewText, Does.Contain("updateRuntimeUrlToken(newToken);"));
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
        Assert.That(runtimeViewText, Does.Contain("const clearTokenRefreshTimer = () =>"));
        Assert.That(runtimeViewText, Does.Contain("const clearAllRuntimeTimers = () =>"));
        Assert.That(runtimeViewText, Does.Contain("Submit Answer (${countdownValue})"));
        Assert.That(runtimeViewText, Does.Contain("answerBox.addEventListener('input', () => {"));
        Assert.That(runtimeViewText, Does.Contain("resetTimers();"));
        Assert.That(runtimeViewText, Does.Not.Contain("answerStageTimer = setTimeout(() => {"));
        Assert.That(runtimeViewText, Does.Not.Contain("}, autoSubmitDelaySeconds * 1000);"));
        Assert.That(runtimeViewText, Does.Contain("Please speak or type something."));
        Assert.That(runtimeViewText, Does.Contain("stopInProgress = true;"));
        Assert.That(runtimeViewText, Does.Contain("runtimeStoppedOrCompleted = true;"));
        Assert.That(runtimeViewText, Does.Contain("clearTokenRefreshTimer();"));
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
        Assert.That(runtimeViewText, Does.Contain("await finalizeRecordingBeforeCompletion();"));
        Assert.That(runtimeViewText, Does.Contain("const startCompletedRedirectCountdown = (reportUrl) =>"));
        Assert.That(runtimeViewText, Does.Contain("startCompletedRedirectCountdown(reportUrl);"));
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
        Assert.That(runtimeViewText, Does.Contain("Plugins.Misc.AIInterview.Runtime.Guidelines.Acknowledge"));
        Assert.That(runtimeViewText, Does.Contain("Mobile phones and tablets are not allowed by policy, but they are not blocked technically."));
        Assert.That(runtimeViewText, Does.Contain("Entire screen sharing is required for this interview."));
        Assert.That(runtimeViewText, Does.Contain("When your browser asks what to share, choose Entire screen or Your entire screen. Do not select a browser tab or a single window."));
        Assert.That(runtimeViewText, Does.Contain("Use full screen and keep the interview tab visible."));
        Assert.That(runtimeViewText, Does.Contain("runtime-screen-share-guide"));
        Assert.That(runtimeViewText, Does.Contain("Share picker guide"));
        Assert.That(runtimeViewText, Does.Contain("runtime-share-system-audio"));
        Assert.That(runtimeViewText, Does.Contain("Also share system audio"));
        Assert.That(runtimeViewText, Does.Contain("Select this"));
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
        Assert.That(runtimeViewText, Does.Contain("stopScreenShare();\r\n                setStatus('Interview token refresh failed.', true);").Or.Contain("stopScreenShare();\n                setStatus('Interview token refresh failed.', true);"));
        Assert.That(beginInterviewStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(beginInterviewScreenShareIndex, Is.GreaterThan(beginInterviewStart));
        Assert.That(beginInterviewTokenRefreshIndex, Is.GreaterThan(beginInterviewStart));
        Assert.That(beginInterviewScreenShareIndex, Is.LessThan(beginInterviewTokenRefreshIndex));
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
        Assert.That(runtimeViewText, Does.Contain("Interview completed. Please wait, creating the report."));
        Assert.That(runtimeViewText, Does.Contain("Redirecting to report in ${remainingSeconds}s."));
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
        Assert.That(runtimeViewText, Does.Contain("id=\"recording-status\""));
    }

    [Test]
    public void RuntimeView_Speaks_Final_Completion_Message_Once()
    {
        var runtimeViewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));

        Assert.That(runtimeViewText, Does.Contain("let finalCompletionSpoken = false;"));
        Assert.That(runtimeViewText, Does.Contain("const defaultFinalCompletionMessage = 'Thank you. Your interview is complete.';"));
        Assert.That(runtimeViewText, Does.Contain("const getFinalCompletionSpeechText = (result) =>"));
        Assert.That(runtimeViewText, Does.Contain("getValue(result, 'completion', 'Completion')"));
        Assert.That(runtimeViewText, Does.Contain("const shouldResumeRecognition = purpose !== 'completion' && shouldStopRecognitionForPlayback;"));
        Assert.That(runtimeViewText, Does.Contain("if (shouldResumeRecognition && !runtimeStoppedOrCompleted && !speechUnavailable && isMicActive())"));
        Assert.That(runtimeViewText, Does.Contain("if (!finalCompletionSpoken)"));
        Assert.That(runtimeViewText, Does.Contain("finalCompletionSpoken = true;"));
        Assert.That(runtimeViewText, Does.Contain("speakText(getFinalCompletionSpeechText(result), 'completion')"));
        Assert.That(runtimeViewText, Does.Contain(".catch(() => logActivity('Final completion speech failed.'));"));
        Assert.That(
            runtimeViewText.IndexOf("speakText(getFinalCompletionSpeechText(result), 'completion')", StringComparison.Ordinal),
            Is.LessThan(runtimeViewText.IndexOf("await setCompletedState(result);", StringComparison.Ordinal)));
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
        Assert.That(runtimeCssText, Does.Contain(".runtime-video {\r\n        min-height: min(450px, 53vh);").Or.Contain(".runtime-video {\n        min-height: min(450px, 53vh);"));
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
    public async Task Runtime_SpeechToken_ExpiredOrInactive_ReturnsSafeJson()
    {
        _interviewRuntimeService.Setup(x => x.GetSpeechTokenAsync("expired")).ReturnsAsync(new SpeechTokenResponseModel
        {
            Success = false,
            Message = "Voice mode is unavailable. Please type your answer below.",
            FailureKind = "invalid-session",
            DiagnosticMessage = "Mode=speech-token; FailureKind=invalid-session; AzureResponseBody={\"error\":\"do-not-leak\"}; StackTrace=hidden;"
        });
        _interviewRuntimeService.Setup(x => x.GetSpeechTokenAsync("inactive")).ReturnsAsync(new SpeechTokenResponseModel
        {
            Success = false,
            Message = "Voice mode is unavailable. Please type your answer below.",
            FailureKind = "invalid-session",
            DiagnosticMessage = "Mode=speech-token; FailureKind=invalid-session; EndpointHost=secret.example;"
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

        Assert.That(expiredError, Is.EqualTo("Voice mode is unavailable. Please type your answer below."));
        Assert.That(expiredMessage, Is.EqualTo("Voice mode is unavailable. Please type your answer below."));
        Assert.That(serializedExpired, Does.Not.Contain("do-not-leak"));
        Assert.That(serializedExpired, Does.Not.Contain("secret.example"));
        Assert.That(serializedExpired, Does.Not.Contain("StackTrace"));

        Assert.That(((JsonResult)inactive).Value.GetType().GetProperty("error").GetValue(((JsonResult)inactive).Value, null), Is.EqualTo("Voice mode is unavailable. Please type your answer below."));
        _nopLogger.Verify(x => x.InsertLogAsync(
            It.IsAny<LogLevel>(),
            "AI Interview speech token failure",
            It.IsAny<string>(),
            It.IsAny<Customer>()), Times.Never);
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
        var method = typeof(MockAiInterviewAdminController).GetMethod("Configure", new[] { typeof(ConfigurationModel) });
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

        public async Task<IActionResult> TestFallback()
        {
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Missing", "Fallback text");
        }
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
