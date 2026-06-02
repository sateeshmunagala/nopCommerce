using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class JobApplication : BaseEntity
{
    public int CustomerId { get; set; }
    public string JobTitle { get; set; }
    public string Status { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}
