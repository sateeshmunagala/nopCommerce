using Nop.Core.Configuration;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Domain.Enums;

namespace Nop.Plugin.Misc.JobSupport;

public class JobSupportSettings : ISettings
{
    public bool Enabled { get; set; }
    // Compatibility retained for read rollback; remove in JobSupport 2.0.0.
    public bool UseLegacyStoredProcedures { get; set; }
    // Compatibility retained for read rollback; remove in JobSupport 2.0.0.
    public string LegacyProfileSearchProcedureName { get; set; } = string.Empty;
    // Compatibility retained for read rollback; remove in JobSupport 2.0.0.
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
    public int ShortDescriptionCustomerAttributeId { get; set; }
    public int FullDescriptionCustomerAttributeId { get; set; }
    public int ThreeMonthSubscriptionProductId { get; set; }
    public int SixMonthSubscriptionProductId { get; set; }
    public int OneYearSubscriptionProductId { get; set; }
    public int ThreeMonthSubscriptionAllottedCount { get; set; }
    public int SixMonthSubscriptionAllottedCount { get; set; }
    public int OneYearSubscriptionAllottedCount { get; set; }
    public int DefaultPageSize { get; set; } = 12;
    public bool AllowGuestProfileBrowsing { get; set; } = true;
    public bool ShowGender { get; set; }
    public string HomepageWidgetZone { get; set; } = "home_page_before_products";
    public string SidebarWidgetZone { get; set; } = "left_side_column_after";
    public int HomepageProfileCount { get; set; } = 4;
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
    // Compatibility retained for read rollback; remove in JobSupport 2.0.0.
    public DataAccessMode DataReadMode { get; set; } = DataAccessMode.Plugin;
    public DataAccessMode DataWriteMode { get; set; } = DataAccessMode.Plugin;
    // Compatibility retained for read rollback; remove in JobSupport 2.0.0.
    public bool AllowLegacyReadRollback { get; set; } = true;
    public int MigrationBatchSize { get; set; } = 500;
}
