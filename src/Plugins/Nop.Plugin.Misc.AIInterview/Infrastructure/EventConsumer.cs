using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public class EventConsumer : IConsumer<ModelPreparedEvent<BaseNopModel>>
{
    private readonly ILocalizationService _localizationService;
    private readonly Services.ICreditService _creditService;
    private readonly Nop.Core.IWorkContext _workContext;
    private readonly AIInterviewSettings _aiInterviewSettings;

    public EventConsumer(ILocalizationService localizationService,
        Services.ICreditService creditService,
        Nop.Core.IWorkContext workContext,
        AIInterviewSettings aiInterviewSettings)
    {
        _localizationService = localizationService;
        _creditService = creditService;
        _workContext = workContext;
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

                if (!navigationModel.CustomerNavigationItems.Any(item =>
                    string.Equals(item.RouteName, AIInterviewDefaults.MyActivityRouteName, StringComparison.OrdinalIgnoreCase)))
                {
                    navigationModel.CustomerNavigationItems.Add(new Nop.Web.Models.Customer.CustomerNavigationItemModel
                    {
                        RouteName = AIInterviewDefaults.MyActivityRouteName,
                        Title = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.MyActivity.Title"),
                        Tab = AIInterviewDefaults.MyActivityNavigationTab,
                        ItemClass = "customer-my-activity"
                    });
                }

                var customer = await _workContext.GetCurrentCustomerAsync();
                if (customer != null && customer.VendorId > 0)
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
