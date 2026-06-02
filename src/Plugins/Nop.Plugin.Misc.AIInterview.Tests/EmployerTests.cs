using Microsoft.AspNetCore.Mvc;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class EmployerTests
{
    private Mock<IApplicationService> _applicationService;
    private Mock<IInterviewSessionService> _interviewSessionService;
    private Mock<ICustomerService> _customerService;
    private Mock<IWorkContext> _workContext;
    private Mock<INotificationService> _notificationService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<IProductService> _productService;
    private Mock<ISponsorInviteService> _inviteService;
    private Mock<ICreditService> _creditService;
    private AIInterviewEmployerController _controller;
    private Customer _employer;

    [SetUp]
    public void SetUp()
    {
        _applicationService = new Mock<IApplicationService>();
        _interviewSessionService = new Mock<IInterviewSessionService>();
        _customerService = new Mock<ICustomerService>();
        _workContext = new Mock<IWorkContext>();
        _notificationService = new Mock<INotificationService>();
        _localizationService = new Mock<ILocalizationService>();
        _productService = new Mock<IProductService>();
        _inviteService = new Mock<ISponsorInviteService>();
        _creditService = new Mock<ICreditService>();

        _employer = new Customer { Id = 123, VendorId = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(_employer);

        _customerService.Setup(x => x.IsAdminAsync(It.IsAny<Customer>(), It.IsAny<bool>())).ReturnsAsync(false);

        _creditService.Setup(x => x.GetOrCreateWalletAsync(It.IsAny<int>())).ReturnsAsync(new CreditWallet { Balance = 500 });

        _controller = new AIInterviewEmployerController(
            _applicationService.Object,
            _interviewSessionService.Object,
            _customerService.Object,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _productService.Object,
            _inviteService.Object,
            _creditService.Object);
    }

    [Test]
    public async Task List_Unauthorized_ReturnsChallenge()
    {
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(new Customer { Id = 456, VendorId = 0 });
        var result = await _controller.List(new ApplicationListModel());
        Assert.That(result, Is.TypeOf<ChallengeResult>());
    }

    [Test]
    public async Task List_FiltersCorrectly()
    {
        var model = new ApplicationListModel { CandidateNameOrEmail = "John", Status = "Pending" };
        var applications = new PagedList<JobApplication>(new List<JobApplication> { new JobApplication { Id = 1, CustomerId = 789 } }, 0, 10);

        _applicationService.Setup(x => x.GetApplicationsAsync(
            "John", "Pending", null, null, null, null, 0, 1, 0, 10, false))
            .ReturnsAsync(applications);

        _customerService.Setup(x => x.GetCustomerByIdAsync(789)).ReturnsAsync(new Customer { Email = "john@example.com" });

        var result = await _controller.List(model);

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var resultModel = (ApplicationListModel)viewResult.Model;
        Assert.That(resultModel.Applications.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task UpdateStatus_SavesCorrectly()
    {
        var application = new JobApplication { Id = 1, Status = "Applied", ProductId = 10 };
        _applicationService.Setup(x => x.GetJobApplicationByIdAsync(1)).ReturnsAsync(application);
        _productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 10, VendorId = 1 });

        var model = new UpdateStatusModel { Id = 1, Status = "Shortlisted", StatusComment = "Great candidate" };
        var result = await _controller.UpdateStatus(model);

        _applicationService.Verify(x => x.UpdateJobApplicationAsync(It.Is<JobApplication>(a => a.Status == "Shortlisted" && a.StatusComment == "Great candidate")), Times.Once);
        Assert.That(result, Is.TypeOf<RedirectToActionResult>());
    }

    [Test]
    public async Task ExportCsv_OutputShape()
    {
        var applications = new PagedList<JobApplication>(new List<JobApplication> {
            new JobApplication { Id = 1, CustomerId = 789, CreatedOnUtc = new DateTime(2024, 1, 1) }
        }, 0, 10);

        _applicationService.Setup(x => x.GetApplicationsAsync(null, null, null, null, null, null, 0, 1, 0, int.MaxValue, false))
            .ReturnsAsync(applications);
        _customerService.Setup(x => x.GetCustomerByIdAsync(789)).ReturnsAsync(new Customer { FirstName = "John", LastName = "Doe", Email = "john@example.com" });

        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ID")).ReturnsAsync("ID");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Candidate")).ReturnsAsync("Candidate");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Email")).ReturnsAsync("Email");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.History.Status")).ReturnsAsync("Status");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.History.Score")).ReturnsAsync("Score");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.History.Date")).ReturnsAsync("Date");

        var result = await _controller.ExportCsv(new ApplicationListModel());

        Assert.That(result, Is.TypeOf<FileContentResult>());
        var fileResult = (FileContentResult)result;
        var csv = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);
        Assert.That(csv, Does.Contain("ID,Candidate,Email,Status,Score,Date"));
        Assert.That(csv, Does.Contain("1,\"John Doe\",\"john@example.com\""));
    }

    [Test]
    public async Task SponsorInvites_ReturnsViewWithBalance()
    {
        var invites = new List<SponsorInvite> { new SponsorInvite { Id = 1, Email = "invited@test.com" } };
        _inviteService.Setup(x => x.GetSponsorInvitesAsync(123)).ReturnsAsync(invites);

        var result = await _controller.SponsorInvites();

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        Assert.That(viewResult.ViewData["CreditBalance"], Is.EqualTo(500m));
        Assert.That(viewResult.Model, Is.EqualTo(invites));
    }

    [Test]
    public async Task CreateInvite_Flow_Success()
    {
        var result = await _controller.CreateInvite("invited@test.com", 10, 1, null);

        _inviteService.Verify(x => x.CreateInviteAsync(123, "invited@test.com", 10, 1, null), Times.Once);
        Assert.That(result, Is.TypeOf<RedirectToActionResult>());
    }

    [Test]
    public async Task DeactivateInvite_Flow_Success()
    {
        var result = await _controller.DeactivateInvite(1);

        _inviteService.Verify(x => x.DeactivateInviteAsync(1, 123), Times.Once);
        Assert.That(result, Is.TypeOf<RedirectToActionResult>());
    }

    [Test]
    public async Task CreateInvite_Unauthorized_ReturnsChallenge()
    {
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(new Customer { Id = 456, VendorId = 0 });
        var result = await _controller.CreateInvite("invited@test.com", 10, 1, null);
        Assert.That(result, Is.TypeOf<ChallengeResult>());
    }
}
