using Microsoft.AspNetCore.Mvc;
using System.Globalization;
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

            ViewBag.ResumeRequired = ResolvePostedBool("AIInterviewJobResumeRequired", requirements.ResumeRequired);
            ViewBag.InterviewRequired = ResolvePostedBool("AIInterviewJobInterviewRequired", requirements.InterviewRequired);
            ViewBag.MinimumScore = ResolvePostedDecimal("AIInterviewJobMinimumScore", requirements.MinimumScore);
            ViewBag.QuestionCount = ResolvePostedInt("AIInterviewJobQuestionCount", requirements.QuestionCount);
        }
        else if (model.ProductTemplateId > 0)
        {
            var productTemplate = await _productTemplateService.GetProductTemplateByIdAsync(model.ProductTemplateId);
            isJobTemplate = productTemplate != null &&
                (string.Equals(productTemplate.ViewPath, AIInterviewDefaults.JobProductTemplateViewPath, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(productTemplate.Name, AIInterviewDefaults.JobProductTemplateName, StringComparison.OrdinalIgnoreCase));

            if (!isJobTemplate)
                return Content(string.Empty);

            ViewBag.ResumeRequired = ResolvePostedBool("AIInterviewJobResumeRequired", false);
            ViewBag.InterviewRequired = ResolvePostedBool("AIInterviewJobInterviewRequired", false);
            ViewBag.MinimumScore = ResolvePostedDecimal("AIInterviewJobMinimumScore", 0m);
            ViewBag.QuestionCount = ResolvePostedInt("AIInterviewJobQuestionCount", 3);
        }

        ViewBag.ProductId = model.Id;
        ViewBag.IsJobTemplate = isJobTemplate;
        return View("~/Plugins/Misc.AIInterview/Views/Admin/_ProductJobRequirements.cshtml", model);
    }

    protected virtual bool ResolvePostedBool(string fieldName, bool fallback)
    {
        var request = HttpContext?.Request;
        if (request?.HasFormContentType == true)
        {
            var form = request.Form;
            if (form.TryGetValue(fieldName, out var values))
            {
                var parsedAny = false;
                foreach (var value in values)
                {
                    if (bool.TryParse(value, out var parsed))
                    {
                        parsedAny = true;
                        if (parsed)
                            return true;
                    }
                }

                if (parsedAny)
                    return false;
            }
        }

        return fallback;
    }

    protected virtual decimal ResolvePostedDecimal(string fieldName, decimal fallback)
    {
        var request = HttpContext?.Request;
        if (request?.HasFormContentType == true)
        {
            var form = request.Form;
            if (form.TryGetValue(fieldName, out var values))
            {
                var hasPostedValue = false;
                foreach (var value in values)
                {
                    hasPostedValue = true;
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var parsed) ||
                        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                        return parsed;
                }

                if (hasPostedValue)
                    return 0m;
            }
        }

        return fallback;
    }

    protected virtual int ResolvePostedInt(string fieldName, int fallback)
    {
        var request = HttpContext?.Request;
        if (request?.HasFormContentType == true)
        {
            var form = request.Form;
            if (form.TryGetValue(fieldName, out var values))
            {
                foreach (var value in values)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed) ||
                        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    {
                        return parsed;
                    }
                }
            }
        }

        return fallback;
    }
}
