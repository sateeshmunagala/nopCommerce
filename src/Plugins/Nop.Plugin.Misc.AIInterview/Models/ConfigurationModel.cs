using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AIInterview.Models;

public record ConfigurationModel : BaseNopModel
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; }
    public bool UseMockResponses { get; set; }
}
