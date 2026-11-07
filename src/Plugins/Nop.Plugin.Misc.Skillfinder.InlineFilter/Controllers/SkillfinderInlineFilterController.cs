using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.Skillfinder.InlineFilter.Services;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.Skillfinder.InlineFilter.Controllers;

public class SkillfinderInlineFilterController : BasePluginController
{
    private readonly IInlineFilterModelService _inlineFilterModelService;

    public SkillfinderInlineFilterController(IInlineFilterModelService inlineFilterModelService)
    {
        _inlineFilterModelService = inlineFilterModelService;
    }

    [HttpGet]
    public async Task<IActionResult> GetFilteredResults(string categorySeName = null)
    {
        var model = await _inlineFilterModelService.PrepareFilteredProductsGridModelAsync(categorySeName);
        return PartialView(
            "~/Plugins/Misc.Skillfinder.InlineFilter/Views/_FilteredProductsGrid.cshtml",
            model);
    }
}
