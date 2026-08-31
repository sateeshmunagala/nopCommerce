using LinqToDB.Mapping;

namespace Nop.Plugin.Misc.JobSupport.Contracts;

public partial class ProfileSearchResult
{
    public int Id { get; set; }
    public int VendorId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Gender { get; set; }
    public string Company { get; set; }
    public int? CountryId { get; set; }
    public int? StateProvinceId { get; set; }
    public string City { get; set; }
    public int? LanguageId { get; set; }
    public string TimeZoneId { get; set; }
    public string AvatarPictureId { get; set; }
    public int? CustomerProfileTypeId { get; set; }
    public string PrimaryTechnology { get; set; }
    public string SecondaryTechnology { get; set; }
    [Column("CurrentAvalibility")]
    public string CurrentAvailability { get; set; }
    public string ProfileType { get; set; }
    public string MotherTongue { get; set; }
    public string WorkExperience { get; set; }
    public string Country { get; set; }
    public string StateProvince { get; set; }
    public string Language { get; set; }
    public string Slug { get; set; }
    public DateTime? LastLoginDateUtc { get; set; }
    public DateTime? LastActivityDateUtc { get; set; }
    public bool ProfileShortListed { get; set; }
    public bool InterestSent { get; set; }
    public bool PremiumCustomer { get; set; }
}
