using System.Linq.Expressions;
using Moq;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Vendors;
using Nop.Web.Framework.Mvc.Routing;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class SponsoredInterviewsTests
{
    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    private sealed class TestAIInterviewController : AIInterviewController
    {
        public TestAIInterviewController(
            IApplicationService applicationService,
            IInterviewSessionService interviewSessionService,
            IWorkContext workContext,
            INotificationService notificationService,
            ILocalizationService localizationService,
            IDownloadService downloadService,
            ICustomerService customerService,
            IProductService productService,
            IJobRequirementService jobRequirementService,
            INopUrlHelper nopUrlHelper,
            IJobProductAccessService jobProductAccessService,
            ISponsorInviteService inviteService,
            IVendorService vendorService)
            : base(
                applicationService,
                interviewSessionService,
                new AIInterviewSettings { Enabled = true },
                workContext,
                notificationService,
                localizationService,
                downloadService,
                customerService,
                productService,
                jobRequirementService,
                nopUrlHelper: nopUrlHelper,
                jobProductAccessService: jobProductAccessService,
                inviteService: inviteService,
                vendorService: vendorService)
        {
        }

        public string NormalizeTab(string tab) => NormalizeMyActivityTab(tab);

        public Task<MyActivityPageModel> BuildPageAsync(Customer customer, string tab) =>
            BuildMyActivityPageModelAsync(customer, tab, null);
    }

    private static void SetupRepositoryQuery<TEntity>(Mock<IRepository<TEntity>> repository, IList<TEntity> entities)
        where TEntity : BaseEntity
    {
        repository
            .Setup(instance => instance.GetAllAsync(
                It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(),
                It.IsAny<Func<ICacheKeyService, CacheKey>>(),
                It.IsAny<bool>()))
            .ReturnsAsync((
                Func<IQueryable<TEntity>, IQueryable<TEntity>> query,
                Func<ICacheKeyService, CacheKey> cacheKeyFactory,
                bool includeDeleted) => query(entities.AsQueryable()).ToList());
    }

    [Test]
    public async Task Candidate_Query_Returns_Only_Exact_Active_Unexpired_Unexhausted_Invites_In_Defined_Order()
    {
        var now = DateTime.UtcNow;
        var invites = new List<SponsorInvite>
        {
            new() { Id = 1, SponsorId = 10, ProductId = 20, Email = "candidate@example.com", InviteCode = "later", MaxAttempts = 2, IsActive = true, ExpiryDateUtc = now.AddDays(2), CreatedOnUtc = now.AddHours(-1) },
            new() { Id = 2, SponsorId = 10, ProductId = 20, Email = "candidate@example.com", InviteCode = "earlier", MaxAttempts = 2, IsActive = true, ExpiryDateUtc = now.AddDays(1), CreatedOnUtc = now.AddHours(-2) },
            new() { Id = 3, SponsorId = 10, ProductId = 20, Email = "candidate@example.com", InviteCode = "no-expiry", MaxAttempts = 1, IsActive = true, CreatedOnUtc = now },
            new() { Id = 4, SponsorId = 10, ProductId = 20, Email = "candidate@example.com", InviteCode = "distinct-code", MaxAttempts = 1, IsActive = true, ExpiryDateUtc = now.AddDays(3), CreatedOnUtc = now },
            new() { Id = 5, SponsorId = 10, ProductId = 20, Email = "candidate@example.com", InviteCode = "earlier", MaxAttempts = 2, IsActive = true, ExpiryDateUtc = now.AddDays(1), CreatedOnUtc = now.AddDays(-2) },
            new() { Id = 6, SponsorId = 10, ProductId = 20, Email = "candidate@example.com", InviteCode = "exhausted", MaxAttempts = 1, IsActive = true, ExpiryDateUtc = now.AddDays(4), CreatedOnUtc = now },
            new() { Id = 7, SponsorId = 10, ProductId = 20, Email = "other@example.com", InviteCode = "wrong-candidate", MaxAttempts = 1, IsActive = true, ExpiryDateUtc = now.AddDays(1), CreatedOnUtc = now },
            new() { Id = 8, SponsorId = 10, ProductId = 20, Email = "candidate@example.com", InviteCode = "expired", MaxAttempts = 1, IsActive = true, ExpiryDateUtc = now.AddSeconds(-1), CreatedOnUtc = now },
            new() { Id = 9, SponsorId = 10, ProductId = 20, Email = "candidate@example.com", InviteCode = "inactive", MaxAttempts = 1, IsActive = false, ExpiryDateUtc = now.AddDays(1), CreatedOnUtc = now },
            new() { Id = 10, SponsorId = 10, ProductId = 20, Email = "candidate@example.com", InviteCode = "accepted", MaxAttempts = 1, IsActive = true, IsAccepted = true, ExpiryDateUtc = now.AddDays(1), CreatedOnUtc = now }
        };
        var inviteRepository = new Mock<IRepository<SponsorInvite>>();
        SetupRepositoryQuery(inviteRepository, invites);
        var sessionService = new Mock<IInterviewSessionService>();
        sessionService.Setup(service => service.GetSponsorInviteAttemptCountAsync(It.IsAny<int>()))
            .ReturnsAsync((int inviteId) => inviteId == 6 ? 1 : 0);
        var service = new SponsorInviteService(
            inviteRepository.Object,
            new Mock<IProductService>().Object,
            new Mock<ICustomerService>().Object,
            new Mock<ILocalizationService>().Object,
            interviewSessionService: sessionService.Object);

        var result = await service.GetActiveEligibleInvitesByCandidateEmailAsync(" candidate@example.com ");

        Assert.That(result.Select(invite => invite.Id), Is.EqualTo(new[] { 2, 1, 4, 3 }));
        Assert.That(result.Count(invite => invite.SponsorId == 10 && invite.ProductId == 20), Is.EqualTo(4));
        Assert.That(result.Select(invite => invite.InviteCode), Does.Contain("later").And.Contain("distinct-code"));
    }

    [Test]
    public async Task Candidate_Query_Matches_Mixed_Case_And_Whitespace_Normalized_Email()
    {
        var invite = new SponsorInvite
        {
            Id = 11,
            SponsorId = 10,
            ProductId = 20,
            Email = "  Candidate@Example.COM  ",
            InviteCode = "mixed-case",
            MaxAttempts = 2,
            IsActive = true,
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
            CreatedOnUtc = DateTime.UtcNow
        };
        var inviteRepository = new Mock<IRepository<SponsorInvite>>();
        SetupRepositoryQuery(inviteRepository, new List<SponsorInvite> { invite });
        var sessionService = new Mock<IInterviewSessionService>();
        sessionService.Setup(service => service.GetSponsorInviteAttemptCountAsync(invite.Id)).ReturnsAsync(0);
        var service = new SponsorInviteService(
            inviteRepository.Object,
            new Mock<IProductService>().Object,
            new Mock<ICustomerService>().Object,
            new Mock<ILocalizationService>().Object,
            interviewSessionService: sessionService.Object);

        var result = await service.GetActiveEligibleInvitesByCandidateEmailAsync(" candidate@example.com ");
        var isValid = await service.ValidateInviteAsync(invite.InviteCode, "CANDIDATE@example.com");

        Assert.That(result.Select(item => item.Id), Is.EqualTo(new[] { invite.Id }));
        Assert.That(isValid, Is.True);
    }

    [Test]
    public async Task Candidate_Query_Returns_Eligible_Invites_When_Optional_Session_Service_Is_Absent()
    {
        var invite = new SponsorInvite
        {
            Id = 12,
            SponsorId = 10,
            ProductId = 20,
            Email = "candidate@example.com",
            InviteCode = "optional-session-service",
            MaxAttempts = 1,
            IsActive = true,
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
            CreatedOnUtc = DateTime.UtcNow
        };
        var inviteRepository = new Mock<IRepository<SponsorInvite>>();
        SetupRepositoryQuery(inviteRepository, new List<SponsorInvite> { invite });
        var service = new SponsorInviteService(
            inviteRepository.Object,
            new Mock<IProductService>().Object,
            new Mock<ICustomerService>().Object,
            new Mock<ILocalizationService>().Object);

        var result = await service.GetActiveEligibleInvitesByCandidateEmailAsync("candidate@example.com");

        Assert.That(result.Select(item => item.Id), Is.EqualTo(new[] { invite.Id }));
    }

    [Test]
    public async Task Default_Tab_Maps_Eligible_Invitation_And_Encodes_Sponsor_Token_In_Product_Link()
    {
        var createdOnUtc = DateTime.UtcNow.AddHours(-2);
        var customer = new Customer { Id = 7, Email = "candidate@example.com" };
        var product = new Product { Id = 20, VendorId = 30, Name = "Platform Engineer" };
        var vendor = new Vendor { Id = 30, Name = "Acme Labs" };
        var invite = new SponsorInvite
        {
            Id = 40,
            SponsorId = 10,
            ProductId = product.Id,
            Email = customer.Email,
            InviteCode = "invite+&token",
            MaxAttempts = 2,
            IsActive = true,
            CreatedOnUtc = createdOnUtc,
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1)
        };
        var applicationService = new Mock<IApplicationService>();
        var sessionService = new Mock<IInterviewSessionService>();
        var workContext = new Mock<IWorkContext>();
        var notificationService = new Mock<INotificationService>();
        var localizationService = new Mock<ILocalizationService>();
        var downloadService = new Mock<IDownloadService>();
        var customerService = new Mock<ICustomerService>();
        var productService = new Mock<IProductService>();
        var requirementService = new Mock<IJobRequirementService>();
        var nopUrlHelper = new Mock<INopUrlHelper>();
        var accessService = new Mock<IJobProductAccessService>();
        var inviteService = new Mock<ISponsorInviteService>();
        var vendorService = new Mock<IVendorService>();

        inviteService.Setup(service => service.GetActiveEligibleInvitesByCandidateEmailAsync(customer.Email)).ReturnsAsync(new List<SponsorInvite> { invite });
        productService.Setup(service => service.GetProductsByIdsAsync(It.Is<int[]>(ids => ids.SequenceEqual(new[] { product.Id })))).ReturnsAsync(new List<Product> { product });
        accessService.Setup(service => service.CanAcceptJobApplicationsAsync(product)).ReturnsAsync(true);
        nopUrlHelper.Setup(helper => helper.RouteGenericUrlAsync(product, null, null, null)).ReturnsAsync("/jobs/platform-engineer");
        vendorService.Setup(service => service.GetVendorByIdAsync(vendor.Id)).ReturnsAsync(vendor);
        localizationService.Setup(service => service.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key.EndsWith("CompanyFallback", StringComparison.Ordinal) ? "Sponsoring company" : "Ready to interview");
        localizationService.Setup(service => service.GetLocalizedAsync(
                product,
                It.IsAny<Expression<Func<Product, string>>>(),
                null,
                true,
                true))
            .ReturnsAsync(product.Name);
        localizationService.Setup(service => service.GetLocalizedAsync(
                vendor,
                It.IsAny<Expression<Func<Vendor, string>>>(),
                null,
                true,
                true))
            .ReturnsAsync(vendor.Name);

        var controller = new TestAIInterviewController(
            applicationService.Object,
            sessionService.Object,
            workContext.Object,
            notificationService.Object,
            localizationService.Object,
            downloadService.Object,
            customerService.Object,
            productService.Object,
            requirementService.Object,
            nopUrlHelper.Object,
            accessService.Object,
            inviteService.Object,
            vendorService.Object);

        var model = await controller.BuildPageAsync(customer, null);

        Assert.That(model.ActiveTab, Is.EqualTo(AIInterviewDefaults.MyActivitySponsoredInterviewsTabKey));
        Assert.That(model.SponsoredInterviews, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(model.SponsoredInterviews[0].CompanyName, Is.EqualTo("Acme Labs"));
            Assert.That(model.SponsoredInterviews[0].JobTitle, Is.EqualTo("Platform Engineer"));
            Assert.That(model.SponsoredInterviews[0].CreatedOnUtc, Is.EqualTo(createdOnUtc));
            Assert.That(model.SponsoredInterviews[0].InterviewUrl, Is.EqualTo("/jobs/platform-engineer?sponsorToken=invite%2B%26token"));
        });
    }

    [Test]
    public void Tab_Normalization_Preserves_All_Existing_Values_And_Falls_Back_To_Sponsored()
    {
        var controller = new TestAIInterviewController(
            new Mock<IApplicationService>().Object,
            new Mock<IInterviewSessionService>().Object,
            new Mock<IWorkContext>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ILocalizationService>().Object,
            new Mock<IDownloadService>().Object,
            new Mock<ICustomerService>().Object,
            new Mock<IProductService>().Object,
            new Mock<IJobRequirementService>().Object,
            new Mock<INopUrlHelper>().Object,
            new Mock<IJobProductAccessService>().Object,
            new Mock<ISponsorInviteService>().Object,
            new Mock<IVendorService>().Object);

        Assert.Multiple(() =>
        {
            Assert.That(controller.NormalizeTab(null), Is.EqualTo(AIInterviewDefaults.MyActivitySponsoredInterviewsTabKey));
            Assert.That(controller.NormalizeTab("unknown"), Is.EqualTo(AIInterviewDefaults.MyActivitySponsoredInterviewsTabKey));
            Assert.That(controller.NormalizeTab(AIInterviewDefaults.MyActivityAppliedJobsTabKey), Is.EqualTo(AIInterviewDefaults.MyActivityAppliedJobsTabKey));
            Assert.That(controller.NormalizeTab(AIInterviewDefaults.MyActivitySavedJobsTabKey), Is.EqualTo(AIInterviewDefaults.MyActivitySavedJobsTabKey));
            Assert.That(controller.NormalizeTab(AIInterviewDefaults.MyActivityMockInterviewsTabKey), Is.EqualTo(AIInterviewDefaults.MyActivityMockInterviewsTabKey));
            Assert.That(controller.NormalizeTab(AIInterviewDefaults.MyActivityCreditsTabKey), Is.EqualTo(AIInterviewDefaults.MyActivityCreditsTabKey));
        });
    }

    [Test]
    public void Candidate_View_Renders_Created_Date_In_Both_Layouts_Without_Exposing_InviteCode()
    {
        var viewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_MyActivitySponsoredInterviewsContent.cshtml"));

        Assert.That(viewText, Does.Contain("ConvertToUserTimeAsync"));
        Assert.That(CountOccurrences(viewText, "FormatCreatedAsync(invitation.CreatedOnUtc)"), Is.EqualTo(2));
        Assert.That(viewText, Does.Contain("MyActivity.SponsoredInterviews.Created"));
        Assert.That(viewText, Does.Contain("createdOnUtc.Value == default"));
        Assert.That(viewText, Does.Contain("Plugins.Misc.AIInterview.Common.None"));
        Assert.That(viewText, Does.Contain("sponsored-interviews-table"));
        Assert.That(viewText, Does.Contain("sponsored-interviews-mobile-list"));
        Assert.That(viewText, Does.Contain("aria-label"));
        Assert.That(viewText, Does.Not.Contain("InviteCode"));
    }

    [Test]
    public void My_Activity_View_Uses_Model_ActiveTab_And_Clears_Stale_Htmx_State()
    {
        var viewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MyActivity.cshtml"));
        var panelText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_MyActivityTabContent.cshtml"));

        Assert.Multiple(() =>
        {
            Assert.That(CountOccurrences(viewText, "class=\"my-activity-tab @(Model.ActiveTab =="), Is.EqualTo(5));
            Assert.That(CountOccurrences(viewText, "aria-current=\"@(Model.ActiveTab =="), Is.EqualTo(5));
            Assert.That(viewText, Does.Contain("tab.classList.remove('is-active')"));
            Assert.That(viewText, Does.Contain("tab.removeAttribute('aria-current')"));
            Assert.That(viewText, Does.Contain("tab.classList.add('is-active')"));
            Assert.That(viewText, Does.Contain("tab.setAttribute('aria-current', 'page')"));
            Assert.That(viewText, Does.Contain("htmx:beforeRequest"));
            Assert.That(viewText, Does.Contain("htmx:afterSwap"));
            Assert.That(viewText, Does.Contain("htmx:responseError"));
            Assert.That(viewText, Does.Contain("htmx:historyRestore"));
            Assert.That(viewText, Does.Contain("popstate"));
            Assert.That(viewText, Does.Contain("searchParams.get('tab')"));
            Assert.That(panelText, Does.Contain("data-active-tab=\"@Model.ActiveTab\""));
        });
    }

    [Test]
    public void Sponsored_Layouts_Use_Mutually_Exclusive_Desktop_And_Mobile_Rules()
    {
        var cssText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Content", "css", "aiinterview-public.css"));
        var mobileBreakpointIndex = cssText.IndexOf("@media (max-width: 640px)", StringComparison.Ordinal);
        var nextBreakpointIndex = cssText.IndexOf("@media (max-width: 480px)", mobileBreakpointIndex, StringComparison.Ordinal);
        var desktopCss = cssText[..mobileBreakpointIndex];
        var mobileCss = cssText[mobileBreakpointIndex..nextBreakpointIndex];

        Assert.Multiple(() =>
        {
            Assert.That(desktopCss, Does.Contain(".html-aiinterview-my-activity-page .sponsored-interviews-mobile-list"));
            Assert.That(desktopCss, Does.Contain("display: none;"));
            Assert.That(mobileCss, Does.Contain(".html-aiinterview-my-activity-page .sponsored-interviews-table-wrap"));
            Assert.That(mobileCss, Does.Contain(".html-aiinterview-my-activity-page .sponsored-interviews-mobile-list"));
            Assert.That(mobileCss, Does.Contain("display: none;"));
            Assert.That(mobileCss, Does.Contain("display: grid;"));
        });
    }
}
