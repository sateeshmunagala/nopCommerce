using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Account;

public record AffiliationCustomerModel : BaseNopEntityModel
{
    public string Name { get; set; }
    public string CreatedOn { get; set; }
}

public record AffiliationModel : BaseNopModel
{
    public string AffiliateUrl { get; set; }
    public IList<AffiliationCustomerModel> Customers { get; set; } = new List<AffiliationCustomerModel>();
}
