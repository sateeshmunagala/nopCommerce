using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class AuthLayoutInjectorViewComponent : NopViewComponent
{
    private const string ViewPath =
        "~/Plugins/Misc.AIInterview/Views/Shared/Components/AuthLayoutInjector/Default.cshtml";

    public IViewComponentResult Invoke(string widgetZone, object additionalData)
    {
        var isLoginZone = string.Equals(
            widgetZone,
            PublicWidgetZones.LoginTop,
            StringComparison.OrdinalIgnoreCase);
        var isRegisterZone = string.Equals(
            widgetZone,
            PublicWidgetZones.RegisterTop,
            StringComparison.OrdinalIgnoreCase);

        return isLoginZone || isRegisterZone
            ? View(ViewPath)
            : Content(string.Empty);
    }
}
