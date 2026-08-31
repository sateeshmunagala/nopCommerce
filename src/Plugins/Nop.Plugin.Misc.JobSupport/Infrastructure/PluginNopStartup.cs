using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.JobSupport.Services;
using Nop.Plugin.Misc.JobSupport.Factories;
using Microsoft.AspNetCore.Mvc;

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
        services.AddScoped<IJobSupportProfileModelFactory, JobSupportProfileModelFactory>();
        services.AddScoped<IJobSupportAccountModelFactory, JobSupportAccountModelFactory>();
        services.AddScoped<IJobSupportAdminModelFactory, JobSupportAdminModelFactory>();
        services.AddScoped<JobSupportRegistrationResultFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<JobSupportRegistrationResultFilter>());
        services.AddScoped<JobSupportSynchronizationTask>();
    }

    public void Configure(IApplicationBuilder application)
    {
    }

    public int Order => 1;
}
