using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Web.Framework.Components;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class AIInterviewJobProductCardViewComponent : NopViewComponent
{
    private readonly IAIInterviewJobDisplayService _aiInterviewJobDisplayService;

    public AIInterviewJobProductCardViewComponent(IAIInterviewJobDisplayService aiInterviewJobDisplayService)
    {
        _aiInterviewJobDisplayService = aiInterviewJobDisplayService;
    }

    public async Task<IViewComponentResult> InvokeAsync(ProductOverviewModel productOverview)
    {
        var model = await _aiInterviewJobDisplayService.PrepareJobProductCardModelAsync(productOverview);
        if (model == null)
            return Content(string.Empty);

        return View("~/Plugins/Misc.AIInterview/Views/Shared/Components/AIInterviewJobProductCard/Default.cshtml", model);
    }
}
