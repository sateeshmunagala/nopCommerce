using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.JobSupport.Components;

public class JobSupportCustomerNavigationViewComponent : NopViewComponent
{
    public IViewComponentResult Invoke() =>
        View("~/Plugins/Misc.JobSupport/Views/Shared/Components/JobSupportCustomerNavigation/Default.cshtml");
}
