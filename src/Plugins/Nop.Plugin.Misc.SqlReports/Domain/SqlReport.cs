using Nop.Core;

namespace Nop.Plugin.Misc.SqlReports.Domain;

public class SqlReport : BaseEntity
{
    public string Name { get; set; }

    public string SystemName { get; set; }

    public string Description { get; set; }

    public string SqlQuery { get; set; }

    public bool IsActive { get; set; }

    public int DisplayOrder { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}
