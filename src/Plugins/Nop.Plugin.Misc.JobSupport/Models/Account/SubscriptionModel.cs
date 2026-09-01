using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Account;

public record SubscriptionPlanModel : BaseNopEntityModel
{
    public string Name { get; set; }
    public string Url { get; set; }
}

public record SubscriptionModel : BaseNopModel
{
    public string Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int AllottedCredits { get; set; }
    public int UsedCredits { get; set; }
    public int RemainingCredits { get; set; }
    public IList<SubscriptionPlanModel> Plans { get; set; } = new List<SubscriptionPlanModel>();
}
