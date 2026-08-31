using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.JobSupport.Models.Admin;

public enum LegacyParityQueryType
{
    ProfileSearch = 0,
    Relationship = 1
}

public partial record LegacyParityRequestModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.QueryType")]
    public LegacyParityQueryType QueryType { get; set; }

    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.ProductIds")]
    public string ProductIdentifiers { get; set; }

    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.CustomerId")]
    public int CustomerId { get; set; }

    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.ProfileTypeId")]
    public int? ProfileTypeId { get; set; }

    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.RelationshipType")]
    public RelationshipType RelationshipType { get; set; } = RelationshipType.ShortlistedByMe;

    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.PageIndex")]
    public int PageIndex { get; set; }

    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.PageSize")]
    public int PageSize { get; set; } = 12;

    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.LegacyParity.Fields.SortOrder")]
    public int SortOrder { get; set; }

    public LegacyParityResultModel Result { get; set; }
}
