using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Localization;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class AIInterviewHomepageTopPerformersViewComponent : NopViewComponent
{
    private readonly IInterviewSessionService _interviewSessionService;
    private readonly ILocalizationService _localizationService;
    private readonly IStoreContext _storeContext;

    public AIInterviewHomepageTopPerformersViewComponent(
        IInterviewSessionService interviewSessionService,
        ILocalizationService localizationService,
        IStoreContext storeContext)
    {
        _interviewSessionService = interviewSessionService;
        _localizationService = localizationService;
        _storeContext = storeContext;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var performers = await _interviewSessionService.GetHomepageTopPerformersAsync(
            store?.Id ?? 0,
            AIInterviewDefaults.HomepageTopPerformersMaxCount);

        var model = new HomeTopPerformersModel
        {
            Performers = performers ?? new List<HomeTopPerformer>(),
            FallbackAvatarAltText = await _localizationService.GetResourceAsync(AIInterviewDefaults.HomepageTopPerformersAvatarAltResourceKey)
        };

        return View("~/Plugins/Misc.AIInterview/Views/Shared/Components/AIInterviewHomepageTopPerformers/Default.cshtml", model);
    }
}
