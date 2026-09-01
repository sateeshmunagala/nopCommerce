using Nop.Core;

namespace Nop.Plugin.Misc.JobSupport.Domain;

public partial class JobSupportProfileAttributeDefinition : BaseEntity
{
    public int? LegacyCustomerAttributeId { get; set; }
    public string SystemName { get; set; }
    public string Name { get; set; }
    public string HelpText { get; set; }
    public int ControlType { get; set; }
    public bool IsRequired { get; set; }
    public bool ShowOnOnboarding { get; set; }
    public bool ShowOnProfileEdit { get; set; }
    public bool ShowOnPublicProfile { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
