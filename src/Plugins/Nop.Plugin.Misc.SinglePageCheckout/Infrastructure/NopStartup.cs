using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.SinglePageCheckout.Factories;
using Nop.Plugin.Misc.SinglePageCheckout.Filters;
using Nop.Web.Factories;

namespace Nop.Plugin.Misc.SinglePageCheckout.Infrastructure;

public class NopStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISinglePageCheckoutPageModelFactory, SinglePageCheckoutPageModelFactory>();
        services.AddScoped<SinglePageCheckoutModelTuner>();
        services.AddScoped<CheckoutModelFactory>();

        // Wrap/decorate standard ICheckoutModelFactory
        services.AddScoped<ICheckoutModelFactory, SinglePageCheckoutCheckoutModelFactory>();

        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add(typeof(BypassCartActionFilter));
        });
    }

    public void Configure(IApplicationBuilder application)
    {
    }

    public int Order => 3000;
}
