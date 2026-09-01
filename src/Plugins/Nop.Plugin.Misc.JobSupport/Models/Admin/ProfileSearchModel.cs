using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.JobSupport.Models.Admin;

public record ProfileSearchModel : BaseSearchModel
{
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.Profiles.Search.Customer")]
    public string CustomerName { get; set; }
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.Profiles.Search.ProfileType")]
    public int? ProfileTypeId { get; set; }
}
