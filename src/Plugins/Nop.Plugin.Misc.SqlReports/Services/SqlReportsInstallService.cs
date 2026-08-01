using Nop.Core.Domain.Logging;
using Nop.Data;
using Nop.Services.Configuration;
using Nop.Services.Localization;

namespace Nop.Plugin.Misc.SqlReports.Services;

public class SqlReportsInstallService
{
    private readonly IRepository<ActivityLogType> _activityLogTypeRepository;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingService _settingService;

    public SqlReportsInstallService(IRepository<ActivityLogType> activityLogTypeRepository,
        ILocalizationService localizationService,
        ISettingService settingService)
    {
        _activityLogTypeRepository = activityLogTypeRepository;
        _localizationService = localizationService;
        _settingService = settingService;
    }

    public virtual async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new SqlReportsSettings
        {
            MaxRowsPerQuery = 500,
            CommandTimeoutSeconds = 30,
            MaxCellLength = 4000,
            EnableInstantQuery = true,
            AllowExport = true
        });

        await InsertActivityLogTypeAsync(SqlReportsDefaults.ActivityLogTypeSystemNames.AddReport, "SQL reports. Add report");
        await InsertActivityLogTypeAsync(SqlReportsDefaults.ActivityLogTypeSystemNames.EditReport, "SQL reports. Edit report");
        await InsertActivityLogTypeAsync(SqlReportsDefaults.ActivityLogTypeSystemNames.DeleteReport, "SQL reports. Delete report");
        await InsertActivityLogTypeAsync(SqlReportsDefaults.ActivityLogTypeSystemNames.RunReport, "SQL reports. Run report");
        await InsertActivityLogTypeAsync(SqlReportsDefaults.ActivityLogTypeSystemNames.ExportReport, "SQL reports. Export report");
    }

    public virtual async Task UninstallAsync()
    {
        await _settingService.DeleteSettingAsync<SqlReportsSettings>();
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.SqlReports.");

        var keywords = new[]
        {
            SqlReportsDefaults.ActivityLogTypeSystemNames.AddReport,
            SqlReportsDefaults.ActivityLogTypeSystemNames.EditReport,
            SqlReportsDefaults.ActivityLogTypeSystemNames.DeleteReport,
            SqlReportsDefaults.ActivityLogTypeSystemNames.RunReport,
            SqlReportsDefaults.ActivityLogTypeSystemNames.ExportReport
        };

        await _activityLogTypeRepository.DeleteAsync(type => keywords.Contains(type.SystemKeyword));
    }

    protected virtual async Task InsertActivityLogTypeAsync(string systemKeyword, string name)
    {
        var exists = await _activityLogTypeRepository.Table.AnyAsync(type => type.SystemKeyword == systemKeyword);
        if (exists)
            return;

        await _activityLogTypeRepository.InsertAsync(new ActivityLogType
        {
            SystemKeyword = systemKeyword,
            Name = name,
            Enabled = true
        });
    }
}
