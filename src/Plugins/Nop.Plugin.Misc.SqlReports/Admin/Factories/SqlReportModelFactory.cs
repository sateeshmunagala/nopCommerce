using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Plugin.Misc.SqlReports.Admin.Models;
using Nop.Plugin.Misc.SqlReports.Domain;
using Nop.Plugin.Misc.SqlReports.Services;
using Nop.Services.Customers;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Factories;
using Nop.Web.Framework.Models.Extensions;
using Nop.Web.Areas.Admin.Infrastructure.Mapper.Extensions;

namespace Nop.Plugin.Misc.SqlReports.Admin.Factories;

public class SqlReportModelFactory
{
    private readonly IBaseAdminModelFactory _baseAdminModelFactory;
    private readonly ICustomerService _customerService;
    private readonly IPermissionService _permissionService;
    private readonly IWorkContext _workContext;
    private readonly SqlReportService _sqlReportService;
    private readonly SqlReportsSettings _settings;

    public SqlReportModelFactory(IBaseAdminModelFactory baseAdminModelFactory,
        ICustomerService customerService,
        IPermissionService permissionService,
        IWorkContext workContext,
        SqlReportService sqlReportService,
        SqlReportsSettings settings)
    {
        _baseAdminModelFactory = baseAdminModelFactory;
        _customerService = customerService;
        _permissionService = permissionService;
        _workContext = workContext;
        _sqlReportService = sqlReportService;
        _settings = settings;
    }

    public virtual ConfigurationModel PrepareConfigurationModel()
    {
        return new ConfigurationModel
        {
            MaxRowsPerQuery = _settings.MaxRowsPerQuery,
            CommandTimeoutSeconds = _settings.CommandTimeoutSeconds,
            MaxCellLength = _settings.MaxCellLength,
            EnableInstantQuery = _settings.EnableInstantQuery,
            AllowExport = _settings.AllowExport
        };
    }

    public virtual async Task<SqlReportSearchModel> PrepareReportSearchModelAsync(SqlReportSearchModel searchModel)
    {
        ArgumentNullException.ThrowIfNull(searchModel);

        searchModel.CanManageReports = await _permissionService.AuthorizeAsync(SqlReportsDefaults.Permissions.ManageReports);
        searchModel.SetGridPageSize();

        return searchModel;
    }

    public virtual async Task<SqlReportListModel> PrepareReportListModelAsync(SqlReportSearchModel searchModel)
    {
        ArgumentNullException.ThrowIfNull(searchModel);

        var customer = await _workContext.GetCurrentCustomerAsync();
        var reports = await _sqlReportService.GetAllReportsAsync(customer, searchModel.SearchName,
            pageIndex: searchModel.Page - 1, pageSize: searchModel.PageSize);

        return await new SqlReportListModel().PrepareToGridAsync(searchModel, reports, () =>
        {
            return reports.SelectAwait(async report =>
            {
                var roles = await _sqlReportService.GetReportCustomerRolesAsync(report.Id);
                var parameters = await _sqlReportService.GetReportParametersAsync(report.Id);

                return new SqlReportModel
                {
                    Id = report.Id,
                    Name = report.Name,
                    SystemName = report.SystemName,
                    Description = report.Description,
                    IsActive = report.IsActive,
                    DisplayOrder = report.DisplayOrder,
                    CustomerRoleNames = roles.Any() ? string.Join(", ", roles.Select(role => role.Name)) : "Administrators only",
                    ParameterNames = parameters.Any() ? string.Join(", ", parameters.Select(parameter => parameter.ParameterName)) : string.Empty
                };
            });
        });
    }

    public virtual async Task<SqlReportModel> PrepareReportModelAsync(SqlReportModel model, SqlReport report, bool excludeProperties = false)
    {
        if (report != null && model == null)
        {
            model = new SqlReportModel
            {
                Id = report.Id,
                Name = report.Name,
                SystemName = report.SystemName,
                Description = report.Description,
                SqlQuery = report.SqlQuery,
                IsActive = report.IsActive,
                DisplayOrder = report.DisplayOrder
            };
        }

        model ??= new SqlReportModel { IsActive = true };

        await PrepareAvailableCustomerRolesAsync(model.AvailableCustomerRoles);
        await PrepareAvailableParametersAsync(model.AvailableParameters);

        if (!excludeProperties && report != null)
        {
            model.SelectedCustomerRoleIds = (await _sqlReportService.GetReportCustomerRoleIdsAsync(report.Id)).ToList();
            model.SelectedParameterIds = (await _sqlReportService.GetReportParameterIdsAsync(report.Id)).ToList();
        }

        foreach (var item in model.AvailableCustomerRoles)
            item.Selected = int.TryParse(item.Value, out var roleId) && model.SelectedCustomerRoleIds.Contains(roleId);

        foreach (var item in model.AvailableParameters)
            item.Selected = int.TryParse(item.Value, out var parameterId) && model.SelectedParameterIds.Contains(parameterId);

        return model;
    }

