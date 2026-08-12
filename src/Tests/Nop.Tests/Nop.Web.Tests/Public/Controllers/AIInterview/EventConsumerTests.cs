using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview;
using Nop.Plugin.Misc.AIInterview.Infrastructure;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Models;
using Nop.Web.Models.Customer;
using NUnit.Framework;

namespace Nop.Tests.Nop.Web.Tests.Public.Controllers.AIInterview;

[TestFixture]
public class EventConsumerTests
{
    private Mock<ILocalizationService> _localizationService;
    private Mock<ICreditService> _creditService;
    private Mock<IWorkContext> _workContext;
    private Mock<ICustomerService> _customerService;
    private AIInterviewSettings _settings;
    private EventConsumer _eventConsumer;

    [SetUp]
    public void SetUp()
    {
        _localizationService = new Mock<ILocalizationService>();
        _creditService = new Mock<ICreditService>();
        _workContext = new Mock<IWorkContext>();
        _customerService = new Mock<ICustomerService>();
        _settings = new AIInterviewSettings { Enabled = true };

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key);
        _workContext.Setup(x => x.GetCurrentCustomerAsync())
            .ReturnsAsync(new Customer { Id = 1, VendorId = 0 });

        _eventConsumer = new EventConsumer(
            _localizationService.Object,
            _creditService.Object,
            _workContext.Object,
            _customerService.Object,
            _settings);
    }

    [Test]
    public async Task HandleEventAsync_Replaces_Legacy_Customer_Activity_Links()
    {
        // Arrange
        var navigationModel = new CustomerNavigationModel
        {
            CustomerNavigationItems =
            {
                new CustomerNavigationItemModel { RouteName = AIInterviewDefaults.MyApplicationsRouteName, Title = "Legacy applications", Tab = AIInterviewDefaults.MyApplicationsNavigationTab },
                new CustomerNavigationItemModel { RouteName = AIInterviewDefaults.MockHistoryRouteName, Title = "Legacy mock history", Tab = AIInterviewDefaults.MyActivityNavigationTab },
                new CustomerNavigationItemModel { RouteName = "customer/info", Title = "Info", Tab = 0 }
            }
        };

        // Act
        await _eventConsumer.HandleEventAsync(new ModelPreparedEvent<BaseNopModel>(navigationModel));

        // Assert
        Assert.That(navigationModel.CustomerNavigationItems.Any(item => item.RouteName == AIInterviewDefaults.MyApplicationsRouteName), Is.False);
        Assert.That(navigationModel.CustomerNavigationItems.Any(item => item.RouteName == AIInterviewDefaults.MockHistoryRouteName), Is.False);

        var myActivityItem = navigationModel.CustomerNavigationItems.SingleOrDefault(item => item.RouteName == AIInterviewDefaults.MyActivityRouteName);
        Assert.That(myActivityItem, Is.Not.Null);
        Assert.That(myActivityItem.Title, Is.EqualTo("Plugins.Misc.AIInterview.MyActivity.Title"));
        Assert.That(myActivityItem.Tab, Is.EqualTo(AIInterviewDefaults.MyActivityNavigationTab));
        Assert.That(navigationModel.CustomerNavigationItems.Any(item => item.RouteName == "customer/info"), Is.True);
    }
}
