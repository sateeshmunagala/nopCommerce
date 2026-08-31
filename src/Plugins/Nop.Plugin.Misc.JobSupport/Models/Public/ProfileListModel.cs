using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Public;

public record ProfileListModel : BaseNopModel
{
    public ProfileFilterModel Filter { get; set; } = new();
    public IList<ProfileCardModel> Profiles { get; set; } = new List<ProfileCardModel>();
    public int TotalRecords { get; set; }
    public int TotalPages { get; set; }
    public bool QuerySucceeded { get; set; }
}
