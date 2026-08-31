using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.JobSupport;

public class JobSupportSettings : ISettings
{
    public bool Enabled { get; set; }
    public bool UseLegacyStoredProcedures { get; set; }
    public string LegacyProfileSearchProcedureName { get; set; } = string.Empty;
    public string LegacyShortlistProcedureName { get; set; } = string.Empty;
    public string GiveSupportRoleSystemName { get; set; } = string.Empty;
    public string TakeSupportRoleSystemName { get; set; } = string.Empty;
    public string PaidCustomerRoleSystemName { get; set; } = string.Empty;
    public int ProfileTypeSpecificationAttributeId { get; set; }
    public int CurrentAvailabilitySpecificationAttributeId { get; set; }
    public int RelevantExperienceSpecificationAttributeId { get; set; }
    public int MotherTongueSpecificationAttributeId { get; set; }
    public int PrimaryTechnologySpecificationAttributeId { get; set; }
    public int SecondaryTechnologySpecificationAttributeId { get; set; }
    public int ThreeMonthSubscriptionProductId { get; set; }
    public int SixMonthSubscriptionProductId { get; set; }
    public int OneYearSubscriptionProductId { get; set; }
    public int DefaultPageSize { get; set; } = 12;
}
