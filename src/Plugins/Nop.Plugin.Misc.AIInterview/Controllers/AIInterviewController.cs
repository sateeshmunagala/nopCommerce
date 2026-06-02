using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

public class AIInterviewController : BasePluginController
{
    public IActionResult Index()
    {
        return View("~/Plugins/Misc.AIInterview/Views/Index.cshtml");
    }
}
