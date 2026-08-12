using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class VendorPortalLogoViewComponent : NopViewComponent
{
    public Task<IViewComponentResult> InvokeAsync()
    {
        var logoUrl = HttpContext.Items.TryGetValue(
            AIInterviewDefaults.VendorPortalLogoUrlKey, out var url)
            ? url as string
            : null;

        return Task.FromResult<IViewComponentResult>(
            View("~/Plugins/Misc.AIInterview/Views/Shared/Components/VendorPortalLogo/Default.cshtml",
                (object)logoUrl));
    }
}
