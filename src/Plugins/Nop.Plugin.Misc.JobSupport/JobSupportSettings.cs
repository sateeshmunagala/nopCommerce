using Nop.Core.Configuration;
using Nop.Plugin.Misc.JobSupport.Contracts;

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
    public bool EnablePluginEventConsumers { get; set; }
    public bool EnableRegistrationWorkflow { get; set; }
    public bool EnableActivationWorkflow { get; set; }
    public bool EnableOrderPaidWorkflow { get; set; }
    public bool EnableAvailabilityWorkflow { get; set; }
    public bool EnableAvatarSyncWorkflow { get; set; }
    public bool EnableRelationshipNotifications { get; set; }
    public bool EnableSynchronizationTask { get; set; }
    public WorkflowExecutionMode ExecutionMode { get; set; } = WorkflowExecutionMode.Shadow;
    public int SynchronizationBatchSize { get; set; } = 200;
    public bool WriteLegacyRewardPointsHistory { get; set; }
}
