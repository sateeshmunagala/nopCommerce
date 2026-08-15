using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Infrastructure;

public class RouteProvider : IRouteProvider
{
	public int Priority => 0;

	public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
	{
		endpointRouteBuilder.MapControllerRoute(WhatsAppBusinessDefaults.ConfigurationRouteName, "Admin/WhatsAppBusiness/Configure", new
		{
			controller = "WhatsAppBusiness",
			action = "Configure",
			area = "Admin"
		});
	}
}
