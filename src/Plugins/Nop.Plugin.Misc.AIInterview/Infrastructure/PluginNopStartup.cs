using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.AIInterview.Services;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

/// <summary>
/// Represents the object for the configuring services on application startup
/// </summary>
public class PluginNopStartup : INopStartup
{
    /// <summary>
    /// Add and configure any of the middleware
    /// </summary>
    /// <param name="services">Collection of service descriptors</param>
    /// <param name="configuration">Configuration of the application</param>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IInterviewSessionService, InterviewSessionService>();
        services.AddScoped<IInterviewTurnService, InterviewTurnService>();
        services.AddScoped<IResumeFileService, ResumeFileService>();
        services.AddScoped<IResumeTextExtractionService, ResumeTextExtractionService>();
        services.AddScoped<IResumeProfileService, ResumeProfileService>();
        services.AddScoped<IAzureUsageService, AzureUsageService>();
        services.AddScoped<IAIInterviewClient, InterviewAiClient>();
        services.AddScoped<IInterviewRuntimeService, InterviewRuntimeService>();
        services.AddScoped<ICreditService, CreditService>();
        services.AddScoped<ICreditActivityService, CreditActivityService>();
        services.AddScoped<ICreditDepositNotificationService, CreditDepositNotificationService>();
        services.AddScoped<ICreditPurchaseService, CreditPurchaseService>();
        services.AddScoped<ISponsorInviteService, SponsorInviteService>();
        services.AddScoped<IJobInterviewExperienceService, JobInterviewExperienceService>();
        services.AddScoped<IJobRequirementService, JobRequirementService>();
        services.AddScoped<IJobProductAccessService, JobProductAccessService>();
        services.AddScoped<IAIInterviewJobDisplayService, AIInterviewJobDisplayService>();
    }

    /// <summary>
    /// Configure the using of added middleware
    /// </summary>
    /// <param name="application">Builder for configuring an application's request pipeline</param>
    public void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Gets order of this startup configuration implementation
    /// </summary>
    public int Order => 1;
}
