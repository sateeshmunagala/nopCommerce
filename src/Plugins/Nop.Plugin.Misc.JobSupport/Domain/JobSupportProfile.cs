using Nop.Core;

namespace Nop.Plugin.Misc.JobSupport.Domain;

public partial class JobSupportProfile : BaseEntity
{
    public int CustomerId { get; set; }
    public int? LegacyProductId { get; set; }
    public int ProfileType { get; set; }
    public string DisplayName { get; set; }
    public string Slug { get; set; }
    public string ShortDescription { get; set; }
    public string FullDescription { get; set; }
    public string CurrentAvailability { get; set; }
    public string AvailabilityDays { get; set; }
    public string AvailabilityTimings { get; set; }
    public string HoursPerWeek { get; set; }
    public string MotherTongue { get; set; }
    public string RelevantExperience { get; set; }
    public int? AvatarPictureId { get; set; }
    public int? CountryId { get; set; }
    public int? StateProvinceId { get; set; }
    public string City { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
    public string MigrationSource { get; set; }
    public int? LegacySourceId { get; set; }
}
