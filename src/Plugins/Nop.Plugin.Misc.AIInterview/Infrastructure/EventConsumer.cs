using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public class EventConsumer : IConsumer<ModelPreparedEvent<BaseNopModel>>
{
    private readonly ILocalizationService _localizationService;

    public EventConsumer(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
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
    }
}
