using Moq;
using NUnit.Framework;
using Nop.Plugin.Misc.AIInterview;
using Nop.Plugin.Misc.AIInterview.Infrastructure;
using Nop.Core.Http;
using Nop.Services.Localization;
using Nop.Web.Framework.Events;
using Nop.Web.Models.Customer;
using System.Threading.Tasks;
using System.Linq;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class EventConsumerTests
{
    [Test]
    public async Task HandleEventAsync_AddsMyActivity_WhenPluginEnabled()
    {
        // Arrange
        var localizationService = new Mock<ILocalizationService>();
        localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.MyActivity.Title")).ReturnsAsync("My Activity");

        var creditService = new Mock<Services.ICreditService>();
        var workContext = new Mock<Nop.Core.IWorkContext>();
        var aiInterviewSettings = new AIInterviewSettings { Enabled = true };

        var consumer = new EventConsumer(localizationService.Object, creditService.Object, workContext.Object, aiInterviewSettings);

        var model = new CustomerNavigationModel();
        var eventMessage = new ModelPreparedEvent<Nop.Web.Framework.Models.BaseNopModel>(model);

        // Act
        await consumer.HandleEventAsync(eventMessage);

        // Assert
        Assert.That(model.CustomerNavigationItems.Count, Is.EqualTo(1));
        var addedItem = model.CustomerNavigationItems.First();
        Assert.That(addedItem.RouteName, Is.EqualTo(AIInterviewDefaults.MyActivityRouteName));
        Assert.That(addedItem.Title, Is.EqualTo("My Activity"));
        Assert.That(addedItem.ItemClass, Is.EqualTo("customer-my-activity"));
        Assert.That(addedItem.Tab, Is.EqualTo(AIInterviewDefaults.MyActivityNavigationTab));
    }

    [Test]
    public async Task HandleEventAsync_DoesNotAddMyApplications_WhenPluginDisabled()
    {
        // Arrange
        var localizationService = new Mock<ILocalizationService>();
        var creditService = new Mock<Services.ICreditService>();
        var workContext = new Mock<Nop.Core.IWorkContext>();
        var aiInterviewSettings = new AIInterviewSettings { Enabled = false };

        var consumer = new EventConsumer(localizationService.Object, creditService.Object, workContext.Object, aiInterviewSettings);

        var model = new CustomerNavigationModel();
        var eventMessage = new ModelPreparedEvent<Nop.Web.Framework.Models.BaseNopModel>(model);

        // Act
        await consumer.HandleEventAsync(eventMessage);

        // Assert
        Assert.That(model.CustomerNavigationItems.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task HandleEventAsync_DoesNotDuplicateNavigationItems()
    {
        var localizationService = new Mock<ILocalizationService>();
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string resourceKey) => resourceKey);

        var creditService = new Mock<Services.ICreditService>();
        var workContext = new Mock<Nop.Core.IWorkContext>();
        workContext.Setup(x => x.GetCurrentCustomerAsync())
            .ReturnsAsync(new Nop.Core.Domain.Customers.Customer { Id = 1, VendorId = 2 });
        var consumer = new EventConsumer(localizationService.Object, creditService.Object, workContext.Object,
            new AIInterviewSettings { Enabled = true });
        var model = new CustomerNavigationModel();
        var eventMessage = new ModelPreparedEvent<Nop.Web.Framework.Models.BaseNopModel>(model);

        await consumer.HandleEventAsync(eventMessage);
        await consumer.HandleEventAsync(eventMessage);

        Assert.Multiple(() =>
        {
            Assert.That(model.CustomerNavigationItems.Count(item => item.RouteName == AIInterviewDefaults.MyActivityRouteName), Is.EqualTo(1));
            Assert.That(model.CustomerNavigationItems.Count(item => item.RouteName == AIInterviewDefaults.EmployerDashboardRouteName), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleEventAsync_Removes_Legacy_Vendor_Info_When_Employer_Dashboard_Is_Used()
    {
        var localizationService = new Mock<ILocalizationService>();
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string resourceKey) => resourceKey);

        var creditService = new Mock<Services.ICreditService>();
        var workContext = new Mock<Nop.Core.IWorkContext>();
        workContext.Setup(x => x.GetCurrentCustomerAsync())
            .ReturnsAsync(new Nop.Core.Domain.Customers.Customer { Id = 1, VendorId = 2 });
        var consumer = new EventConsumer(localizationService.Object, creditService.Object, workContext.Object,
            new AIInterviewSettings { Enabled = true });
        var model = new CustomerNavigationModel();
        model.CustomerNavigationItems.Add(new CustomerNavigationItemModel
        {
            RouteName = NopRouteNames.Standard.CUSTOMER_VENDOR_INFO,
            Title = "Employer Profile",
            Tab = 999,
            ItemClass = "customer-vendor-info"
        });

        await consumer.HandleEventAsync(new ModelPreparedEvent<Nop.Web.Framework.Models.BaseNopModel>(model));

        Assert.That(model.CustomerNavigationItems.Any(item => item.RouteName == NopRouteNames.Standard.CUSTOMER_VENDOR_INFO), Is.False);
        Assert.That(model.CustomerNavigationItems.Count(item => item.RouteName == AIInterviewDefaults.EmployerDashboardRouteName), Is.EqualTo(1));
    }
}
