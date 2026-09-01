using Nop.Core;

namespace Nop.Plugin.Misc.JobSupport.Domain;

public partial class JobSupportProfileSkill : BaseEntity
{
    public int ProfileId { get; set; }
    public int SkillType { get; set; }
    public string Name { get; set; }
    public int? LegacySpecificationAttributeId { get; set; }
    public int? LegacySpecificationAttributeOptionId { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
