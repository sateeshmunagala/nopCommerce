using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AIInterview.Models;

public record ConfigurationModel : BaseNopModel
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; }
    public decimal MinimumScore { get; set; }
    public bool UseMockResponses { get; set; }
    public string Provider { get; set; }
    public string AiModel { get; set; }
    public string Prompt { get; set; }
    public string ServiceSettings { get; set; }
    public decimal CreditPackAmount { get; set; }
    public decimal CreditPackPrice { get; set; }
}
