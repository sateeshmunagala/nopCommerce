using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.WhatsAppBusiness.Services;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Infrastructure;

public class NopStartup : INopStartup
{
	public int Order => 3000;

	public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
	{
		services.AddHttpClient(WhatsAppBusinessDefaults.HttpClientName);
		services.AddScoped<IWhatsAppBusinessService, WhatsAppBusinessService>();
		services.AddScoped<IWhatsAppNotificationService, WhatsAppNotificationService>();
	}

	public void Configure(IApplicationBuilder application)
	{
	}
}
