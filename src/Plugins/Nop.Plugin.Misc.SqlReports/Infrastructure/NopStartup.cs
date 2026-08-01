using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.SqlReports.Admin.Factories;
using Nop.Plugin.Misc.SqlReports.Services;

namespace Nop.Plugin.Misc.SqlReports.Infrastructure;

public class NopStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SqlReportModelFactory>();
        services.AddScoped<SqlReportService>();
        services.AddScoped<SqlReportExecutionService>();
    }

    public void Configure(IApplicationBuilder application)
    {
    }

    public int Order => 3000;
}
