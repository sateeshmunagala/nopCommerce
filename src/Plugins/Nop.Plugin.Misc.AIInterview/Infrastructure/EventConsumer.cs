using Nop.Core.Http;
using Nop.Services.Events;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public class EventConsumer : IConsumer<ModelPreparedEvent<BaseNopModel>>
{
    private readonly ILocalizationService _localizationService;
    private readonly Services.ICreditService _creditService;
    private readonly Nop.Core.IWorkContext _workContext;
    private readonly ICustomerService _customerService;
    private readonly AIInterviewSettings _aiInterviewSettings;

    public EventConsumer(ILocalizationService localizationService,
        Services.ICreditService creditService,
        Nop.Core.IWorkContext workContext,
        ICustomerService customerService,
        AIInterviewSettings aiInterviewSettings)
    {
        _localizationService = localizationService;
        _creditService = creditService;
        _workContext = workContext;
        _customerService = customerService;
        _aiInterviewSettings = aiInterviewSettings;
    }

    public async Task HandleEventAsync(ModelPreparedEvent<BaseNopModel> eventMessage)
    {
        // Add "My Applications" link to customer account navigation
        if (eventMessage.Model is Nop.Web.Models.Customer.CustomerNavigationModel navigationModel)
        {
            if (_aiInterviewSettings.Enabled)
            {
                var legacyActivityItems = navigationModel.CustomerNavigationItems
                    .Where(item =>
                        string.Equals(item.RouteName, AIInterviewDefaults.MyApplicationsRouteName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(item.RouteName, AIInterviewDefaults.MockHistoryRouteName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var legacyActivityItem in legacyActivityItems)
                    navigationModel.CustomerNavigationItems.Remove(legacyActivityItem);

                var customer = await _workContext.GetCurrentCustomerAsync();
                var hasVendorRole = customer != null
                    && await _customerService.IsVendorAsync(customer, true);
                var hasEmployerRole = customer != null
                    && await _customerService.IsInCustomerRoleAsync(customer, AIInterviewDefaults.EmployerCustomerRoleSystemName, true);
                var hasInstituteRole = customer != null
                    && await _customerService.IsInCustomerRoleAsync(customer, AIInterviewDefaults.InstituteCustomerRoleSystemName, true);

                // My Activity is for applicants only - users with no portal role
                if (customer != null && !hasVendorRole && !hasInstituteRole && !hasEmployerRole)
                {
                    if (!navigationModel.CustomerNavigationItems.Any(item =>
                        string.Equals(item.RouteName, AIInterviewDefaults.MyActivityRouteName,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        navigationModel.CustomerNavigationItems.Add(
                            new Nop.Web.Models.Customer.CustomerNavigationItemModel
                            {
                                RouteName = AIInterviewDefaults.MyActivityRouteName,
                                Title = await _localizationService.GetResourceAsync(
                                    "Plugins.Misc.AIInterview.MyActivity.Title"),
                                Tab = AIInterviewDefaults.MyActivityNavigationTab,
                                ItemClass = "customer-my-activity"
                            });
                    }
                }

                if (hasVendorRole && hasEmployerRole)
                {
                    var legacyEmployerItems = navigationModel.CustomerNavigationItems
                        .Where(item =>
                            string.Equals(item.RouteName, NopRouteNames.Standard.CUSTOMER_VENDOR_INFO, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.RouteName, AIInterviewDefaults.VendorScoreboardRouteName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.RouteName, AIInterviewDefaults.EmployerApplicationsRouteName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.RouteName, AIInterviewDefaults.MockEmployerManageRouteName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.ItemClass, "customer-vendor-info", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var legacyEmployerItem in legacyEmployerItems)
                        navigationModel.CustomerNavigationItems.Remove(legacyEmployerItem);

                    if (!navigationModel.CustomerNavigationItems.Any(item =>
                        string.Equals(item.RouteName, AIInterviewDefaults.EmployerDashboardRouteName, StringComparison.OrdinalIgnoreCase)))
                    {
                        navigationModel.CustomerNavigationItems.Add(new Nop.Web.Models.Customer.CustomerNavigationItemModel
                        {
                            RouteName = AIInterviewDefaults.EmployerDashboardRouteName,
                            Title = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Dashboard.Title"),
                            Tab = AIInterviewDefaults.EmployerDashboardNavigationTab,
                            ItemClass = "vendor-employer-dashboard"
                        });
                    }
                }

                if (hasVendorRole && hasInstituteRole)
                {
                    var legacyInstituteItems = navigationModel.CustomerNavigationItems
                        .Where(item =>
                            string.Equals(item.RouteName, AIInterviewDefaults.InstituteCandidatesRouteName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.RouteName, AIInterviewDefaults.InstituteCreditsRouteName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.ItemClass, "institute-nav-candidates", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.ItemClass, "institute-nav-credits", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var legacyInstituteItem in legacyInstituteItems)
                        navigationModel.CustomerNavigationItems.Remove(legacyInstituteItem);

                    if (!navigationModel.CustomerNavigationItems.Any(item =>
                        string.Equals(item.RouteName,
                            AIInterviewDefaults.InstituteDashboardRouteName,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        navigationModel.CustomerNavigationItems.Add(
                            new Nop.Web.Models.Customer.CustomerNavigationItemModel
                            {
                                RouteName = AIInterviewDefaults.InstituteDashboardRouteName,
                                Title = "Institute Dashboard",
                                Tab = AIInterviewDefaults.InstituteDashboardNavigationTab,
                                ItemClass = "institute-dashboard"
                            });
                    }
                }
            }
        }

        // Handle ProductDetailsModel to inject credit/sponsor messages
        if (eventMessage.Model.GetType().Name == "ProductDetailsModel")
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer != null)
            {
                var wallet = await _creditService.GetOrCreateWalletAsync(customer.Id);
                var hasCredits = wallet.Balance >= 1;

                var model = eventMessage.Model;
                var customPropertiesProperty = model.GetType().GetProperty("CustomProperties");
                if (customPropertiesProperty != null)
                {
                    var customProperties = customPropertiesProperty.GetValue(model) as System.Collections.Generic.Dictionary<string, object>;
                    if (customProperties != null)
                    {
                        if (!hasCredits)
                        {
                            customProperties["AIInterview.CreditErrorMessage"] = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.Error.NoCredits");
                        }

                        // Sponsor message could be handled similarly if we have a sponsor token in the URL
                    }
                }
            }
        }
    }
}
