using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Models.Admin;
using Nop.Plugin.Misc.JobSupport.Models.Admin.Migration;
using Nop.Plugin.Misc.JobSupport.Services;
using Nop.Plugin.Misc.JobSupport.Services.Migration;
using Nop.Services.Localization;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;
using Nop.Services.Security;
using Nop.Services.Configuration;
using Nop.Plugin.Misc.JobSupport.Factories;
using Nop.Plugin.Misc.JobSupport.Infrastructure;
using Nop.Plugin.Misc.JobSupport.Domain.Enums;
using Nop.Services.Messages;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.JobSupport.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class JobSupportAdminController : BasePluginController
{
    private const string LEGACY_PARITY_VIEW_PATH = "~/Plugins/Misc.JobSupport/Views/JobSupportAdmin/LegacyParity.cshtml";
    private const string WORKFLOW_DIAGNOSTICS_VIEW_PATH = "~/Plugins/Misc.JobSupport/Views/JobSupportAdmin/WorkflowDiagnostics.cshtml";
    private const string CONFIGURE_VIEW_PATH = "~/Plugins/Misc.JobSupport/Views/JobSupportAdmin/Configure.cshtml";
    private const string PROFILES_VIEW_PATH = "~/Plugins/Misc.JobSupport/Views/JobSupportAdmin/Profiles.cshtml";
    private const string RELATIONSHIPS_VIEW_PATH = "~/Plugins/Misc.JobSupport/Views/JobSupportAdmin/Relationships.cshtml";
    private const string SUBSCRIPTIONS_VIEW_PATH = "~/Plugins/Misc.JobSupport/Views/JobSupportAdmin/Subscriptions.cshtml";
    private const string MIGRATION_VIEW_PATH = "~/Plugins/Misc.JobSupport/Views/JobSupportAdmin/MigrationStatus.cshtml";
    private const string CUTOVER_VIEW_PATH = "~/Plugins/Misc.JobSupport/Views/JobSupportAdmin/CutoverStatus.cshtml";

    private readonly ICustomerActivityService _customerActivityService;
    private readonly ICustomerService _customerService;
    private readonly IJobSupportProfileQueryService _profileQueryService;
    private readonly IJobSupportBackfillService _backfillService;
    private readonly IJobSupportReconciliationService _reconciliationService;
    private readonly IJobSupportCutoverService _cutoverService;
    private readonly ILocalizationService _localizationService;
    private readonly IMessageTemplateService _messageTemplateService;
    private readonly IPermissionService _permissionService;
    private readonly IProductService _productService;
    private readonly IScheduleTaskService _scheduleTaskService;
    private readonly JobSupportSettings _settings;
    private readonly IJobSupportAdminModelFactory _adminModelFactory;
    private readonly INotificationService _notificationService;
    private readonly ISettingService _settingService;

    public JobSupportAdminController(ICustomerActivityService customerActivityService,
        ICustomerService customerService,
        IJobSupportProfileQueryService profileQueryService,
        IJobSupportBackfillService backfillService,
        IJobSupportReconciliationService reconciliationService,
        IJobSupportCutoverService cutoverService,
        ILocalizationService localizationService,
        IMessageTemplateService messageTemplateService,
        IPermissionService permissionService,
        IProductService productService,
        IScheduleTaskService scheduleTaskService,
        IJobSupportAdminModelFactory adminModelFactory,
        INotificationService notificationService,
        ISettingService settingService,
        JobSupportSettings settings)
    {
        _customerActivityService = customerActivityService;
        _customerService = customerService;
        _profileQueryService = profileQueryService;
        _backfillService = backfillService;
        _reconciliationService = reconciliationService;
        _cutoverService = cutoverService;
        _localizationService = localizationService;
        _messageTemplateService = messageTemplateService;
        _permissionService = permissionService;
        _productService = productService;
        _scheduleTaskService = scheduleTaskService;
        _adminModelFactory = adminModelFactory;
        _notificationService = notificationService;
        _settingService = settingService;
        _settings = settings;
    }

    [CheckPermission(JobSupportPermissionConfigManager.MANAGE_PROFILES)]
    public IActionResult Configure()
    {
        return View(CONFIGURE_VIEW_PATH, _adminModelFactory.PrepareConfigurationModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [CheckPermission(JobSupportPermissionConfigManager.MANAGE_PROFILES)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return View(CONFIGURE_VIEW_PATH, model);
        _adminModelFactory.ApplyConfigurationModel(model);
        await _settingService.SaveSettingAsync(_settings);
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.JobSupport.Admin.Configuration.Saved"));
        return RedirectToRoute(JobSupportDefaults.ConfigurationRouteName);
    }

    [CheckPermission(JobSupportPermissionConfigManager.MANAGE_PROFILES)]
    public IActionResult Profiles()
    {
        return View(PROFILES_VIEW_PATH, _adminModelFactory.PrepareProfileSearchModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [CheckPermission(JobSupportPermissionConfigManager.MANAGE_PROFILES)]
    public async Task<IActionResult> ProfileList(ProfileSearchModel searchModel)
    {
        return Json(await _adminModelFactory.PrepareProfileListModelAsync(searchModel));
    }

    [CheckPermission(JobSupportPermissionConfigManager.MANAGE_RELATIONSHIPS)]
    public IActionResult Relationships()
    {
        return View(RELATIONSHIPS_VIEW_PATH);
    }

    [CheckPermission(JobSupportPermissionConfigManager.MANAGE_SUBSCRIPTIONS)]
    public IActionResult Subscriptions()
    {
        return View(SUBSCRIPTIONS_VIEW_PATH);
    }

    [CheckPermission(JobSupportPermissionConfigManager.VIEW_DIAGNOSTICS)]
    public async Task<IActionResult> Cutover()
    {
        return View(CUTOVER_VIEW_PATH, await _cutoverService.GetStatusAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CheckPermission(JobSupportPermissionConfigManager.VIEW_DIAGNOSTICS)]
    public async Task<IActionResult> Cutover(CutoverStatusModel model)
    {
        if (model.ReadMode is not (DataAccessMode.Legacy or DataAccessMode.Compare or DataAccessMode.Plugin))
            ModelState.AddModelError(nameof(model.ReadMode), "Select a supported read mode.");
        if (model.WriteMode is not (DataAccessMode.Legacy or DataAccessMode.Dual or DataAccessMode.Plugin))
            ModelState.AddModelError(nameof(model.WriteMode), "Select a supported write mode.");
        if (model.CompareReturnMode is not (DataAccessMode.Legacy or DataAccessMode.Plugin))
            ModelState.AddModelError(nameof(model.CompareReturnMode), "Select the path returned during comparison.");

        if (!ModelState.IsValid)
        {
            var status = await _cutoverService.GetStatusAsync();
            status.ReadMode = model.ReadMode;
            status.WriteMode = model.WriteMode;
            status.CompareReturnMode = model.CompareReturnMode;
            return View(CUTOVER_VIEW_PATH, status);
        }

        _settings.DataReadMode = model.ReadMode;
        _settings.DataWriteMode = model.WriteMode;
        _settings.CompareReturnMode = model.CompareReturnMode;
        await _settingService.SaveSettingAsync(_settings);
        return RedirectToRoute("Plugin.Misc.JobSupport.Cutover");
    }

    [CheckPermission(JobSupportPermissionConfigManager.VIEW_DIAGNOSTICS)]
    public async Task<IActionResult> Migration(CancellationToken cancellationToken)
    {
        var checkpoints = await _reconciliationService.GetCheckpointsAsync(cancellationToken);
        var stepNames = new[] { "Profiles", "SkillsAndAttributes", "Relationships", "ViewsAndReveals", "Subscriptions" };
        var model = new MigrationStatusModel
        {
            SchemaVersion = "1.00.002",
            ReadMode = _settings.DataReadMode,
            WriteMode = _settings.DataWriteMode,
            LastExecutionOnUtc = checkpoints.Max(checkpoint => checkpoint.LastExecutedOnUtc),
            MismatchCount = checkpoints.Sum(checkpoint => checkpoint.MismatchCount),
            Steps = stepNames.Select(name =>
            {
                var checkpoint = checkpoints.FirstOrDefault(item => item.MigrationName == name);
                return new MigrationStepStatusModel
                {
                    Name = name,
                    Status = checkpoint?.Status ?? "NotStarted",
                    LastProcessedId = checkpoint?.LastProcessedId ?? 0,
                    ProcessedCount = checkpoint?.ProcessedCount ?? 0,
                    SkippedCount = checkpoint?.SkippedCount ?? 0,
                    FailedCount = checkpoint?.FailedCount ?? 0,
                    MismatchCount = checkpoint?.MismatchCount ?? 0,
                    LastExecutionOnUtc = checkpoint?.LastExecutedOnUtc
                };
            }).ToList()
        };
        return View(MIGRATION_VIEW_PATH, model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CheckPermission(JobSupportPermissionConfigManager.VIEW_DIAGNOSTICS)]
    public async Task<IActionResult> ResumeMigration(string step, CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(1, _settings.MigrationBatchSize);
        _ = step switch
        {
            "Profiles" => await _backfillService.BackfillProfilesAsync(batchSize, cancellationToken),
            "SkillsAndAttributes" => await _backfillService.BackfillSkillsAsync(batchSize, cancellationToken),
            "Relationships" => await _backfillService.BackfillRelationshipsAsync(batchSize, cancellationToken),
            "ViewsAndReveals" => await _backfillService.BackfillViewsAndRevealsAsync(batchSize, cancellationToken),
            "Subscriptions" => await _backfillService.BackfillSubscriptionsAsync(batchSize, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(step))
        };
        return RedirectToRoute("Plugin.Misc.JobSupport.Migration");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CheckPermission(JobSupportPermissionConfigManager.VIEW_DIAGNOSTICS)]
    public async Task<IActionResult> CompareMigration(CancellationToken cancellationToken)
    {
        await _reconciliationService.ReconcileAsync(cancellationToken);
        return RedirectToRoute("Plugin.Misc.JobSupport.Migration");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CheckPermission(JobSupportPermissionConfigManager.VIEW_DIAGNOSTICS)]
    public async Task<IActionResult> ExportMigrationMismatches(CancellationToken cancellationToken)
    {
        var csv = await _reconciliationService.ExportSanitizedMismatchesAsync(cancellationToken);
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "job-support-mismatches.csv");
    }

    [CheckPermission(JobSupportPermissionConfigManager.VIEW_DIAGNOSTICS)]
    public async Task<IActionResult> LegacyParity()
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return AccessDeniedView();

        return View(LEGACY_PARITY_VIEW_PATH, new LegacyParityRequestModel
        {
            PageSize = _settings.DefaultPageSize > 0 ? _settings.DefaultPageSize : 12
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CheckPermission(JobSupportPermissionConfigManager.VIEW_DIAGNOSTICS)]
    public async Task<IActionResult> LegacyParity(LegacyParityRequestModel model)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return AccessDeniedView();

        if (!TryParseProductIds(model.ProductIdentifiers, out var productIds))
        {
            ModelState.AddModelError(nameof(model.ProductIdentifiers),
                await _localizationService.GetResourceAsync(
                    "Plugins.Misc.JobSupport.Admin.LegacyParity.Validation.ProductIds"));
        }

        if (!ModelState.IsValid)
            return View(LEGACY_PARITY_VIEW_PATH, model);

        var request = new ProfileSearchRequest
        {
            ProductIds = productIds,
            CustomerId = model.CustomerId,
            ProfileTypeId = model.ProfileTypeId,
            RelationshipType = model.RelationshipType,
            PageIndex = model.PageIndex,
            PageSize = model.PageSize,
            SortOrder = model.SortOrder
        };

        var procedureName = model.QueryType == LegacyParityQueryType.Relationship
            ? _settings.LegacyShortlistProcedureName
            : _settings.LegacyProfileSearchProcedureName;

        var stopwatch = Stopwatch.StartNew();
        var result = model.QueryType == LegacyParityQueryType.Relationship
            ? await _profileQueryService.GetProfilesByRelationshipAsync(request)
            : await _profileQueryService.SearchProfilesAsync(request);
        stopwatch.Stop();

        model.Result = new LegacyParityResultModel
        {
            Diagnostic = new ProfileQueryDiagnosticResult
            {
                ProcedureName = procedureName,
                Succeeded = result.Succeeded,
                ReturnedRowCount = result.ReturnedRowCount,
                OutputTotalRecords = result.OutputTotalRecords,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                ProfileIds = result.Items.Select(item => item.Id).ToList(),
                MappingWarnings = result.MappingWarnings.ToList(),
                ErrorCode = result.ErrorCode
            },
            Profiles = result.Items.Select(item => new LegacyParityProfilePresenceModel
            {
                ProfileId = item.Id,
                HasPhone = !string.IsNullOrWhiteSpace(item.Phone),
                HasEmail = !string.IsNullOrWhiteSpace(item.Email)
            }).ToList()
        };

        return View(LEGACY_PARITY_VIEW_PATH, model);
    }

    [CheckPermission(JobSupportPermissionConfigManager.VIEW_DIAGNOSTICS)]
    public async Task<IActionResult> WorkflowDiagnostics()
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return AccessDeniedView();

        var effectiveBase = _settings.Enabled && _settings.EnablePluginEventConsumers &&
                            _settings.ExecutionMode != WorkflowExecutionMode.Disabled;
        var task = await _scheduleTaskService.GetTaskByTypeAsync(JobSupportDefaults.SynchronizationTaskType);
        var templatePresent = (await _messageTemplateService.GetMessageTemplatesByNameAsync(
            JobSupportDefaults.CustomerAvailableMessageTemplateSystemName)).Any();
        var giveRole = await ResolveRoleNameAsync(_settings.GiveSupportRoleSystemName);
        var takeRole = await ResolveRoleNameAsync(_settings.TakeSupportRoleSystemName);
        var paidRole = await ResolveRoleNameAsync(_settings.PaidCustomerRoleSystemName);

        var model = new WorkflowDiagnosticsModel();
        AddDiagnostic(model, "Plugin enabled", _settings.Enabled);
        AddDiagnostic(model, "Event consumers enabled", _settings.EnablePluginEventConsumers);
        AddDiagnostic(model, "Execution mode", _settings.ExecutionMode.ToString());
        AddDiagnostic(model, "Registration workflow effective", effectiveBase && _settings.EnableRegistrationWorkflow);
        AddDiagnostic(model, "Activation workflow effective", effectiveBase && _settings.EnableActivationWorkflow);
        AddDiagnostic(model, "Paid order workflow effective", effectiveBase && _settings.EnableOrderPaidWorkflow);
        AddDiagnostic(model, "Availability workflow effective", effectiveBase && _settings.EnableAvailabilityWorkflow);
        AddDiagnostic(model, "Avatar synchronization effective", effectiveBase && _settings.EnableAvatarSyncWorkflow);
        AddDiagnostic(model, "Relationship notifications effective",
            _settings.Enabled && _settings.EnableRelationshipNotifications &&
            _settings.ExecutionMode != WorkflowExecutionMode.Disabled);
        AddDiagnostic(model, "Synchronization workflow effective",
            _settings.Enabled && _settings.EnableSynchronizationTask &&
            _settings.ExecutionMode != WorkflowExecutionMode.Disabled);
        AddDiagnostic(model, "Schedule task registered", task != null);
        AddDiagnostic(model, "Schedule task enabled", task?.Enabled ?? false);
        AddDiagnostic(model, "Message template present", templatePresent);
        AddDiagnostic(model, "Give support role", giveRole);
        AddDiagnostic(model, "Take support role", takeRole);
        AddDiagnostic(model, "Paid customer role", paidRole);
        AddDiagnostic(model, "Three month subscription product",
            await ResolveProductNameAsync(_settings.ThreeMonthSubscriptionProductId));
        AddDiagnostic(model, "Six month subscription product",
            await ResolveProductNameAsync(_settings.SixMonthSubscriptionProductId));
        AddDiagnostic(model, "One year subscription product",
            await ResolveProductNameAsync(_settings.OneYearSubscriptionProductId));

        var activityType = (await _customerActivityService.GetAllActivityTypesAsync())
            .FirstOrDefault(type => type.SystemKeyword == JobSupportDefaults.ActivityTypeSystemName);
        if (activityType != null)
        {
            var activities = await _customerActivityService.GetAllActivitiesAsync(
                activityLogTypeId: activityType.Id,
                pageIndex: 0,
                pageSize: 20);
            model.Activities = activities.Select(activity => new WorkflowActivityModel
            {
                CreatedOnUtc = activity.CreatedOnUtc,
                ActivityType = activityType.Name,
                EntityName = activity.EntityName,
                EntityId = activity.EntityId
            }).ToList();
        }

        return View(WORKFLOW_DIAGNOSTICS_VIEW_PATH, model);
    }

    private async Task<string> ResolveRoleNameAsync(string systemName)
    {
        if (string.IsNullOrWhiteSpace(systemName))
            return "Not configured";
        return (await _customerService.GetCustomerRoleBySystemNameAsync(systemName))?.Name ?? "Not resolved";
    }

    private async Task<string> ResolveProductNameAsync(int productId)
    {
        if (productId <= 0)
            return "Not configured";
        return (await _productService.GetProductByIdAsync(productId))?.Name ?? "Not resolved";
    }

    private static void AddDiagnostic(WorkflowDiagnosticsModel model, string name, bool value)
    {
        AddDiagnostic(model, name, value ? "Enabled" : "Disabled");
    }

    private static void AddDiagnostic(WorkflowDiagnosticsModel model, string name, string value)
    {
        model.Configuration.Add(new WorkflowDiagnosticItemModel { Name = name, Value = value });
    }

    private static bool TryParseProductIds(string value, out IList<int> productIds)
    {
        productIds = new List<int>();
        if (string.IsNullOrWhiteSpace(value))
            return true;

        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, out var productId) || productId <= 0)
                return false;

            productIds.Add(productId);
        }

        return true;
    }
}
