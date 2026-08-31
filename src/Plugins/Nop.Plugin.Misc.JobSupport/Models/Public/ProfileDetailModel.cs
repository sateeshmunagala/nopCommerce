namespace Nop.Plugin.Misc.JobSupport.Models.Public;

public record ProfileDetailModel : ProfileCardModel
{
    public string Description { get; set; }
    public string ReviewSummary { get; set; }
    public bool CanRevealContact { get; set; }
    public bool IsOwnProfile { get; set; }
    public bool IsGuest { get; set; }
    public string LoginUrl { get; set; }
    public string RevealContactUrl { get; set; }
    public string BlockUrl { get; set; }
}
