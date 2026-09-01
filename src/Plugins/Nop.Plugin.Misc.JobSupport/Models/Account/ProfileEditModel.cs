using System.ComponentModel.DataAnnotations;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.JobSupport.Models.Account;

public record ProfileOptionModel : BaseNopEntityModel
{
    public string Name { get; set; }
    public bool Selected { get; set; }
}

public record ProfileOptionGroupModel : BaseNopModel
{
    public string Name { get; set; }
    public IList<ProfileOptionModel> Options { get; set; } = new List<ProfileOptionModel>();
}

public record ProfileEditModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Account.Profile.Fields.ProfileType")]
    public int ProfileTypeId { get; set; }
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Account.Profile.Fields.PrimaryTechnology")]
    public IList<int> PrimaryTechnologyIds { get; set; } = new List<int>();
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Account.Profile.Fields.SecondaryTechnology")]
    public IList<int> SecondaryTechnologyIds { get; set; } = new List<int>();
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Account.Profile.Fields.Availability")]
    public int AvailabilityId { get; set; }
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Account.Profile.Fields.Experience")]
    public int RelevantExperienceId { get; set; }
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Account.Profile.Fields.Language")]
    public int MotherTongueId { get; set; }
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Account.Profile.Fields.ShortDescription")]
    public string ShortDescription { get; set; }
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Account.Profile.Fields.Description")]
    public string Description { get; set; }
    public IList<ProfileOptionModel> ProfileTypes { get; set; } = new List<ProfileOptionModel>();
    public IList<ProfileOptionModel> PrimaryTechnologies { get; set; } = new List<ProfileOptionModel>();
    public IList<ProfileOptionModel> SecondaryTechnologies { get; set; } = new List<ProfileOptionModel>();
    public IList<ProfileOptionModel> Availabilities { get; set; } = new List<ProfileOptionModel>();
    public IList<ProfileOptionModel> RelevantExperiences { get; set; } = new List<ProfileOptionModel>();
    public IList<ProfileOptionModel> MotherTongues { get; set; } = new List<ProfileOptionModel>();
}
