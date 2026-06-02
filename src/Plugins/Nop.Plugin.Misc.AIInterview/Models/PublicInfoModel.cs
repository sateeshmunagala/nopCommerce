using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AIInterview.Models;

public record PublicInfoModel : BaseNopModel
{
    public string Message { get; set; }
}
