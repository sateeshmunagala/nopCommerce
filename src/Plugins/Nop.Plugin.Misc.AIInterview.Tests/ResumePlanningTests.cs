using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Logging;
using Nop.Core.Domain.Media;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using NopLogger = Nop.Services.Logging.ILogger;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class ResumePlanningTests
{
    private static byte[] CreateDocx(params string[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(
                new Body(paragraphs.Select(text => new Paragraph(new Run(new Text(text))))));
        }

        return stream.ToArray();
    }

    private static byte[] CreateDocxWithHeaderAndFooter(string bodyText, string headerText, string footerText)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text(bodyText)))));

            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(new Paragraph(new Run(new Text(headerText))));
            var footerPart = mainPart.AddNewPart<FooterPart>();
            footerPart.Footer = new Footer(new Paragraph(new Run(new Text(footerText))));

            var sectionProperties = new SectionProperties(
                new HeaderReference { Id = mainPart.GetIdOfPart(headerPart), Type = HeaderFooterValues.Default },
                new FooterReference { Id = mainPart.GetIdOfPart(footerPart), Type = HeaderFooterValues.Default });
            mainPart.Document.Body.Append(sectionProperties);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static IFormFile CreateResumeFile(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "ResumeFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "application/pdf"
                : "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };
    }

    [Test]
    public async Task ResumeTextExtractionService_Docx_NormalizesWhitespace()
    {
        var service = new ResumeTextExtractionService();
        var download = new Download
        {
            Filename = "resume.docx",
            Extension = ".docx",
            DownloadBinary = CreateDocx("Senior   .NET   Engineer", "Built payment platform   with Azure")
        };

        var result = await service.ExtractTextAsync(download);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Text, Is.EqualTo("Senior .NET Engineer Built payment platform with Azure"));
    }

    [Test]
    public async Task ResumeTextExtractionService_Unsupported_And_Empty_Content_Fail_Safely()
    {
        var service = new ResumeTextExtractionService();

        var unsupported = await service.ExtractTextAsync(new Download
        {
            Filename = "resume.txt",
            Extension = ".txt",
            DownloadBinary = Encoding.UTF8.GetBytes("plain text")
        });

        var empty = await service.ExtractTextAsync(new Download
        {
            Filename = "resume.docx",
            Extension = ".docx",
            DownloadBinary = CreateDocx(string.Empty, "   ")
        });

        Assert.That(unsupported.Success, Is.False);
        Assert.That(unsupported.ErrorCode, Is.EqualTo("unsupported_extension"));
        Assert.That(empty.Success, Is.False);
        Assert.That(empty.ErrorCode, Is.EqualTo("empty_text"));
    }

    [Test]
    public async Task ResumeTextExtractionService_InvalidDocx_ReturnsExtractionFailed()
    {
        var service = new ResumeTextExtractionService();

        var result = await service.ExtractTextAsync(new Download
        {
            Filename = "invalid.docx",
            Extension = ".docx",
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            DownloadBinary = Encoding.UTF8.GetBytes("not-a-real-openxml-package")
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo("extraction_failed"));
        Assert.That(result.ExceptionType, Is.Not.Empty);
    }

    [Test]
    public async Task ResumeTextExtractionService_Docx_ExtractsSupportedWordParts()
    {
        var service = new ResumeTextExtractionService();
        var download = new Download
        {
            Filename = "resume.docx",
            Extension = ".docx",
            DownloadBinary = CreateDocxWithHeaderAndFooter("Body content", "Header content", "Footer content")
        };

        var result = await service.ExtractTextAsync(download);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Text, Does.Contain("Body content"));
        Assert.That(result.Text, Does.Contain("Header content"));
        Assert.That(result.Text, Does.Contain("Footer content"));
    }

    [Test]
    public async Task InterviewAiClient_MockResumeProfile_And_QuestionPlan_Return_Structured_Data()
    {
        var client = new InterviewAiClient(new AIInterviewSettings(), new MockAIInterviewSettings { UseMockResponses = true });

        var profile = await client.AnalyzeResumeAsync(new AIResumeProfileRequest
        {
            JobTitle = "Backend Engineer",
            JobContext = "Payments and cloud APIs",
            ResumeText = "Worked on a payment platform project using C#, .NET, Azure, SQL, Docker and REST APIs."
        });

        var profileJson = JsonSerializer.Serialize(new
        {
            skills = profile.Skills,
            primarySkills = profile.PrimarySkills,
            tools = profile.Tools,
            projects = profile.Projects,
            experienceSummary = profile.ExperienceSummary,
            senioritySignals = profile.SenioritySignals,
            missingOrUnclearAreas = profile.MissingOrUnclearAreas
        });

        var plan = await client.GenerateQuestionPlanAsync(new AIInterviewQuestionPlanRequest
        {
            JobTitle = "Backend Engineer",
            JobContext = "Payments and cloud APIs",
            Difficulty = "Medium",
            QuestionCount = 5,
            Prompt = "Focus on practical experience.",
            ResumeProfileJson = profileJson
        });

        Assert.That(profile.Success, Is.True);
        Assert.That(profile.Skills, Does.Contain("C#"));
        Assert.That(profile.PrimarySkills, Is.Not.Empty);
        Assert.That(profile.Projects, Is.Not.Empty);

        Assert.That(plan.Success, Is.True);
        Assert.That(plan.Questions.Count, Is.EqualTo(5));
        Assert.That(plan.Questions.Select(question => question.SequenceNumber).Distinct().Count(), Is.EqualTo(5));
        Assert.That(plan.Questions.First().Category, Is.EqualTo("Introduction & Project Experience"));
        Assert.That(plan.Questions.First().Question, Does.Contain("introduce yourself"));
        Assert.That(plan.Questions.Skip(1).Any(question => question.Category == "skill"), Is.True);
        Assert.That(plan.Questions.Skip(1).Any(question => question.Category == "project_scenario"), Is.True);
        Assert.That(plan.Questions.All(question => !string.IsNullOrWhiteSpace(question.Question)), Is.True);
    }

    [Test]
    public async Task Apply_With_ResumeServices_Stores_Profile_After_Insert()
    {
        var applicationService = new Mock<IApplicationService>();
        var interviewSessionService = new Mock<IInterviewSessionService>();
        var workContext = new Mock<IWorkContext>();
        var notificationService = new Mock<INotificationService>();
        var localizationService = new Mock<ILocalizationService>();
        var downloadService = new Mock<IDownloadService>();
        var customerService = new Mock<ICustomerService>();
        var productService = new Mock<IProductService>();
        var jobRequirementService = new Mock<IJobRequirementService>();
        var resumeFileService = new Mock<IResumeFileService>();
        var resumeProfileService = new Mock<IResumeProfileService>();

        var customer = new Customer { Id = 9 };
        var product = new Product { Id = 15, Name = "Backend Engineer" };
        var resumeFile = CreateResumeFile("resume.pdf", "fake-binary");

        workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        workContext.Setup(context => context.GetWorkingLanguageAsync()).ReturnsAsync(new Nop.Core.Domain.Localization.Language { Id = 1 });
        localizationService.Setup(service => service.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        applicationService.Setup(service => service.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<JobApplication>());
        applicationService.Setup(service => service.GetJobApplicationsByCustomerIdAndJobTitleAsync(customer.Id, "Backend Engineer")).ReturnsAsync(new List<JobApplication>());
        interviewSessionService.Setup(service => service.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        jobRequirementService.Setup(service => service.GetRequirementsAsync(15)).ReturnsAsync(new JobRequirementsModel());
        productService.Setup(service => service.GetProductByIdAsync(15)).ReturnsAsync(product);
        resumeFileService.Setup(service => service.ValidateResumeFile(resumeFile)).Returns(new ResumeFileValidationResult { Success = true });
        resumeFileService.Setup(service => service.StoreResumeAsync(resumeFile)).ReturnsAsync(new ResumeFileStoreResult { Success = true, DownloadId = 42 });
        applicationService.Setup(service => service.InsertJobApplicationAsync(It.IsAny<JobApplication>()))
            .Callback<JobApplication>(application => application.Id = 77)
            .Returns(Task.CompletedTask);
        resumeProfileService.Setup(service => service.EnsureResumeProfileAsync(It.IsAny<JobApplication>(), product, true))
            .ReturnsAsync(new ResumeProfileGenerationResult { Success = true, ProfileJson = "{\"skills\":[\"C#\"]}" });

        var controller = new AIInterviewController(
            applicationService.Object,
            interviewSessionService.Object,
            new AIInterviewSettings { Enabled = true },
            workContext.Object,
            notificationService.Object,
            localizationService.Object,
            downloadService.Object,
            customerService.Object,
            productService.Object,
            jobRequirementService.Object,
            resumeFileService: resumeFileService.Object,
            resumeProfileService: resumeProfileService.Object);

        var result = await controller.Apply(new ApplyModel
        {
            JobTitle = "Backend Engineer",
            ProductId = 15,
            ResumeFile = resumeFile
        });

        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        applicationService.Verify(service => service.InsertJobApplicationAsync(It.Is<JobApplication>(application =>
            application.Id == 77 &&
            application.ResumeDownloadId == 42 &&
            application.ProductId == 15)), Times.Once);
        resumeProfileService.Verify(service => service.EnsureResumeProfileAsync(
            It.Is<JobApplication>(application => application.Id == 77 && application.ResumeDownloadId == 42),
            product,
            true), Times.Once);
        downloadService.Verify(service => service.InsertDownloadAsync(It.IsAny<Download>()), Times.Never);
    }

    [Test]
    public async Task StartPost_With_ResumeFile_Creates_Application_And_Links_Session()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var inviteService = new Mock<ISponsorInviteService>();
        var creditService = new Mock<ICreditService>();
        var customerService = new Mock<ICustomerService>();
        var productService = new Mock<IProductService>();
        var vendorService = new Mock<Nop.Services.Vendors.IVendorService>();
        var applicationService = new Mock<IApplicationService>();
        var jobRequirementService = new Mock<IJobRequirementService>();
        var resumeFileService = new Mock<IResumeFileService>();
        var resumeProfileService = new Mock<IResumeProfileService>();

        var customer = new Customer { Id = 12, Email = "candidate@example.com" };
        var product = new Product { Id = 44, Name = "Platform Engineer" };
        var resumeFile = CreateResumeFile("resume.pdf", "binary");
        var files = new FormFileCollection { resumeFile };
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), files);

        workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        localizationService.Setup(service => service.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        sessionService.Setup(service => service.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        productService.Setup(service => service.GetProductByIdAsync(44)).ReturnsAsync(product);
        creditService.Setup(service => service.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);
        applicationService.Setup(service => service.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<JobApplication>());
        jobRequirementService.Setup(service => service.GetRequirementsAsync(44)).ReturnsAsync(new JobRequirementsModel { QuestionCount = 5 });
        resumeFileService.Setup(service => service.ValidateResumeFile(resumeFile)).Returns(new ResumeFileValidationResult { Success = true });
        resumeFileService.Setup(service => service.StoreResumeAsync(resumeFile)).ReturnsAsync(new ResumeFileStoreResult { Success = true, DownloadId = 78 });
        applicationService.Setup(service => service.InsertJobApplicationAsync(It.IsAny<JobApplication>()))
            .Callback<JobApplication>(application => application.Id = 501)
            .Returns(Task.CompletedTask);
        sessionService.Setup(service => service.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(session => session.Id = 900)
            .Returns(Task.CompletedTask);
        resumeProfileService.Setup(service => service.EnsureResumeProfileAsync(It.IsAny<JobApplication>(), product, true))
            .ReturnsAsync(new ResumeProfileGenerationResult { Success = true, ProfileJson = "{\"skills\":[\"Azure\"]}" });

        var controller = new MockAiInterviewController(
            sessionService.Object,
            localizationService.Object,
            workContext.Object,
            inviteService.Object,
            creditService.Object,
            customerService.Object,
            productService.Object,
            vendorService.Object,
            applicationService.Object,
            jobRequirementService: jobRequirementService.Object,
            resumeFileService: resumeFileService.Object,
            resumeProfileService: resumeProfileService.Object);

        var result = await controller.StartPost(form, 44, "Medium");

        Assert.That(result, Is.TypeOf<JsonResult>());
        applicationService.Verify(service => service.InsertJobApplicationAsync(It.Is<JobApplication>(application =>
            application.Id == 501 &&
            application.ProductId == 44 &&
            application.ResumeDownloadId == 78 &&
            application.CustomerId == 12)), Times.Once);
        sessionService.Verify(service => service.InsertInterviewSessionAsync(It.Is<InterviewSession>(session =>
            session.JobApplicationId == 501 &&
            session.ProductId == 44 &&
            session.QuestionCount == 5 &&
            session.CustomerId == 12)), Times.Once);
        resumeProfileService.Verify(service => service.EnsureResumeProfileAsync(
            It.Is<JobApplication>(application => application.Id == 501 && application.ResumeDownloadId == 78),
            product,
            true), Times.Once);
    }

    [Test]
    public async Task StartPost_WithInvalidResume_DoesNotChargeOrCreateSession()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var inviteService = new Mock<ISponsorInviteService>();
        var creditService = new Mock<ICreditService>();
        var customerService = new Mock<ICustomerService>();
        var productService = new Mock<IProductService>();
        var vendorService = new Mock<Nop.Services.Vendors.IVendorService>();
        var applicationService = new Mock<IApplicationService>();
        var jobRequirementService = new Mock<IJobRequirementService>();
        var resumeFileService = new Mock<IResumeFileService>();

        var customer = new Customer { Id = 12, Email = "candidate@example.com" };
        var product = new Product { Id = 44, Name = "Platform Engineer" };
        var resumeFile = CreateResumeFile("resume.txt", "binary");
        var files = new FormFileCollection { resumeFile };
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), files);

        workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        localizationService.Setup(service => service.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        sessionService.Setup(service => service.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        productService.Setup(service => service.GetProductByIdAsync(44)).ReturnsAsync(product);
        applicationService.Setup(service => service.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<JobApplication>());
        jobRequirementService.Setup(service => service.GetRequirementsAsync(44)).ReturnsAsync(new JobRequirementsModel());
        resumeFileService.Setup(service => service.ValidateResumeFile(resumeFile)).Returns(new ResumeFileValidationResult
        {
            Success = false,
            ErrorMessage = "Allowed resume file types: PDF, DOCX. Maximum size: 5 MB."
        });

        var controller = new MockAiInterviewController(
            sessionService.Object,
            localizationService.Object,
            workContext.Object,
            inviteService.Object,
            creditService.Object,
            customerService.Object,
            productService.Object,
            vendorService.Object,
            applicationService.Object,
            jobRequirementService: jobRequirementService.Object,
            resumeFileService: resumeFileService.Object);

        var result = await controller.StartPost(form, 44, "Medium");

        Assert.That(result, Is.TypeOf<JsonResult>());
        creditService.Verify(service => service.AuthorizeAndChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        sessionService.Verify(service => service.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        applicationService.Verify(service => service.InsertJobApplicationAsync(It.IsAny<JobApplication>()), Times.Never);
        applicationService.Verify(service => service.UpdateJobApplicationAsync(It.IsAny<JobApplication>()), Times.Never);
    }

    [Test]
    public async Task StartPost_WhenResumeRequiredAndMissing_DoesNotChargeOrCreateSession()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var inviteService = new Mock<ISponsorInviteService>();
        var creditService = new Mock<ICreditService>();
        var customerService = new Mock<ICustomerService>();
        var productService = new Mock<IProductService>();
        var vendorService = new Mock<Nop.Services.Vendors.IVendorService>();
        var applicationService = new Mock<IApplicationService>();
        var jobRequirementService = new Mock<IJobRequirementService>();

        var customer = new Customer { Id = 12, Email = "candidate@example.com" };
        var product = new Product { Id = 44, Name = "Platform Engineer" };
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());

        workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        localizationService.Setup(service => service.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        sessionService.Setup(service => service.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        productService.Setup(service => service.GetProductByIdAsync(44)).ReturnsAsync(product);
        applicationService.Setup(service => service.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<JobApplication>());
        jobRequirementService.Setup(service => service.GetRequirementsAsync(44)).ReturnsAsync(new JobRequirementsModel
        {
            ResumeRequired = true,
            QuestionCount = 5
        });

        var controller = new MockAiInterviewController(
            sessionService.Object,
            localizationService.Object,
            workContext.Object,
            inviteService.Object,
            creditService.Object,
            customerService.Object,
            productService.Object,
            vendorService.Object,
            applicationService.Object,
            jobRequirementService: jobRequirementService.Object);

        var result = await controller.StartPost(form, 44, "Medium");

        Assert.That(result, Is.TypeOf<JsonResult>());
        creditService.Verify(service => service.AuthorizeAndChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        sessionService.Verify(service => service.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        applicationService.Verify(service => service.InsertJobApplicationAsync(It.IsAny<JobApplication>()), Times.Never);
        applicationService.Verify(service => service.UpdateJobApplicationAsync(It.IsAny<JobApplication>()), Times.Never);
    }

    [Test]
    public async Task StartPost_ReusableSession_WithPostedResume_StoresResumeAndLinksApplication()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var inviteService = new Mock<ISponsorInviteService>();
        var creditService = new Mock<ICreditService>();
        var customerService = new Mock<ICustomerService>();
        var productService = new Mock<IProductService>();
        var vendorService = new Mock<Nop.Services.Vendors.IVendorService>();
        var applicationService = new Mock<IApplicationService>();
        var jobRequirementService = new Mock<IJobRequirementService>();
        var resumeFileService = new Mock<IResumeFileService>();
        var resumeProfileService = new Mock<IResumeProfileService>();
        var turnService = new Mock<IInterviewTurnService>();

        var customer = new Customer { Id = 12, Email = "candidate@example.com" };
        var product = new Product { Id = 44, Name = "Platform Engineer" };
        var resumeFile = CreateResumeFile("resume.pdf", "binary");
        var files = new FormFileCollection { resumeFile };
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), files);
        var reusableSession = new InterviewSession
        {
            Id = 900,
            ProductId = 44,
            CustomerId = 12,
            SessionKey = "session-900",
            Token = "token-900",
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(20),
            IsActive = true
        };
        var plannedTurns = new List<InterviewTurn>
        {
            new() { Id = 1, InterviewSessionId = 900, SequenceNumber = 1, QuestionText = "Q1", AskedOnUtc = DateTime.UtcNow.AddMinutes(-2), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-2) },
            new() { Id = 2, InterviewSessionId = 900, SequenceNumber = 2, QuestionText = "Q2", AskedOnUtc = DateTime.UtcNow.AddMinutes(-1), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1) }
        };

        workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        localizationService.Setup(service => service.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        sessionService.Setup(service => service.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession> { reusableSession });
        productService.Setup(service => service.GetProductByIdAsync(44)).ReturnsAsync(product);
        applicationService.Setup(service => service.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<JobApplication>());
        jobRequirementService.Setup(service => service.GetRequirementsAsync(44)).ReturnsAsync(new JobRequirementsModel { QuestionCount = 5 });
        resumeFileService.Setup(service => service.ValidateResumeFile(resumeFile)).Returns(new ResumeFileValidationResult { Success = true });
        resumeFileService.Setup(service => service.StoreResumeAsync(resumeFile)).ReturnsAsync(new ResumeFileStoreResult { Success = true, DownloadId = 78 });
        applicationService.Setup(service => service.InsertJobApplicationAsync(It.IsAny<JobApplication>()))
            .Callback<JobApplication>(application => application.Id = 501)
            .Returns(Task.CompletedTask);
        resumeProfileService.Setup(service => service.EnsureResumeProfileAsync(It.IsAny<JobApplication>(), product, true))
            .ReturnsAsync(new ResumeProfileGenerationResult { Success = true, ProfileJson = "{\"skills\":[\"Azure\"]}" });
        turnService.Setup(service => service.GetTurnsBySessionIdAsync(900)).ReturnsAsync(plannedTurns);
        sessionService.Setup(service => service.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);

        var controller = new MockAiInterviewController(
            sessionService.Object,
            localizationService.Object,
            workContext.Object,
            inviteService.Object,
            creditService.Object,
            customerService.Object,
            productService.Object,
            vendorService.Object,
            applicationService.Object,
            jobRequirementService: jobRequirementService.Object,
            turnService: turnService.Object,
            resumeFileService: resumeFileService.Object,
            resumeProfileService: resumeProfileService.Object);

        var result = await controller.StartPost(form, 44, "Medium");

        Assert.That(result, Is.TypeOf<JsonResult>());
        creditService.Verify(service => service.AuthorizeAndChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        sessionService.Verify(service => service.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        applicationService.Verify(service => service.InsertJobApplicationAsync(It.Is<JobApplication>(application =>
            application.Id == 501 &&
            application.ProductId == 44 &&
            application.ResumeDownloadId == 78 &&
            application.CustomerId == 12)), Times.Once);
        resumeProfileService.Verify(service => service.EnsureResumeProfileAsync(
            It.Is<JobApplication>(application => application.Id == 501 && application.ResumeDownloadId == 78),
            product,
            true), Times.Once);
        turnService.Verify(service => service.DeleteInterviewTurnsAsync(It.Is<IList<InterviewTurn>>(turns => turns.Count == 2 && turns.All(turn => turn.InterviewSessionId == 900))), Times.Once);
        sessionService.Verify(service => service.UpdateInterviewSessionAsync(It.Is<InterviewSession>(session =>
            session.Id == 900 &&
            session.JobApplicationId == 501)), Times.Once);
    }

    [Test]
    public async Task ResumeProfileService_ExtractionFailure_PersistsError_AndLogsAdminWarning()
    {
        var downloadService = new Mock<IDownloadService>();
        var extractionService = new Mock<IResumeTextExtractionService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var applicationService = new Mock<IApplicationService>();
        var interviewSessionService = new Mock<IInterviewSessionService>();
        var productService = new Mock<IProductService>();
        var nopLogger = new Mock<NopLogger>();

        var application = new JobApplication
        {
            Id = 45,
            CustomerId = 9,
            ProductId = 15,
            ResumeDownloadId = 88,
            JobTitle = "Backend Engineer"
        };
        var product = new Product { Id = 15, Name = "Backend Engineer" };
        var download = new Download
        {
            Id = 88,
            Filename = "resume.docx",
            Extension = ".docx",
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            DownloadBinary = new byte[321]
        };

        downloadService.Setup(service => service.GetDownloadByIdAsync(88)).ReturnsAsync(download);
        extractionService.Setup(service => service.ExtractTextAsync(download)).ReturnsAsync(new ResumeTextExtractionResult
        {
            Success = false,
            ErrorCode = "empty_text",
            ErrorMessage = "Resume text could not be extracted.",
            DiagnosticMessage = "empty main document text"
        });
        applicationService.Setup(service => service.UpdateJobApplicationAsync(It.IsAny<JobApplication>())).Returns(Task.CompletedTask);
        nopLogger.Setup(logger => logger.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var service = new ResumeProfileService(
            downloadService.Object,
            extractionService.Object,
            aiClient.Object,
            applicationService.Object,
            interviewSessionService.Object,
            productService.Object,
            new Mock<IAzureUsageService>().Object,
            nopLogger.Object);

        var result = await service.EnsureResumeProfileAsync(application, product, true);

        Assert.That(result.Success, Is.False);
        Assert.That(application.ResumeProfileError, Does.Contain("empty_text"));
        applicationService.Verify(x => x.UpdateJobApplicationAsync(It.Is<JobApplication>(jobApplication =>
            jobApplication.Id == 45 &&
            jobApplication.ResumeProfileJson == null &&
            jobApplication.ResumeProfileGeneratedOnUtc == null &&
            jobApplication.ResumeProfileError.Contains("empty_text"))), Times.Once);
        nopLogger.Verify(logger => logger.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview resume extraction failed",
            It.Is<string>(message =>
                message.Contains("ApplicationId=45") &&
                message.Contains("CustomerId=9") &&
                message.Contains("ProductId=15") &&
                message.Contains("ResumeDownloadId=88") &&
                message.Contains("FileExtension=.docx") &&
                message.Contains("FileSizeBytes=321") &&
                !message.Contains("Resume-backed candidate profile", StringComparison.OrdinalIgnoreCase) &&
                !message.Contains("Body content", StringComparison.OrdinalIgnoreCase)),
            null), Times.Once);
    }
}
