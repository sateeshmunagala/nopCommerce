using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.JobSupport.Services;

namespace Nop.Plugin.Misc.JobSupport.Infrastructure;

public class PluginNopStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IJobSupportProfileQueryService, JobSupportProfileQueryService>();
        services.AddScoped<IJobSupportLegacyParityService, JobSupportLegacyParityService>();
        services.AddScoped<IJobSupportProfileService, JobSupportProfileService>();
        services.AddScoped<IJobSupportRelationshipService, JobSupportRelationshipService>();
        services.AddScoped<IJobSupportSubscriptionService, JobSupportSubscriptionService>();
        services.AddScoped<IJobSupportAffiliateService, JobSupportAffiliateService>();
        services.AddScoped<IJobSupportNotificationService, JobSupportNotificationService>();
        services.AddScoped<JobSupportSynchronizationTask>();
    }

    public void Configure(IApplicationBuilder application)
    {
    }

    public int Order => 1;
}
