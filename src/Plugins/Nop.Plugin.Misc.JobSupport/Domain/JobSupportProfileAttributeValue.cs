using Nop.Core;

namespace Nop.Plugin.Misc.JobSupport.Domain;

public partial class JobSupportProfileAttributeValue : BaseEntity
{
    public int ProfileId { get; set; }
    public int AttributeDefinitionId { get; set; }
    public int? AttributeOptionId { get; set; }
    public string Value { get; set; }
    public int? LegacyCustomerAttributeId { get; set; }
    public int? LegacyCustomerAttributeValueId { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
