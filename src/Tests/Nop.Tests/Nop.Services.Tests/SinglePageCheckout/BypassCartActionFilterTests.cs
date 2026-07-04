using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using NUnit.Framework;
using Nop.Plugin.Misc.SinglePageCheckout;
using Nop.Plugin.Misc.SinglePageCheckout.Filters;
using Nop.Services.Configuration;

namespace Nop.Tests.Nop.Services.Tests.SinglePageCheckout
{
    [TestFixture]
    public class BypassCartActionFilterTests
    {
        [Test]
        public async Task ActionFilter_Redirects_When_BypassCart_Is_Enabled()
        {
            var settingService = new Mock<ISettingService>();
            settingService.Setup(s => s.LoadSettingAsync<SinglePageCheckoutSettings>(It.IsAny<int>())).ReturnsAsync(new SinglePageCheckoutSettings
            {
                Enabled = true,
                BypassCart = true
            });

            var filter = new BypassCartActionFilter(settingService.Object);

            var controller = new CheckoutController();
            var actionContext = new ActionContext(
                new DefaultHttpContext(),
                new RouteData(new RouteValueDictionary(new { action = "Index" })),
                new ActionDescriptor
                {
                    RouteValues = new Dictionary<string, string> { { "action", "Index" } }
                }
            );

            var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object>(), controller);

            await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller)));

            context.Result.Should().BeOfType<RedirectToRouteResult>();
            var redirectResult = context.Result as RedirectToRouteResult;
            redirectResult.RouteName.Should().Be(SinglePageCheckoutDefaults.CheckoutRouteName);
        }

        [Test]
        public async Task ActionFilter_DoesNot_Redirect_When_BypassCart_Is_Disabled()
        {
            var settingService = new Mock<ISettingService>();
            settingService.Setup(s => s.LoadSettingAsync<SinglePageCheckoutSettings>(It.IsAny<int>())).ReturnsAsync(new SinglePageCheckoutSettings
            {
                Enabled = true,
                BypassCart = false
            });

            var filter = new BypassCartActionFilter(settingService.Object);

            var controller = new CheckoutController();
            var actionContext = new ActionContext(
                new DefaultHttpContext(),
                new RouteData(new RouteValueDictionary(new { action = "Index" })),
                new ActionDescriptor
                {
                    RouteValues = new Dictionary<string, string> { { "action", "Index" } }
                }
            );

            var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object>(), controller);

            await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller)));

            context.Result.Should().BeNull();
        }

        public class CheckoutController : Controller
        {
        }
    }
}