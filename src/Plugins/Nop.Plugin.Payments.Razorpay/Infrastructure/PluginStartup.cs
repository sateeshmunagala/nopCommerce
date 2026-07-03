using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Payments.Razorpay.Services;

namespace Nop.Plugin.Payments.Razorpay.Infrastructure;

public class PluginStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<RazorpayHttpClient>();
    }

    public void Configure(IApplicationBuilder application)
    {
    }

    public int Order => 1;
}
