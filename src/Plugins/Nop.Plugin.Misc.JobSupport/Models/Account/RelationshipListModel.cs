using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Models.Public;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Account;

public record RelationshipListModel : BaseNopModel
{
    public RelationshipType RelationshipType { get; set; }
    public IList<ProfileCardModel> Profiles { get; set; } = new List<ProfileCardModel>();
    public bool QuerySucceeded { get; set; }
}
