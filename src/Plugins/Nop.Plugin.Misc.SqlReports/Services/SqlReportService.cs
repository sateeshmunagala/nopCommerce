using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Plugin.Misc.SqlReports.Domain;
using Nop.Services.Customers;

namespace Nop.Plugin.Misc.SqlReports.Services;

public class SqlReportService
{
    private readonly ICustomerService _customerService;
    private readonly IRepository<CustomerRole> _customerRoleRepository;
    private readonly IRepository<SqlReport> _reportRepository;
    private readonly IRepository<SqlReportCustomerRoleMapping> _reportRoleMappingRepository;
    private readonly IRepository<SqlReportParameter> _parameterRepository;
    private readonly IRepository<SqlReportParameterMapping> _parameterMappingRepository;
    private readonly IRepository<SqlReportParameterOption> _parameterOptionRepository;
    private readonly IRepository<SqlReportExecutionLog> _executionLogRepository;

    public SqlReportService(ICustomerService customerService,
        IRepository<CustomerRole> customerRoleRepository,
        IRepository<SqlReport> reportRepository,
        IRepository<SqlReportCustomerRoleMapping> reportRoleMappingRepository,
        IRepository<SqlReportParameter> parameterRepository,
        IRepository<SqlReportParameterMapping> parameterMappingRepository,
        IRepository<SqlReportParameterOption> parameterOptionRepository,
        IRepository<SqlReportExecutionLog> executionLogRepository)
    {
        _customerService = customerService;
        _customerRoleRepository = customerRoleRepository;
        _reportRepository = reportRepository;
        _reportRoleMappingRepository = reportRoleMappingRepository;
        _parameterRepository = parameterRepository;
        _parameterMappingRepository = parameterMappingRepository;
        _parameterOptionRepository = parameterOptionRepository;
        _executionLogRepository = executionLogRepository;
    }

    public virtual async Task<IPagedList<SqlReport>> GetAllReportsAsync(Customer customer = null,
        string name = null, bool activeOnly = false, int pageIndex = 0, int pageSize = int.MaxValue)
    {
        var isAdmin = customer != null && await _customerService.IsAdminAsync(customer);
        var customerRoleIds = customer != null ? await _customerService.GetCustomerRoleIdsAsync(customer) : Array.Empty<int>();

        return await _reportRepository.GetAllPagedAsync(query =>
        {
            if (!string.IsNullOrWhiteSpace(name))
                query = query.Where(report => report.Name.Contains(name));

            if (activeOnly || !isAdmin)
                query = query.Where(report => report.IsActive);

            if (!isAdmin)
            {
                query = from report in query
                    join mapping in _reportRoleMappingRepository.Table on report.Id equals mapping.SqlReportId
                    where customerRoleIds.Contains(mapping.CustomerRoleId)
                    select report;

                query = query.Distinct();
            }

            return query.OrderBy(report => report.DisplayOrder).ThenBy(report => report.Name);
        }, pageIndex, pageSize);
    }

    public virtual async Task<SqlReport> GetReportByIdAsync(int reportId)
    {
        return await _reportRepository.GetByIdAsync(reportId, cache => default);
    }

    public virtual async Task InsertReportAsync(SqlReport report)
    {
        await EnsureUniqueReportSystemNameAsync(report);
        await _reportRepository.InsertAsync(report);
    }

    public virtual async Task UpdateReportAsync(SqlReport report)
    {
        await EnsureUniqueReportSystemNameAsync(report);
        await _reportRepository.UpdateAsync(report);
    }

    public virtual async Task DeleteReportAsync(SqlReport report)
    {
        await _reportRoleMappingRepository.DeleteAsync(mapping => mapping.SqlReportId == report.Id);
        await _parameterMappingRepository.DeleteAsync(mapping => mapping.SqlReportId == report.Id);
        await _reportRepository.DeleteAsync(report);
    }

    public virtual async Task<IList<int>> GetReportCustomerRoleIdsAsync(int reportId)
    {
        return await _reportRoleMappingRepository.Table
            .Where(mapping => mapping.SqlReportId == reportId)
            .Select(mapping => mapping.CustomerRoleId)
            .ToListAsync();
    }

    public virtual async Task<IList<CustomerRole>> GetReportCustomerRolesAsync(int reportId)
    {
        var roleIds = await GetReportCustomerRoleIdsAsync(reportId);

        return await _customerRoleRepository.Table
            .Where(role => roleIds.Contains(role.Id))
            .OrderBy(role => role.Name)
            .ToListAsync();
    }

    public virtual async Task SaveReportCustomerRoleMappingsAsync(int reportId, IList<int> customerRoleIds)
    {
        customerRoleIds ??= new List<int>();
        await _reportRoleMappingRepository.DeleteAsync(mapping => mapping.SqlReportId == reportId);

        foreach (var customerRoleId in customerRoleIds.Distinct())
        {
            await _reportRoleMappingRepository.InsertAsync(new SqlReportCustomerRoleMapping
            {
                SqlReportId = reportId,
                CustomerRoleId = customerRoleId
            });
        }
    }

    public virtual async Task<IList<int>> GetReportParameterIdsAsync(int reportId)
    {
        return await _parameterMappingRepository.Table
            .Where(mapping => mapping.SqlReportId == reportId)
            .Select(mapping => mapping.SqlReportParameterId)
            .ToListAsync();
    }

    public virtual async Task<IList<SqlReportParameter>> GetReportParametersAsync(int reportId)
    {
        var parameterIds = await GetReportParameterIdsAsync(reportId);

        return await _parameterRepository.Table
            .Where(parameter => parameterIds.Contains(parameter.Id))
            .OrderBy(parameter => parameter.DisplayOrder)
            .ThenBy(parameter => parameter.Name)
            .ToListAsync();
    }

