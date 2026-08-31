using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;

namespace Nop.Plugin.Misc.JobSupport;

public class JobSupportPlugin : BasePlugin, IMiscPlugin
{
    private readonly ILocalizationService _localizationService;
    private readonly ISettingService _settingService;

    public JobSupportPlugin(ILocalizationService localizationService,
        ISettingService settingService)
    {
        _localizationService = localizationService;
        _settingService = settingService;
    }

    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new JobSupportSettings
        {
            Enabled = false,
            UseLegacyStoredProcedures = false,
            LegacyProfileSearchProcedureName = "ProductLoadAllPaged_V6",
            LegacyShortlistProcedureName = "ProductShortList",
            DefaultPageSize = 12
        });
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Misc.JobSupport.FriendlyName"] = "Job Support",
            ["Plugins.Misc.JobSupport.Configuration"] = "Job Support configuration",
            ["Plugins.Misc.JobSupport.Fields.Enabled"] = "Enabled",
            ["Plugins.Misc.JobSupport.Disabled"] = "Job Support is currently disabled.",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Title"] = "Legacy profile query diagnostic",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Description"] = "Run a plugin-owned read against a configured legacy profile procedure.",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.QueryType"] = "Query type",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.ProductIds"] = "Product identifiers",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.CustomerId"] = "Customer identifier",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.ProfileTypeId"] = "Profile type identifier",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.RelationshipType"] = "Relationship type",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.PageIndex"] = "Page index",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.PageSize"] = "Page size",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.SortOrder"] = "Sort order",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.QueryTypes.ProfileSearch"] = "Profile search",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.QueryTypes.Relationship"] = "Relationship search",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.ShortlistedByMe"] = "Shortlisted by me",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.ShortlistedMe"] = "Shortlisted me",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.InterestSent"] = "Interest sent",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.InterestReceived"] = "Interest received",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.AcceptedByMe"] = "Accepted by me",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.AcceptedMe"] = "Accepted me",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.DeclinedByMe"] = "Declined by me",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.DeclinedMe"] = "Declined me",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.BlockedByMe"] = "Blocked by me",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.BlockedMe"] = "Blocked me",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.ViewedByMe"] = "Viewed by me",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.RelationshipTypes.ViewedMe"] = "Viewed me",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Run"] = "Run diagnostic",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Results"] = "Diagnostic result",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.Metric"] = "Metric",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.Value"] = "Value",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.ProcedureName"] = "Procedure name",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.Success"] = "Success",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.ReturnedRows"] = "Returned rows",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.OutputTotal"] = "Output total records",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.DurationMilliseconds"] = "Duration (milliseconds)",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.ProfileIds"] = "Profile identifiers",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.ErrorCode"] = "Error code",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.MappingWarnings"] = "Mapping warnings",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.Warning"] = "Warning",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.ContactPresence"] = "Contact field presence",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.ProfileId"] = "Profile identifier",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.Phone"] = "Phone",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Result.Email"] = "Email",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Status.Succeeded"] = "Succeeded",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Status.Failed"] = "Failed",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Presence.Present"] = "Present",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Presence.Missing"] = "Missing",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.NotAvailable"] = "Not available",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.None"] = "None",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.Validation.ProductIds"] = "Enter positive product identifiers separated by commas.",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.ErrorCodes.None"] = "None",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.ErrorCodes.Disabled"] = "Disabled",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.ErrorCodes.UnsupportedProvider"] = "Unsupported provider",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.ErrorCodes.MissingProcedureName"] = "Missing procedure name",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.ErrorCodes.InvalidRequest"] = "Invalid request",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.ErrorCodes.NotSupported"] = "Not supported",
            ["Plugins.Misc.JobSupport.Admin.LegacyParity.ErrorCodes.ProcedureExecutionFailed"] = "Procedure execution failed"
        });
        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        await _settingService.DeleteSettingAsync<JobSupportSettings>();
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.JobSupport");
        await base.UninstallAsync();
    }
}