    public virtual async Task<SqlReportRunModel> PrepareRunModelAsync(SqlReport report, SqlReportRunModel model = null)
    {
        var parameters = await _sqlReportService.GetReportParametersAsync(report.Id);
        var optionsByParameterId = await _sqlReportService.GetParameterOptionsByParameterIdsAsync(parameters.Select(parameter => parameter.Id).ToList());

        model ??= new SqlReportRunModel();
        model.Id = report.Id;
        model.Name = report.Name;
        model.Description = report.Description;
        model.SqlQuery = report.SqlQuery;
        model.AllowExport = _settings.AllowExport;

        if (!model.Parameters.Any())
        {
            model.Parameters = parameters.Select(parameter =>
            {
                var runParameter = new SqlReportRunParameterModel
                {
                    ParameterId = parameter.Id,
                    Name = parameter.Name,
                    ParameterName = parameter.ParameterName.TrimStart('@'),
                    DataType = parameter.DataType,
                    Prompt = parameter.Prompt,
                    IsRequired = parameter.IsRequired,
                    Value = parameter.DefaultValue
                };

                if (optionsByParameterId.TryGetValue(parameter.Id, out var options))
                {
                    foreach (var option in options)
                    {
                        runParameter.AvailableOptions.Add(new SelectListItem(option.Text, option.Value));
                    }
                }

                return runParameter;
            }).ToList();
        }
        else
        {
            foreach (var parameterModel in model.Parameters)
            {
                if (!optionsByParameterId.TryGetValue(parameterModel.ParameterId, out var options))
                    continue;

                parameterModel.AvailableOptions.Clear();
                foreach (var option in options)
                    parameterModel.AvailableOptions.Add(new SelectListItem(option.Text, option.Value, parameterModel.SelectedValues.Contains(option.Value)));
            }
        }

        return model;
    }

    public virtual async Task<SqlReportParameterSearchModel> PrepareParameterSearchModelAsync(SqlReportParameterSearchModel searchModel)
    {
        ArgumentNullException.ThrowIfNull(searchModel);

        searchModel.SetGridPageSize();

        return await Task.FromResult(searchModel);
    }

    public virtual async Task<SqlReportParameterListModel> PrepareParameterListModelAsync(SqlReportParameterSearchModel searchModel)
    {
        ArgumentNullException.ThrowIfNull(searchModel);

        var parameters = await _sqlReportService.GetAllParametersAsync(searchModel.SearchName,
            pageIndex: searchModel.Page - 1, pageSize: searchModel.PageSize);

        return await new SqlReportParameterListModel().PrepareToGridAsync(searchModel, parameters, () =>
        {
            return parameters.Select(parameter => new SqlReportParameterModel
            {
                Id = parameter.Id,
                Name = parameter.Name,
                ParameterName = parameter.ParameterName,
                DataType = parameter.DataType,
                DefaultValue = parameter.DefaultValue,
                Prompt = parameter.Prompt,
                IsRequired = parameter.IsRequired,
                DisplayOrder = parameter.DisplayOrder
            }).ToAsyncEnumerable();
        });
    }

    public virtual async Task<SqlReportParameterModel> PrepareParameterModelAsync(SqlReportParameterModel model, SqlReportParameter parameter)
    {
        if (parameter != null && model == null)
        {
            model = new SqlReportParameterModel
            {
                Id = parameter.Id,
                Name = parameter.Name,
                ParameterName = parameter.ParameterName,
                DataType = parameter.DataType,
                DefaultValue = parameter.DefaultValue,
                Prompt = parameter.Prompt,
                IsRequired = parameter.IsRequired,
                DisplayOrder = parameter.DisplayOrder,
                OptionsText = FormatOptions(await _sqlReportService.GetParameterOptionsAsync(parameter.Id))
            };
        }

        model ??= new SqlReportParameterModel
        {
            DataType = SqlReportDataType.Text
        };

        PrepareDataTypes(model);

        return await Task.FromResult(model);
    }

    protected virtual void PrepareDataTypes(SqlReportParameterModel model)
    {
        foreach (var dataType in SqlReportDataType.All)
            model.AvailableDataTypes.Add(new SelectListItem(dataType, dataType, string.Equals(dataType, model.DataType, StringComparison.OrdinalIgnoreCase)));
    }

    protected virtual async Task PrepareAvailableCustomerRolesAsync(IList<SelectListItem> items)
    {
        await _baseAdminModelFactory.PrepareCustomerRolesAsync(items, false);
    }

    protected virtual async Task PrepareAvailableParametersAsync(IList<SelectListItem> items)
    {
        foreach (var parameter in await _sqlReportService.GetAllParametersAsync())
            items.Add(new SelectListItem($"{parameter.Name} ({parameter.ParameterName})", parameter.Id.ToString()));
    }

    protected virtual string FormatOptions(IList<SqlReportParameterOption> options)
    {
        return string.Join(Environment.NewLine, options.Select(option => $"{option.Value}|{option.Text}|{option.DisplayOrder}"));
    }
}
