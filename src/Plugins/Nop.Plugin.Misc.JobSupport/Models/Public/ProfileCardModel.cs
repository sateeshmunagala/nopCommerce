using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Public;

public record ProfileCardModel : BaseNopEntityModel
{
    public string FirstName { get; set; }
    public string AvatarUrl { get; set; }
    public string City { get; set; }
    public string Country { get; set; }
    public string ProfileType { get; set; }
    public string PrimaryTechnology { get; set; }
    public string SecondaryTechnology { get; set; }
    public string Availability { get; set; }
    public string RelevantExperience { get; set; }
    public string MotherTongue { get; set; }
    public string Gender { get; set; }
    public bool ShowGender { get; set; }
    public bool IsPremium { get; set; }
    public bool IsShortlisted { get; set; }
    public bool InterestSent { get; set; }
    public bool CanAct { get; set; }
    public string DetailUrl { get; set; }
    public string ShortlistUrl { get; set; }
    public string RemoveShortlistUrl { get; set; }
    public string InterestUrl { get; set; }
    public string AcceptUrl { get; set; }
    public string DeclineUrl { get; set; }
}
