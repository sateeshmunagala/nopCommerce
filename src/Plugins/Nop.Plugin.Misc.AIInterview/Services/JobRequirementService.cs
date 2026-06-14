using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.AIInterview;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Services.Catalog;
using Nop.Services.Common;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class JobRequirementService : IJobRequirementService
{
    private const int DefaultQuestionCount = 3;
    private const int MinQuestionCount = 1;
    private const int MaxQuestionCount = 10;

    private readonly IGenericAttributeService _genericAttributeService;
    private readonly IProductService _productService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly AIInterviewSettings _aiInterviewSettings;

    public JobRequirementService(IGenericAttributeService genericAttributeService,
        IProductService productService,
        IProductTemplateService productTemplateService,
        AIInterviewSettings aiInterviewSettings = null)
    {
        _genericAttributeService = genericAttributeService;
        _productService = productService;
        _productTemplateService = productTemplateService;
        _aiInterviewSettings = aiInterviewSettings;
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
        model.MinimumScore = await _genericAttributeService.GetAttributeAsync<decimal>(product, AIInterviewDefaults.JobMinimumScoreAttributeName, defaultValue: _aiInterviewSettings?.MinimumScore ?? 0);
        model.QuestionCount = NormalizeQuestionCount(await _genericAttributeService.GetAttributeAsync<int>(product, AIInterviewDefaults.JobQuestionCountAttributeName, defaultValue: DefaultQuestionCount));

        return model;
    }

    public async Task<JobRequirementsModel> GetRequirementsAsync(int productId)
    {
        if (productId <= 0)
            return new JobRequirementsModel();

        var product = await _productService.GetProductByIdAsync(productId);
        return await GetRequirementsAsync(product);
    }

    public async Task SaveRequirementsAsync(Product product, bool resumeRequired, bool interviewRequired, decimal minimumScore = 0, int questionCount = DefaultQuestionCount)
    {
        if (product == null || !await IsJobProductAsync(product))
            return;

        await _genericAttributeService.SaveAttributeAsync(product, AIInterviewDefaults.JobResumeRequiredAttributeName, resumeRequired);
        await _genericAttributeService.SaveAttributeAsync(product, AIInterviewDefaults.JobInterviewRequiredAttributeName, interviewRequired);
        await _genericAttributeService.SaveAttributeAsync(product, AIInterviewDefaults.JobMinimumScoreAttributeName, minimumScore);
        await _genericAttributeService.SaveAttributeAsync(product, AIInterviewDefaults.JobQuestionCountAttributeName, NormalizeQuestionCount(questionCount));
    }

    public async Task SaveRequirementsAsync(int productId, bool resumeRequired, bool interviewRequired, decimal minimumScore = 0, int questionCount = DefaultQuestionCount)
    {
        if (productId <= 0)
            return;

        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return;

        await SaveRequirementsAsync(product, resumeRequired, interviewRequired, minimumScore, questionCount);
    }

    protected virtual int NormalizeQuestionCount(int questionCount)
    {
        return Math.Clamp(questionCount <= 0 ? DefaultQuestionCount : questionCount, MinQuestionCount, MaxQuestionCount);
    }
}
