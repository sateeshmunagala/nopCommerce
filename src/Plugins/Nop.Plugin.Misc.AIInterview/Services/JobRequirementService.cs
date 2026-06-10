using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Services.Catalog;
using Nop.Services.Common;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class JobRequirementService : IJobRequirementService
{
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly IProductService _productService;
    private readonly IProductTemplateService _productTemplateService;

    public JobRequirementService(IGenericAttributeService genericAttributeService,
        IProductService productService,
        IProductTemplateService productTemplateService)
    {
        _genericAttributeService = genericAttributeService;
        _productService = productService;
        _productTemplateService = productTemplateService;
    }

    public async Task<bool> IsJobProductAsync(Product product)
    {
        if (product == null || product.ProductTemplateId <= 0 || _productTemplateService == null)
            return false;

        var productTemplate = await _productTemplateService.GetProductTemplateByIdAsync(product.ProductTemplateId);
        if (productTemplate == null)
            return false;

        return string.Equals(productTemplate.ViewPath, AIInterviewDefaults.JobProductTemplateViewPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(productTemplate.Name, AIInterviewDefaults.JobProductTemplateName, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<JobRequirementsModel> GetRequirementsAsync(Product product)
    {
        if (product == null)
            return new JobRequirementsModel();

        var model = new JobRequirementsModel
        {
            ProductId = product.Id,
            IsJobProduct = await IsJobProductAsync(product)
        };

        model.ResumeRequired = await _genericAttributeService.GetAttributeAsync<bool>(product, AIInterviewDefaults.JobResumeRequiredAttributeName, defaultValue: false);
        model.InterviewRequired = await _genericAttributeService.GetAttributeAsync<bool>(product, AIInterviewDefaults.JobInterviewRequiredAttributeName, defaultValue: false);

        return model;
    }

    public async Task<JobRequirementsModel> GetRequirementsAsync(int productId)
    {
        if (productId <= 0)
            return new JobRequirementsModel();

        var product = await _productService.GetProductByIdAsync(productId);
        return await GetRequirementsAsync(product);
    }

    public async Task SaveRequirementsAsync(Product product, bool resumeRequired, bool interviewRequired)
    {
        if (product == null || !await IsJobProductAsync(product))
            return;

        await _genericAttributeService.SaveAttributeAsync(product, AIInterviewDefaults.JobResumeRequiredAttributeName, resumeRequired);
        await _genericAttributeService.SaveAttributeAsync(product, AIInterviewDefaults.JobInterviewRequiredAttributeName, interviewRequired);
    }

    public async Task SaveRequirementsAsync(int productId, bool resumeRequired, bool interviewRequired)
    {
        if (productId <= 0)
            return;

        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return;

        await SaveRequirementsAsync(product, resumeRequired, interviewRequired);
    }
}
