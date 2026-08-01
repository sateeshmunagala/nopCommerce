using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.SqlReports.Admin.Factories;
using Nop.Plugin.Misc.SqlReports.Admin.Models;
using Nop.Plugin.Misc.SqlReports.Domain;
using Nop.Plugin.Misc.SqlReports.Services;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.SqlReports.Admin.Controllers;

[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
[ValidateIpAddress]
[AuthorizeAdmin]
[SaveSelectedTab]
public class SqlReportsAdminController : BasePluginController
{
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IWorkContext _workContext;
    private readonly SqlReportExecutionService _executionService;
    private readonly SqlReportModelFactory _modelFactory;
    private readonly SqlReportService _sqlReportService;

    public SqlReportsAdminController(ILocalizationService localizationService,
        INotificationService notificationService,
        IWorkContext workContext,
        SqlReportExecutionService executionService,
        SqlReportModelFactory modelFactory,
        SqlReportService sqlReportService)
    {
        _localizationService = localizationService;
        _notificationService = notificationService;
        _workContext = workContext;
        _executionService = executionService;
        _modelFactory = modelFactory;
        _sqlReportService = sqlReportService;
    }

    public IActionResult Index()
    {
        return RedirectToAction(nameof(Reports));
    }

    [CheckPermission(SqlReportsDefaults.Permissions.RunReports)]
    public async Task<IActionResult> Reports()
    {
        var model = await _modelFactory.PrepareReportSearchModelAsync(new SqlReportSearchModel());

        return View("~/Plugins/Misc.SqlReports/Admin/Views/Reports.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(SqlReportsDefaults.Permissions.RunReports)]
    public async Task<IActionResult> List(SqlReportSearchModel searchModel)
    {
        var model = await _modelFactory.PrepareReportListModelAsync(searchModel);

        return Json(model);
    }

    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> ReportCreate()
    {
        var model = await _modelFactory.PrepareReportModelAsync(new SqlReportModel(), null);

        return View("~/Plugins/Misc.SqlReports/Admin/Views/ReportCreate.cshtml", model);
    }

    [HttpPost]
    [ParameterBasedOnFormName("save-continue", "continueEditing")]
    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> ReportCreate(SqlReportModel model, bool continueEditing)
    {
        ValidateSqlQuery(model.SqlQuery);

        if (ModelState.IsValid)
        {
            var report = new SqlReport
            {
                Name = model.Name,
                SystemName = model.SystemName,
                Description = model.Description,
                SqlQuery = model.SqlQuery,
                IsActive = model.IsActive,
                DisplayOrder = model.DisplayOrder,
                CreatedOnUtc = DateTime.UtcNow,
                UpdatedOnUtc = DateTime.UtcNow
            };

            await _sqlReportService.InsertReportAsync(report);
            await _sqlReportService.SaveReportCustomerRoleMappingsAsync(report.Id, model.SelectedCustomerRoleIds);
            await _sqlReportService.SaveReportParameterMappingsAsync(report.Id, model.SelectedParameterIds);

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Common.DataSuccessfullySaved"));

            return continueEditing ? RedirectToAction(nameof(ReportEdit), new { id = report.Id }) : RedirectToAction(nameof(Reports));
        }

        model = await _modelFactory.PrepareReportModelAsync(model, null, true);

        return View("~/Plugins/Misc.SqlReports/Admin/Views/ReportCreate.cshtml", model);
    }

    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> ReportEdit(int id)
    {
        var report = await _sqlReportService.GetReportByIdAsync(id);
        if (report == null)
            return RedirectToAction(nameof(Reports));

        var model = await _modelFactory.PrepareReportModelAsync(null, report);

        return View("~/Plugins/Misc.SqlReports/Admin/Views/ReportEdit.cshtml", model);
    }

    [HttpPost]
    [ParameterBasedOnFormName("save-continue", "continueEditing")]
    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> ReportEdit(SqlReportModel model, bool continueEditing)
    {
        var report = await _sqlReportService.GetReportByIdAsync(model.Id);
        if (report == null)
            return RedirectToAction(nameof(Reports));

        ValidateSqlQuery(model.SqlQuery);

        if (ModelState.IsValid)
        {
            report.Name = model.Name;
            report.SystemName = model.SystemName;
            report.Description = model.Description;
            report.SqlQuery = model.SqlQuery;
            report.IsActive = model.IsActive;
            report.DisplayOrder = model.DisplayOrder;
            report.UpdatedOnUtc = DateTime.UtcNow;

            await _sqlReportService.UpdateReportAsync(report);
            await _sqlReportService.SaveReportCustomerRoleMappingsAsync(report.Id, model.SelectedCustomerRoleIds);
            await _sqlReportService.SaveReportParameterMappingsAsync(report.Id, model.SelectedParameterIds);

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Common.DataSuccessfullySaved"));

            return continueEditing ? RedirectToAction(nameof(ReportEdit), new { id = report.Id }) : RedirectToAction(nameof(Reports));
        }

        model = await _modelFactory.PrepareReportModelAsync(model, report, true);

        return View("~/Plugins/Misc.SqlReports/Admin/Views/ReportEdit.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> ReportDelete(int id)
    {
        var report = await _sqlReportService.GetReportByIdAsync(id);
        if (report == null)
            return RedirectToAction(nameof(Reports));

        await _sqlReportService.DeleteReportAsync(report);
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Common.DataSuccessfullyDeleted"));

        return RedirectToAction(nameof(Reports));
    }

    [CheckPermission(SqlReportsDefaults.Permissions.RunReports)]
    public async Task<IActionResult> Run(int id)
    {
        var report = await GetRunnableReportAsync(id);
        if (report == null)
            return AccessDeniedView();

        var model = await _modelFactory.PrepareRunModelAsync(report);

        return View("~/Plugins/Misc.SqlReports/Admin/Views/Run.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(SqlReportsDefaults.Permissions.RunReports)]
    public async Task<IActionResult> Run(SqlReportRunModel model)
    {
        var report = await GetRunnableReportAsync(model.Id);
        if (report == null)
            return AccessDeniedView();

        model = await _modelFactory.PrepareRunModelAsync(report, model);
        model.Result = await ExecuteReportAsync(report, model);

        return View("~/Plugins/Misc.SqlReports/Admin/Views/Run.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(SqlReportsDefaults.Permissions.RunReports)]
    public async Task<IActionResult> Export(int id, SqlReportRunModel model)
    {
        var report = await GetRunnableReportAsync(id);
        if (report == null)
            return AccessDeniedView();

        var result = await ExecuteReportAsync(report, model, int.MaxValue);
        if (!string.IsNullOrEmpty(result.Error))
        {
            _notificationService.ErrorNotification(result.Error);
            return RedirectToAction(nameof(Run), new { id });
        }

        var bytes = _executionService.ExportToXlsx(ToExecutionResult(result));

        return File(bytes, MimeTypes.TextXlsx, $"{SanitizeFileName(report.Name)}.xlsx");
    }

    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> Parameters()
    {
        var model = await _modelFactory.PrepareParameterSearchModelAsync(new SqlReportParameterSearchModel());

        return View("~/Plugins/Misc.SqlReports/Admin/Views/Parameters.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> ParameterList(SqlReportParameterSearchModel searchModel)
    {
        var model = await _modelFactory.PrepareParameterListModelAsync(searchModel);

        return Json(model);
    }

    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> ParameterCreate()
    {
        var model = await _modelFactory.PrepareParameterModelAsync(new SqlReportParameterModel(), null);

        return View("~/Plugins/Misc.SqlReports/Admin/Views/ParameterCreate.cshtml", model);
    }

    [HttpPost]
    [ParameterBasedOnFormName("save-continue", "continueEditing")]
    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> ParameterCreate(SqlReportParameterModel model, bool continueEditing)
    {
        if (ModelState.IsValid)
        {
            var parameter = new SqlReportParameter
            {
                Name = model.Name,
                ParameterName = NormalizeParameterName(model.ParameterName),
                DataType = model.DataType,
                DefaultValue = model.DefaultValue,
                Prompt = model.Prompt,
                IsRequired = model.IsRequired,
                DisplayOrder = model.DisplayOrder
            };

            await _sqlReportService.InsertParameterAsync(parameter);
            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Common.DataSuccessfullySaved"));

            return continueEditing ? RedirectToAction(nameof(ParameterEdit), new { id = parameter.Id }) : RedirectToAction(nameof(Parameters));
        }

        model = await _modelFactory.PrepareParameterModelAsync(model, null);

        return View("~/Plugins/Misc.SqlReports/Admin/Views/ParameterCreate.cshtml", model);
    }

    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> ParameterEdit(int id)
    {
        var parameter = await _sqlReportService.GetParameterByIdAsync(id);
        if (parameter == null)
            return RedirectToAction(nameof(Parameters));

        var model = await _modelFactory.PrepareParameterModelAsync(null, parameter);

        return View("~/Plugins/Misc.SqlReports/Admin/Views/ParameterEdit.cshtml", model);
    }

    [HttpPost]
    [ParameterBasedOnFormName("save-continue", "continueEditing")]
    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> ParameterEdit(SqlReportParameterModel model, bool continueEditing)
    {
        var parameter = await _sqlReportService.GetParameterByIdAsync(model.Id);
        if (parameter == null)
            return RedirectToAction(nameof(Parameters));

        if (ModelState.IsValid)
        {
            parameter.Name = model.Name;
            parameter.ParameterName = NormalizeParameterName(model.ParameterName);
            parameter.DataType = model.DataType;
            parameter.DefaultValue = model.DefaultValue;
            parameter.Prompt = model.Prompt;
            parameter.IsRequired = model.IsRequired;
            parameter.DisplayOrder = model.DisplayOrder;

            await _sqlReportService.UpdateParameterAsync(parameter);
            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Common.DataSuccessfullySaved"));

            return continueEditing ? RedirectToAction(nameof(ParameterEdit), new { id = parameter.Id }) : RedirectToAction(nameof(Parameters));
        }

        model = await _modelFactory.PrepareParameterModelAsync(model, parameter);

        return View("~/Plugins/Misc.SqlReports/Admin/Views/ParameterEdit.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(SqlReportsDefaults.Permissions.ManageReports)]
    public async Task<IActionResult> ParameterDelete(int id)
    {
        var parameter = await _sqlReportService.GetParameterByIdAsync(id);
        if (parameter == null)
            return RedirectToAction(nameof(Parameters));

        await _sqlReportService.DeleteParameterAsync(parameter);
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Common.DataSuccessfullyDeleted"));

        return RedirectToAction(nameof(Parameters));
    }

    [CheckPermission(SqlReportsDefaults.Permissions.RunReports)]
    public IActionResult InstantQuery()
    {
        return View("~/Plugins/Misc.SqlReports/Admin/Views/InstantQuery.cshtml", new InstantQueryModel());
    }

    [HttpPost]
    [CheckPermission(SqlReportsDefaults.Permissions.RunReports)]
    public async Task<IActionResult> InstantQuery(InstantQueryModel model)
    {
        model.Result = await ExecuteInstantQueryAsync(model);

        return View("~/Plugins/Misc.SqlReports/Admin/Views/InstantQuery.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(SqlReportsDefaults.Permissions.RunReports)]
    public async Task<IActionResult> InstantExport(InstantQueryModel model)
    {
        var result = await ExecuteInstantQueryAsync(model, int.MaxValue);
        if (!string.IsNullOrEmpty(result.Error))
        {
            _notificationService.ErrorNotification(result.Error);
            return View("~/Plugins/Misc.SqlReports/Admin/Views/InstantQuery.cshtml", model);
        }

        var bytes = _executionService.ExportToXlsx(ToExecutionResult(result));

        return File(bytes, MimeTypes.TextXlsx, "instant-sql-report.xlsx");
    }

    protected virtual async Task<SqlReport> GetRunnableReportAsync(int reportId)
    {
        var report = await _sqlReportService.GetReportByIdAsync(reportId);
        var customer = await _workContext.GetCurrentCustomerAsync();

        return await _sqlReportService.CanRunReportAsync(report, customer) ? report : null;
    }

    protected virtual async Task<SqlReportResultModel> ExecuteReportAsync(SqlReport report, SqlReportRunModel model, int maxRows = 200)
    {
        try
        {
            var parameters = await _sqlReportService.GetReportParametersAsync(report.Id);
            var values = model.Parameters.ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);
            var result = await _executionService.ExecuteAsync(report.SqlQuery, parameters, values, maxRows);

            return ToResultModel(result);
        }
        catch (Exception exception)
        {
            return new SqlReportResultModel { Error = exception.Message };
        }
    }

    protected virtual async Task<SqlReportResultModel> ExecuteInstantQueryAsync(InstantQueryModel model, int maxRows = 200)
    {
        try
        {
            var values = ParseInstantParameterValues(model.ParameterValues);
            var result = await _executionService.ExecuteAsync(model.SqlQuery, Enumerable.Empty<SqlReportParameter>(), values, maxRows);

            return ToResultModel(result);
        }
        catch (Exception exception)
        {
            return new SqlReportResultModel { Error = exception.Message };
        }
    }

    protected virtual SqlReportResultModel ToResultModel(SqlReportExecutionResult result)
    {
        return new SqlReportResultModel
        {
            Columns = result.Columns,
            Rows = result.Rows,
            RowsReturned = result.RowsReturned,
            ElapsedMilliseconds = result.ElapsedMilliseconds,
            Truncated = result.Truncated
        };
    }

    protected virtual SqlReportExecutionResult ToExecutionResult(SqlReportResultModel model)
    {
        return new SqlReportExecutionResult
        {
            Columns = model.Columns,
            Rows = model.Rows,
            RowsReturned = model.RowsReturned,
            ElapsedMilliseconds = model.ElapsedMilliseconds,
            Truncated = model.Truncated
        };
    }

    protected virtual IDictionary<string, string> ParseInstantParameterValues(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            result[NormalizeParameterName(line[..separatorIndex])] = line[(separatorIndex + 1)..].Trim();
        }

        return result;
    }

    protected virtual string NormalizeParameterName(string parameterName)
    {
        return (parameterName ?? string.Empty).Trim().TrimStart('@');
    }

    protected virtual void ValidateSqlQuery(string sql)
    {
        try
        {
            _executionService.ValidateSelectOnly(sql);
        }
        catch (Exception exception)
        {
            ModelState.AddModelError(nameof(SqlReportModel.SqlQuery), exception.Message);
        }
    }

    protected virtual string SanitizeFileName(string fileName)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidChar, '-');

        return string.IsNullOrWhiteSpace(fileName) ? "sql-report" : fileName;
    }
}
