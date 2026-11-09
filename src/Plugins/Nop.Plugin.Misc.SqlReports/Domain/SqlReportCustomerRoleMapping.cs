using Nop.Core;

namespace Nop.Plugin.Misc.SqlReports.Domain;

public class SqlReportCustomerRoleMapping : BaseEntity
{
    public int SqlReportId { get; set; }

    public int CustomerRoleId { get; set; }
}
