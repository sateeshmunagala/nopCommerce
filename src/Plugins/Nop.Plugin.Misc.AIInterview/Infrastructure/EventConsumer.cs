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
                var isInstituteVendor = customer != null && customer.VendorId > 0
                    && await _customerService.IsInCustomerRoleAsync(customer, "Institute", true);

                // Only applicants (non-vendor customers) should see My Activity
                if (customer == null || customer.VendorId == 0)
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

                if (customer != null && customer.VendorId > 0)
                {
                    var legacyEmployerItems = navigationModel.CustomerNavigationItems
                        .Where(item =>
                            string.Equals(item.RouteName, NopRouteNames.Standard.CUSTOMER_VENDOR_INFO, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.RouteName, AIInterviewDefaults.VendorScoreboardRouteName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.RouteName, AIInterviewDefaults.EmployerApplicationsRouteName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.RouteName, AIInterviewDefaults.MockEmployerManageRouteName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var legacyEmployerItem in legacyEmployerItems)
                        navigationModel.CustomerNavigationItems.Remove(legacyEmployerItem);

                    if (!isInstituteVendor)
                    {
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
                }

                if (isInstituteVendor)
                {
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

                    if (!navigationModel.CustomerNavigationItems.Any(item =>
                        string.Equals(item.RouteName,
                            AIInterviewDefaults.InstituteCandidatesRouteName,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        navigationModel.CustomerNavigationItems.Add(
                            new Nop.Web.Models.Customer.CustomerNavigationItemModel
                            {
                                RouteName = AIInterviewDefaults.InstituteCandidatesRouteName,
                                Title = "Candidates",
                                Tab = AIInterviewDefaults.InstituteDashboardNavigationTab,
                                ItemClass = "institute-nav-candidates"
                            });
                    }

                    if (!navigationModel.CustomerNavigationItems.Any(item =>
                        string.Equals(item.RouteName,
                            AIInterviewDefaults.InstituteCreditsRouteName,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        navigationModel.CustomerNavigationItems.Add(
                            new Nop.Web.Models.Customer.CustomerNavigationItemModel
                            {
                                RouteName = AIInterviewDefaults.InstituteCreditsRouteName,
                                Title = "Credits",
                                Tab = AIInterviewDefaults.InstituteDashboardNavigationTab,
                                ItemClass = "institute-nav-credits"
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
