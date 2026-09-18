using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview.Components;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Vendors;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class VendorPortalLogoIsolationTests
{
    private static void SetHttpContext(ViewComponent component, HttpContext httpContext)
    {
        component.ViewComponentContext = new ViewComponentContext
        {
            ViewContext = new ViewContext
            {
                HttpContext = httpContext
            }
        };
    }

    [Test]
    public async Task Widget_Registration_Maps_Vendor_Logo_Once_And_Preserves_Header_Links_After()
    {
        var plugin = new AIInterviewPlugin(
            new Mock<ILocalizationService>().Object,
            new Mock<ISettingService>().Object,
            new Mock<IWebHelper>().Object,
            new Mock<IMessageTemplateService>().Object);

        var zones = await plugin.GetWidgetZonesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(zones.Count(zone => string.Equals(zone, AIInterviewDefaults.VendorPortalLogoWidgetZone, StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
            Assert.That(zones.Count(zone => string.Equals(zone, "header_links_after", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
            Assert.That(plugin.GetWidgetViewComponent(AIInterviewDefaults.VendorPortalLogoWidgetZone), Is.EqualTo(typeof(VendorPortalLogoViewComponent)));
            Assert.That(plugin.GetWidgetViewComponent("header_links_after"), Is.EqualTo(typeof(VendorPortalHeaderLinksViewComponent)));
        });
    }

    [Test]
    public async Task Vendor_Logo_Returns_No_Content_On_Ordinary_Page_Without_Resolving_Services()
    {
        var workContext = new Mock<IWorkContext>();
        var customerService = new Mock<ICustomerService>();
        var pictureService = new Mock<IPictureService>();
        var vendorService = new Mock<IVendorService>();
        var component = new VendorPortalLogoViewComponent(
            pictureService.Object,
            vendorService.Object,
            workContext.Object,
            customerService.Object);
        SetHttpContext(component, new DefaultHttpContext());

        var result = await component.InvokeAsync();

        Assert.That(result, Is.TypeOf<ContentViewComponentResult>());
        Assert.That(((ContentViewComponentResult)result).Content, Is.Empty);
        workContext.VerifyNoOtherCalls();
        customerService.VerifyNoOtherCalls();
        pictureService.VerifyNoOtherCalls();
        vendorService.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Vendor_Portal_Marker_Exposes_Provided_Branding_For_Authenticated_Candidate()
    {
        var customer = new Customer { Id = 7, Email = "candidate@example.com" };
        var workContext = new Mock<IWorkContext>();
        var customerService = new Mock<ICustomerService>();
        workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        customerService.Setup(service => service.IsGuestAsync(customer, true)).ReturnsAsync(false);
        var component = new VendorPortalLogoViewComponent(
            new Mock<IPictureService>().Object,
            new Mock<IVendorService>().Object,
            workContext.Object,
            customerService.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Items[AIInterviewDefaults.IsVendorPortalPageKey] = true;
        httpContext.Items[AIInterviewDefaults.VendorPortalLogoUrlKey] = "/images/sponsor-logo.png";
        SetHttpContext(component, httpContext);

        var result = await component.InvokeAsync();

        Assert.That(result, Is.TypeOf<ViewViewComponentResult>());
        Assert.That(((ViewViewComponentResult)result).ViewData.Model, Is.EqualTo("/images/sponsor-logo.png"));
    }

    [Test]
    public async Task Portal_Marker_Does_Not_Trigger_Branding_Or_Credit_Services_For_Guest()
    {
        var guest = new Customer { Id = 6, VendorId = 12 };
        var workContext = new Mock<IWorkContext>();
        var customerService = new Mock<ICustomerService>();
        var pictureService = new Mock<IPictureService>();
        var vendorService = new Mock<IVendorService>();
        var creditService = new Mock<ICreditService>();
        workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(guest);
        customerService.Setup(service => service.IsGuestAsync(guest, true)).ReturnsAsync(true);
        var httpContext = new DefaultHttpContext();
        httpContext.Items[AIInterviewDefaults.IsVendorPortalPageKey] = true;
        httpContext.Items[AIInterviewDefaults.VendorPortalLogoUrlKey] = "/images/guest-logo.png";
        var logoComponent = new VendorPortalLogoViewComponent(
            pictureService.Object,
            vendorService.Object,
            workContext.Object,
            customerService.Object);
        var headerLinksComponent = new VendorPortalHeaderLinksViewComponent(
            workContext.Object,
            creditService.Object,
            customerService.Object);
        SetHttpContext(logoComponent, httpContext);
        SetHttpContext(headerLinksComponent, httpContext);

        var logoResult = await logoComponent.InvokeAsync();
        var headerLinksResult = await headerLinksComponent.InvokeAsync("header_links_after", null);

        Assert.That(logoResult, Is.TypeOf<ContentViewComponentResult>());
        Assert.That(((ContentViewComponentResult)logoResult).Content, Is.Empty);
        Assert.That(headerLinksResult, Is.TypeOf<ContentViewComponentResult>());
        Assert.That(((ContentViewComponentResult)headerLinksResult).Content, Is.Empty);
        pictureService.VerifyNoOtherCalls();
        vendorService.VerifyNoOtherCalls();
        creditService.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Missing_Vendor_Logo_Uses_Plugin_View_Standard_Logo_Fallback()
    {
        var customer = new Customer { Id = 8, VendorId = 12, Email = "vendor@example.com" };
        var vendor = new Nop.Core.Domain.Vendors.Vendor { Id = 12, PictureId = 0 };
        var workContext = new Mock<IWorkContext>();
        var customerService = new Mock<ICustomerService>();
        var vendorService = new Mock<IVendorService>();
        workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        customerService.Setup(service => service.IsGuestAsync(customer, true)).ReturnsAsync(false);
        vendorService.Setup(service => service.GetVendorByIdAsync(customer.VendorId)).ReturnsAsync(vendor);
        var component = new VendorPortalLogoViewComponent(
            new Mock<IPictureService>().Object,
            vendorService.Object,
            workContext.Object,
            customerService.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Items[AIInterviewDefaults.IsVendorPortalPageKey] = true;
        SetHttpContext(component, httpContext);

        var result = await component.InvokeAsync();
        var viewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath(
            "Views", "Shared", "Components", "VendorPortalLogo", "Default.cshtml"));

        Assert.That(result, Is.TypeOf<ViewViewComponentResult>());
        Assert.That(((ViewViewComponentResult)result).ViewData.Model, Is.Null);
        Assert.That(viewText, Does.Contain("Component.InvokeAsync(typeof(Nop.Web.Components.LogoViewComponent))"));
    }

    [Test]
    public async Task Header_Links_Are_Gated_To_Authenticated_Portal_Requests()
    {
        var customer = new Customer { Id = 9, FirstName = "Vendor", LastName = "User" };
        var workContext = new Mock<IWorkContext>();
        var creditService = new Mock<ICreditService>();
        var customerService = new Mock<ICustomerService>();
        workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        customerService.Setup(service => service.IsGuestAsync(customer, true)).ReturnsAsync(false);
        creditService.Setup(service => service.GetOrCreateWalletAsync(customer.Id)).ReturnsAsync(new CreditWallet { Balance = 5 });
        var component = new VendorPortalHeaderLinksViewComponent(
            workContext.Object,
            creditService.Object,
            customerService.Object);

        SetHttpContext(component, new DefaultHttpContext());
        var ordinaryResult = await component.InvokeAsync("header_links_after", null);

        var portalContext = new DefaultHttpContext();
        portalContext.Items[AIInterviewDefaults.IsVendorPortalPageKey] = true;
        SetHttpContext(component, portalContext);
        var portalResult = await component.InvokeAsync("header_links_after", null);

        Assert.That(ordinaryResult, Is.TypeOf<ContentViewComponentResult>());
        Assert.That(((ContentViewComponentResult)ordinaryResult).Content, Is.Empty);
        Assert.That(portalResult, Is.TypeOf<ViewViewComponentResult>());
        Assert.That(((VendorPortalHeaderLinksModel)((ViewViewComponentResult)portalResult).ViewData.Model).Balance, Is.EqualTo(5));
        creditService.Verify(service => service.GetOrCreateWalletAsync(customer.Id), Times.Once);
    }

    [Test]
    public void Active_Theme_Uses_Widget_Isolation_And_Standard_Logo_Fallback()
    {
        var themeHeaderPath = Path.GetFullPath(Path.Combine(
            TestFilePathHelper.GetPluginRootPath(),
            "..", "..", "Presentation", "Nop.Web", "Themes", "JobBoardVenture", "Views", "Shared", "_Header.cshtml"));
        var headerText = File.ReadAllText(themeHeaderPath);

        Assert.That(headerText, Does.Contain("widgetZone = \"aiinterview_vendor_portal_logo\""));
        Assert.That(headerText, Does.Contain("@if (hasVendorPortalLogo)"));
        Assert.That(headerText, Does.Contain("@vendorPortalLogo"));
        Assert.That(headerText, Does.Contain("Component.InvokeAsync(typeof(LogoViewComponent))"));
        Assert.That(headerText, Does.Not.Contain("Component.InvokeAsync(\"VendorPortalLogo\")"));
        Assert.That(headerText, Does.Contain("non-overridden themes still require a separate restoration of"));
        Assert.That(headerText, Does.Contain("remains theme-owned so AIInterview never introduces a core-to-plugin dependency"));

        var pluginLogoViewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath(
            "Views", "Shared", "Components", "VendorPortalLogo", "Default.cshtml"));
        Assert.That(pluginLogoViewText, Does.Contain("Component.InvokeAsync(typeof(Nop.Web.Components.LogoViewComponent))"));
        Assert.That(pluginLogoViewText, Does.Not.Contain("Component.InvokeAsync(\"VendorPortalLogo\")"));
    }
}
