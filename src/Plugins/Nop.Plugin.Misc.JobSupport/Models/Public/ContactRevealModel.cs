using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Public;

public record ContactRevealModel : BaseNopModel
{
    public bool Succeeded { get; set; }
    public bool AlreadyRevealed { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public int RemainingCredits { get; set; }
    public string MessageKey { get; set; }
}
