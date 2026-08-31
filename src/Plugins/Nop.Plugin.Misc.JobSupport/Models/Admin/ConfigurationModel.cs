using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.JobSupport.Models.Admin;

public record ConfigurationModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.Configuration.Fields.Enabled")]
    public bool Enabled { get; set; }
    [NopResourceDisplayName("Plugins.Misc.JobSupport.Admin.Configuration.Fields.UseLegacyStoredProcedures")]
    public bool UseLegacyStoredProcedures { get; set; }
    public string LegacyProfileSearchProcedureName { get; set; }
    public string LegacyShortlistProcedureName { get; set; }
    public string GiveSupportRoleSystemName { get; set; }
    public string TakeSupportRoleSystemName { get; set; }
    public string PaidCustomerRoleSystemName { get; set; }
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
    public int DefaultPageSize { get; set; }
    public bool AllowGuestProfileBrowsing { get; set; }
    public bool ShowGender { get; set; }
    public string HomepageWidgetZone { get; set; }
    public string SidebarWidgetZone { get; set; }
    public int HomepageProfileCount { get; set; }
    public bool EnablePluginEventConsumers { get; set; }
    public bool EnableRegistrationWorkflow { get; set; }
    public bool EnableActivationWorkflow { get; set; }
    public bool EnableOrderPaidWorkflow { get; set; }
    public bool EnableAvailabilityWorkflow { get; set; }
    public bool EnableAvatarSyncWorkflow { get; set; }
    public bool EnableRelationshipNotifications { get; set; }
    public bool EnableSynchronizationTask { get; set; }
    public WorkflowExecutionMode ExecutionMode { get; set; }
    public int SynchronizationBatchSize { get; set; }
    public bool WriteLegacyRewardPointsHistory { get; set; }
}
