using Nop.Core;

namespace Nop.Plugin.Misc.JobSupport.Domain;

public partial class JobSupportProfileAttributeOption : BaseEntity
{
    public int AttributeDefinitionId { get; set; }
    public int? LegacyCustomerAttributeValueId { get; set; }
    public string Name { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public int? LegacyOptionId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
