using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Models.Admin;
using Nop.Services.Catalog;
using Nop.Services.Helpers;
using Nop.Plugin.Misc.JobSupport.Services;

namespace Nop.Plugin.Misc.JobSupport.Factories;

public class JobSupportAdminModelFactory : IJobSupportAdminModelFactory
{
    private readonly IJobSupportProfileQueryService _profileQueryService;
    private readonly IProductService _productService;
    private readonly IWebHelper _webHelper;
    private readonly JobSupportSettings _settings;

    public JobSupportAdminModelFactory(IJobSupportProfileQueryService profileQueryService,
        IProductService productService,
        IWebHelper webHelper,
        JobSupportSettings settings)
    {
        _profileQueryService = profileQueryService;
        _productService = productService;
        _webHelper = webHelper;
        _settings = settings;
    }

    public ProfileSearchModel PrepareProfileSearchModel()
    {
        var model = new ProfileSearchModel();
        model.SetGridPageSize();
        return model;
    }

    public async Task<ProfileListModel> PrepareProfileListModelAsync(ProfileSearchModel searchModel)
    {
        var result = await _profileQueryService.SearchProfilesAsync(new ProfileSearchRequest
        {
            ProfileTypeId = searchModel.ProfileTypeId,
            PageIndex = searchModel.Page - 1,
            PageSize = searchModel.PageSize,
            SortOrder = 0
        });
        var rows = new List<ProfileAdminModel>();
        foreach (var item in result.Items.Where(item => string.IsNullOrWhiteSpace(searchModel.CustomerName) ||
                     $"{item.FirstName} {item.LastName}".Contains(searchModel.CustomerName, StringComparison.OrdinalIgnoreCase)))
        {
            var product = await _productService.GetProductByIdAsync(item.Id);
            rows.Add(new ProfileAdminModel
            {
                Id = item.Id,
                CustomerId = item.VendorId,
                CustomerName = $"{item.FirstName} {item.LastName}".Trim(),
                ProductId = item.Id,
                ProductName = product?.Name,
                ProfileType = item.ProfileType,
                PrimaryTechnology = item.PrimaryTechnology,
                Availability = item.CurrentAvailability,
                Premium = item.PremiumCustomer,
                CreatedOn = product?.CreatedOnUtc ?? DateTime.MinValue,
                Published = product?.Published ?? false,
                CustomerEditUrl = $"{_webHelper.GetStoreLocation()}Admin/Customer/Edit/{item.VendorId}",
                ProductEditUrl = $"{_webHelper.GetStoreLocation()}Admin/Product/Edit/{item.Id}"
            });
        }
        return new ProfileListModel
        {
            Data = rows,
            Draw = searchModel.Draw,
            RecordsFiltered = result.TotalRecords,
            RecordsTotal = result.TotalRecords
        };
    }

    public ConfigurationModel PrepareConfigurationModel() => new()
    {
        Enabled = _settings.Enabled,
        UseLegacyStoredProcedures = _settings.UseLegacyStoredProcedures,
        LegacyProfileSearchProcedureName = _settings.LegacyProfileSearchProcedureName,
        LegacyShortlistProcedureName = _settings.LegacyShortlistProcedureName,
        GiveSupportRoleSystemName = _settings.GiveSupportRoleSystemName,
        TakeSupportRoleSystemName = _settings.TakeSupportRoleSystemName,
        PaidCustomerRoleSystemName = _settings.PaidCustomerRoleSystemName,
        ProfileTypeSpecificationAttributeId = _settings.ProfileTypeSpecificationAttributeId,
        CurrentAvailabilitySpecificationAttributeId = _settings.CurrentAvailabilitySpecificationAttributeId,
        RelevantExperienceSpecificationAttributeId = _settings.RelevantExperienceSpecificationAttributeId,
        MotherTongueSpecificationAttributeId = _settings.MotherTongueSpecificationAttributeId,
        PrimaryTechnologySpecificationAttributeId = _settings.PrimaryTechnologySpecificationAttributeId,
        SecondaryTechnologySpecificationAttributeId = _settings.SecondaryTechnologySpecificationAttributeId,
        ShortDescriptionCustomerAttributeId = _settings.ShortDescriptionCustomerAttributeId,
        FullDescriptionCustomerAttributeId = _settings.FullDescriptionCustomerAttributeId,
        ThreeMonthSubscriptionProductId = _settings.ThreeMonthSubscriptionProductId,
        SixMonthSubscriptionProductId = _settings.SixMonthSubscriptionProductId,
        OneYearSubscriptionProductId = _settings.OneYearSubscriptionProductId,
        ThreeMonthSubscriptionAllottedCount = _settings.ThreeMonthSubscriptionAllottedCount,
        SixMonthSubscriptionAllottedCount = _settings.SixMonthSubscriptionAllottedCount,
        OneYearSubscriptionAllottedCount = _settings.OneYearSubscriptionAllottedCount,
        DefaultPageSize = _settings.DefaultPageSize,
        AllowGuestProfileBrowsing = _settings.AllowGuestProfileBrowsing,
        ShowGender = _settings.ShowGender,
        HomepageWidgetZone = _settings.HomepageWidgetZone,
        SidebarWidgetZone = _settings.SidebarWidgetZone,
        HomepageProfileCount = _settings.HomepageProfileCount,
        EnablePluginEventConsumers = _settings.EnablePluginEventConsumers,
        EnableRegistrationWorkflow = _settings.EnableRegistrationWorkflow,
        EnableActivationWorkflow = _settings.EnableActivationWorkflow,
        EnableOrderPaidWorkflow = _settings.EnableOrderPaidWorkflow,
        EnableAvailabilityWorkflow = _settings.EnableAvailabilityWorkflow,
        EnableAvatarSyncWorkflow = _settings.EnableAvatarSyncWorkflow,
        EnableRelationshipNotifications = _settings.EnableRelationshipNotifications,
        EnableSynchronizationTask = _settings.EnableSynchronizationTask,
        ExecutionMode = _settings.ExecutionMode,
        SynchronizationBatchSize = _settings.SynchronizationBatchSize
    };

