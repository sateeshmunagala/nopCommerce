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

    public EventConsumer(ILocalizationService localizationService,
        Services.ICreditService creditService,
        Nop.Core.IWorkContext workContext)
    {
        _localizationService = localizationService;
        _creditService = creditService;
        _workContext = workContext;
    }

    public async Task HandleEventAsync(ModelPreparedEvent<BaseNopModel> eventMessage)
    {
        // Add "My Applications" link to customer account navigation
        if (eventMessage.Model.GetType().Name == "CustomerNavigationModel")
        {
            var model = eventMessage.Model;
            var itemsProperty = model.GetType().GetProperty("CustomerNavigationItems");
            if (itemsProperty != null)
            {
                var items = itemsProperty.GetValue(model) as System.Collections.IList;
                if (items != null)
                {
                    var itemType = model.GetType().Assembly.GetType("Nop.Web.Models.Customer.CustomerNavigationItemModel");
                    if (itemType != null)
                    {
                        var newItem = System.Activator.CreateInstance(itemType);
                        itemType.GetProperty("RouteName")?.SetValue(newItem, AIInterviewDefaults.MyApplicationsRouteName);
                        itemType.GetProperty("Title")?.SetValue(newItem, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.MyApplications.Title"));
                        itemType.GetProperty("ItemClass")?.SetValue(newItem, "customer-applications");
                        items.Add(newItem);
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
