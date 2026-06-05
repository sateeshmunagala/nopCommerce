using Moq;
using NUnit.Framework;
using Nop.Plugin.Misc.AIInterview;
using Nop.Plugin.Misc.AIInterview.Infrastructure;
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
    public async Task HandleEventAsync_AddsMyApplications_WhenPluginEnabled()
    {
        // Arrange
        var localizationService = new Mock<ILocalizationService>();
        localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.MyApplications.Title")).ReturnsAsync("My Applications");

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
        Assert.That(addedItem.RouteName, Is.EqualTo(AIInterviewDefaults.MyApplicationsRouteName));
        Assert.That(addedItem.Title, Is.EqualTo("My Applications"));
        Assert.That(addedItem.ItemClass, Is.EqualTo("customer-applications"));
        Assert.That(addedItem.Tab, Is.EqualTo((int)CustomerNavigationEnum.Info + 100));
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
}