    public virtual async Task SaveReportParameterMappingsAsync(int reportId, IList<int> parameterIds)
    {
        parameterIds ??= new List<int>();
        await _parameterMappingRepository.DeleteAsync(mapping => mapping.SqlReportId == reportId);

        foreach (var parameterId in parameterIds.Distinct())
        {
            await _parameterMappingRepository.InsertAsync(new SqlReportParameterMapping
            {
                SqlReportId = reportId,
                SqlReportParameterId = parameterId
            });
        }
    }

    public virtual async Task<bool> CanRunReportAsync(SqlReport report, Customer customer)
    {
        if (report == null || customer == null)
            return false;

        var isAdmin = await _customerService.IsAdminAsync(customer);
        if (isAdmin)
            return SqlReportAccessRules.CanRunReport(true, report.IsActive, null, null);

        var allowedRoleIds = await GetReportCustomerRoleIdsAsync(report.Id);
        var customerRoleIds = await _customerService.GetCustomerRoleIdsAsync(customer);

        return SqlReportAccessRules.CanRunReport(isAdmin, report.IsActive, allowedRoleIds, customerRoleIds);
    }

    public virtual async Task<IPagedList<SqlReportParameter>> GetAllParametersAsync(string name = null,
        int pageIndex = 0, int pageSize = int.MaxValue)
    {
        return await _parameterRepository.GetAllPagedAsync(query =>
        {
            if (!string.IsNullOrWhiteSpace(name))
                query = query.Where(parameter => parameter.Name.Contains(name) || parameter.ParameterName.Contains(name));

            return query.OrderBy(parameter => parameter.DisplayOrder).ThenBy(parameter => parameter.Name);
        }, pageIndex, pageSize);
    }

    public virtual async Task<IList<SqlReportParameter>> GetAllParametersAsync()
    {
        return await _parameterRepository.Table
            .OrderBy(parameter => parameter.DisplayOrder)
            .ThenBy(parameter => parameter.Name)
            .ToListAsync();
    }

    public virtual async Task<SqlReportParameter> GetParameterByIdAsync(int parameterId)
    {
        return await _parameterRepository.GetByIdAsync(parameterId, cache => default);
    }

    public virtual async Task InsertParameterAsync(SqlReportParameter parameter)
    {
        await EnsureUniqueParameterNameAsync(parameter);
        await _parameterRepository.InsertAsync(parameter);
    }

    public virtual async Task UpdateParameterAsync(SqlReportParameter parameter)
    {
        await EnsureUniqueParameterNameAsync(parameter);
        await _parameterRepository.UpdateAsync(parameter);
    }

    public virtual async Task DeleteParameterAsync(SqlReportParameter parameter)
    {
        await _parameterOptionRepository.DeleteAsync(option => option.SqlReportParameterId == parameter.Id);
        await _parameterMappingRepository.DeleteAsync(mapping => mapping.SqlReportParameterId == parameter.Id);
        await _parameterRepository.DeleteAsync(parameter);
    }

    public virtual async Task<IList<SqlReportParameterOption>> GetParameterOptionsAsync(int parameterId)
    {
        return await _parameterOptionRepository.Table
            .Where(option => option.SqlReportParameterId == parameterId)
            .OrderBy(option => option.DisplayOrder)
            .ThenBy(option => option.Text)
            .ToListAsync();
    }

    public virtual async Task<IDictionary<int, IList<SqlReportParameterOption>>> GetParameterOptionsByParameterIdsAsync(IList<int> parameterIds)
    {
        var options = await _parameterOptionRepository.Table
            .Where(option => parameterIds.Contains(option.SqlReportParameterId))
            .OrderBy(option => option.DisplayOrder)
            .ThenBy(option => option.Text)
            .ToListAsync();

        return options
            .GroupBy(option => option.SqlReportParameterId)
            .ToDictionary(group => group.Key, group => (IList<SqlReportParameterOption>)group.ToList());
    }

    public virtual async Task SaveParameterOptionsAsync(int parameterId, IList<SqlReportParameterOption> options)
    {
        await _parameterOptionRepository.DeleteAsync(option => option.SqlReportParameterId == parameterId);

        foreach (var option in options ?? new List<SqlReportParameterOption>())
        {
            option.SqlReportParameterId = parameterId;
            await _parameterOptionRepository.InsertAsync(option);
        }
    }

    public virtual async Task InsertExecutionLogAsync(SqlReportExecutionLog executionLog)
    {
        await _executionLogRepository.InsertAsync(executionLog);
    }

    protected virtual async Task EnsureUniqueReportSystemNameAsync(SqlReport report)
    {
        if (string.IsNullOrWhiteSpace(report.SystemName))
            return;

        var exists = await _reportRepository.Table.AnyAsync(item =>
            item.Id != report.Id && item.SystemName == report.SystemName);

        if (exists)
            throw new InvalidOperationException($"A SQL report with system name '{report.SystemName}' already exists.");
    }

    protected virtual async Task EnsureUniqueParameterNameAsync(SqlReportParameter parameter)
    {
        var parameterName = NormalizeParameterName(parameter.ParameterName);
        parameter.ParameterName = parameterName;

        var exists = await _parameterRepository.Table.AnyAsync(item =>
            item.Id != parameter.Id && item.ParameterName == parameterName);

        if (exists)
            throw new InvalidOperationException($"A SQL report parameter named '@{parameterName}' already exists.");
    }

    protected virtual string NormalizeParameterName(string parameterName)
    {
        return (parameterName ?? string.Empty).Trim().TrimStart('@');
    }
}