    public void ApplyConfigurationModel(ConfigurationModel model)
    {
        _settings.Enabled = model.Enabled;
        _settings.UseLegacyStoredProcedures = model.UseLegacyStoredProcedures;
        _settings.LegacyProfileSearchProcedureName = model.LegacyProfileSearchProcedureName;
        _settings.LegacyShortlistProcedureName = model.LegacyShortlistProcedureName;
        _settings.GiveSupportRoleSystemName = model.GiveSupportRoleSystemName;
        _settings.TakeSupportRoleSystemName = model.TakeSupportRoleSystemName;
        _settings.PaidCustomerRoleSystemName = model.PaidCustomerRoleSystemName;
        _settings.ProfileTypeSpecificationAttributeId = model.ProfileTypeSpecificationAttributeId;
        _settings.CurrentAvailabilitySpecificationAttributeId = model.CurrentAvailabilitySpecificationAttributeId;
        _settings.RelevantExperienceSpecificationAttributeId = model.RelevantExperienceSpecificationAttributeId;
        _settings.MotherTongueSpecificationAttributeId = model.MotherTongueSpecificationAttributeId;
        _settings.PrimaryTechnologySpecificationAttributeId = model.PrimaryTechnologySpecificationAttributeId;
        _settings.SecondaryTechnologySpecificationAttributeId = model.SecondaryTechnologySpecificationAttributeId;
        _settings.ShortDescriptionCustomerAttributeId = model.ShortDescriptionCustomerAttributeId;
        _settings.FullDescriptionCustomerAttributeId = model.FullDescriptionCustomerAttributeId;
        _settings.ThreeMonthSubscriptionProductId = model.ThreeMonthSubscriptionProductId;
        _settings.SixMonthSubscriptionProductId = model.SixMonthSubscriptionProductId;
        _settings.OneYearSubscriptionProductId = model.OneYearSubscriptionProductId;
        _settings.ThreeMonthSubscriptionAllottedCount = model.ThreeMonthSubscriptionAllottedCount;
        _settings.SixMonthSubscriptionAllottedCount = model.SixMonthSubscriptionAllottedCount;
        _settings.OneYearSubscriptionAllottedCount = model.OneYearSubscriptionAllottedCount;
        _settings.DefaultPageSize = model.DefaultPageSize;
        _settings.AllowGuestProfileBrowsing = model.AllowGuestProfileBrowsing;
        _settings.ShowGender = model.ShowGender;
        _settings.HomepageWidgetZone = model.HomepageWidgetZone;
        _settings.SidebarWidgetZone = model.SidebarWidgetZone;
        _settings.HomepageProfileCount = model.HomepageProfileCount;
        _settings.EnablePluginEventConsumers = model.EnablePluginEventConsumers;
        _settings.EnableRegistrationWorkflow = model.EnableRegistrationWorkflow;
        _settings.EnableActivationWorkflow = model.EnableActivationWorkflow;
        _settings.EnableOrderPaidWorkflow = model.EnableOrderPaidWorkflow;
        _settings.EnableAvailabilityWorkflow = model.EnableAvailabilityWorkflow;
        _settings.EnableAvatarSyncWorkflow = model.EnableAvatarSyncWorkflow;
        _settings.EnableRelationshipNotifications = model.EnableRelationshipNotifications;
        _settings.EnableSynchronizationTask = model.EnableSynchronizationTask;
        _settings.ExecutionMode = model.ExecutionMode;
        _settings.SynchronizationBatchSize = model.SynchronizationBatchSize;
    }
}
