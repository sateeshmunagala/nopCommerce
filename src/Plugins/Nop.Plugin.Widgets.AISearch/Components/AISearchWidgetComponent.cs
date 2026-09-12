using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Widgets.AISearch.Components;

public class AISearchWidgetComponent : NopViewComponent
{
    private readonly AISearchSettings _settings;

    public AISearchWidgetComponent(AISearchSettings settings)
    {
        _settings = settings;
    }

    public IViewComponentResult Invoke(string widgetZone, object additionalData)
    {
        if (!_settings.Enabled)
            return Content(string.Empty);

        return View("~/Plugins/Widgets.AISearch/Views/Components/AISearchWidget/Default.cshtml");
    }
}
