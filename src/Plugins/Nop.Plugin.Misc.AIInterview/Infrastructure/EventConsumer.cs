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
                navigationModel.CustomerNavigationItems.Add(new Nop.Web.Models.Customer.CustomerNavigationItemModel
                {
                    RouteName = AIInterviewDefaults.MyApplicationsRouteName,
                    Title = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.MyApplications.Title"),
                    Tab = (int)Nop.Web.Models.Customer.CustomerNavigationEnum.Info + 100, // Custom tab enum
                    ItemClass = "customer-applications"
                });

                var customer = await _workContext.GetCurrentCustomerAsync();
                if (customer != null && customer.VendorId > 0)
                {
                    navigationModel.CustomerNavigationItems.Add(new Nop.Web.Models.Customer.CustomerNavigationItemModel
                    {
                        RouteName = "Plugin.Misc.AIInterview.VendorScoreboard",
                        Title = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorScoreboard.Title"),
                        Tab = (int)Nop.Web.Models.Customer.CustomerNavigationEnum.VendorInfo + 1,
                        ItemClass = "vendor-scoreboard"
                    });

                    navigationModel.CustomerNavigationItems.Add(new Nop.Web.Models.Customer.CustomerNavigationItemModel
                    {
                        RouteName = "Plugin.Misc.AIInterview.VendorJobCreation",
                        Title = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Title"),
                        Tab = (int)Nop.Web.Models.Customer.CustomerNavigationEnum.VendorInfo + 2,
                        ItemClass = "vendor-job-creation"
                    });
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
