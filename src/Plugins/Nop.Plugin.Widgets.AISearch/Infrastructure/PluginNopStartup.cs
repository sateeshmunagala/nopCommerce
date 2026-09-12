using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Widgets.AISearch.Services;

namespace Nop.Plugin.Widgets.AISearch.Infrastructure;

public class PluginNopStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        services.AddScoped<IProductContentBuilder, ProductContentBuilder>();
        services.AddScoped<IAzureOpenAiEmbeddingClient, AzureOpenAiEmbeddingClient>();
        services.AddScoped<IProductEmbeddingService, ProductEmbeddingService>();
        services.AddScoped<ProductEmbeddingSyncTask>();
    }

    public void Configure(IApplicationBuilder application)
    {
    }

    public int Order => 3100;
}
