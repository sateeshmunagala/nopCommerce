using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.Skillfinder.InlineFilter.Services;
using Nop.Web.Framework.Components;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Misc.Skillfinder.InlineFilter.Components;

public class InlineFilterWidgetViewComponent : NopViewComponent
{
    private readonly IInlineFilterModelService _inlineFilterModelService;

    public InlineFilterWidgetViewComponent(IInlineFilterModelService inlineFilterModelService)
    {
        _inlineFilterModelService = inlineFilterModelService;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        if (!string.Equals(widgetZone, PublicWidgetZones.HomepageBeforeProducts, StringComparison.OrdinalIgnoreCase))
            return Content(string.Empty);

        var model = await _inlineFilterModelService.PreparePublicInfoModelAsync();
        return View("~/Plugins/Misc.Skillfinder.InlineFilter/Views/PublicInfo.cshtml", model);
    }
}
