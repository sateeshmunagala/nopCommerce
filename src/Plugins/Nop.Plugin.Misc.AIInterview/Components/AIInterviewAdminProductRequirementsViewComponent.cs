using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Web.Areas.Admin.Models.Catalog;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class AIInterviewAdminProductRequirementsViewComponent : NopViewComponent
{
    private readonly IProductService _productService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly IJobRequirementService _jobRequirementService;

    public AIInterviewAdminProductRequirementsViewComponent(IProductService productService,
        IProductTemplateService productTemplateService,
        IJobRequirementService jobRequirementService)
    {
        _productService = productService;
        _productTemplateService = productTemplateService;
        _jobRequirementService = jobRequirementService;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        if (additionalData is not ProductModel model)
            return Content(string.Empty);

        Product product = null;
        if (model.Id > 0)
            product = await _productService.GetProductByIdAsync(model.Id);

        var isJobTemplate = false;
        if (product != null)
        {
            var requirements = await _jobRequirementService.GetRequirementsAsync(product);
            if (!requirements.IsJobProduct)
                return Content(string.Empty);

            isJobTemplate = true;
            model = new ProductModel
            {
                Id = product.Id
            };

            ViewBag.ResumeRequired = requirements.ResumeRequired;
            ViewBag.InterviewRequired = requirements.InterviewRequired;
        }
        else if (model.ProductTemplateId > 0)
        {
            var productTemplate = await _productTemplateService.GetProductTemplateByIdAsync(model.ProductTemplateId);
            isJobTemplate = productTemplate != null &&
                (string.Equals(productTemplate.ViewPath, AIInterviewDefaults.JobProductTemplateViewPath, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(productTemplate.Name, AIInterviewDefaults.JobProductTemplateName, StringComparison.OrdinalIgnoreCase));

            if (!isJobTemplate)
                return Content(string.Empty);

            ViewBag.ResumeRequired = false;
            ViewBag.InterviewRequired = false;
        }

        ViewBag.ProductId = model.Id;
        ViewBag.IsJobTemplate = isJobTemplate;
        return View("~/Plugins/Misc.AIInterview/Views/Admin/_ProductJobRequirements.cshtml", model);
    }
}
