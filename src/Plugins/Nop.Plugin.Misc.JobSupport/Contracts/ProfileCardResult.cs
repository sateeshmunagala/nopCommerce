namespace Nop.Plugin.Misc.JobSupport.Contracts;

public partial class ProfileCardResult
{
    public int ProfileId { get; set; }
    public int CustomerId { get; set; }
    public int? LegacyProductId { get; set; }
    public string DisplayName { get; set; }
    public int ProfileType { get; set; }
    public string ShortDescription { get; set; }
    public string CurrentAvailability { get; set; }
    public string MotherTongue { get; set; }
    public string RelevantExperience { get; set; }
    public int? AvatarPictureId { get; set; }
    public int? CountryId { get; set; }
    public int? StateProvinceId { get; set; }
    public string City { get; set; }
    public string PrimaryTechnology { get; set; }
    public string SecondaryTechnology { get; set; }
    public string Slug { get; set; }
    public DateTime? LastLoginDateUtc { get; set; }
    public DateTime? LastActivityDateUtc { get; set; }
    public bool Requested { get; set; }
    public bool Connected { get; set; }
    public int? InterestStatus { get; set; }
    public bool PremiumCustomer { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}
