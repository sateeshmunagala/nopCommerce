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

    public SqlReportService(ICustomerService customerService,
        IRepository<CustomerRole> customerRoleRepository,
        IRepository<SqlReport> reportRepository,
        IRepository<SqlReportCustomerRoleMapping> reportRoleMappingRepository,
        IRepository<SqlReportParameter> parameterRepository,
        IRepository<SqlReportParameterMapping> parameterMappingRepository)
    {
        _customerService = customerService;
        _customerRoleRepository = customerRoleRepository;
        _reportRepository = reportRepository;
        _reportRoleMappingRepository = reportRoleMappingRepository;
        _parameterRepository = parameterRepository;
        _parameterMappingRepository = parameterMappingRepository;
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
        await _reportRepository.InsertAsync(report);
    }

    public virtual async Task UpdateReportAsync(SqlReport report)
    {
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

        if (await _customerService.IsAdminAsync(customer))
            return true;

        if (!report.IsActive)
            return false;

        var allowedRoleIds = await GetReportCustomerRoleIdsAsync(report.Id);
        if (!allowedRoleIds.Any())
            return false;

        var customerRoleIds = await _customerService.GetCustomerRoleIdsAsync(customer);

        return allowedRoleIds.Intersect(customerRoleIds).Any();
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
        await _parameterRepository.InsertAsync(parameter);
    }

    public virtual async Task UpdateParameterAsync(SqlReportParameter parameter)
    {
        await _parameterRepository.UpdateAsync(parameter);
    }

    public virtual async Task DeleteParameterAsync(SqlReportParameter parameter)
    {
        await _parameterMappingRepository.DeleteAsync(mapping => mapping.SqlReportParameterId == parameter.Id);
        await _parameterRepository.DeleteAsync(parameter);
    }
}
