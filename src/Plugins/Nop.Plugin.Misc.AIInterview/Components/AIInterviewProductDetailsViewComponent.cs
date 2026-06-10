using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Catalog;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Services.Catalog;
using Nop.Web.Framework.Components;
using Nop.Web.Framework.Models;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class AIInterviewProductDetailsViewComponent : NopViewComponent
{
    private readonly ICreditService _creditService;
    private readonly IProductAttributeService _productAttributeService;
    private readonly IJobInterviewExperienceService _jobInterviewExperienceService;
    private readonly IProductService _productService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly IWorkContext _workContext;
    private readonly IApplicationService _applicationService;
    private readonly IJobRequirementService _jobRequirementService;
    private readonly AIInterviewSettings _aiInterviewSettings;

    public AIInterviewProductDetailsViewComponent(ICreditService creditService,
        IWorkContext workContext,
        IProductAttributeService productAttributeService,
        IJobInterviewExperienceService jobInterviewExperienceService,
        IProductService productService,
        IProductTemplateService productTemplateService,
        IApplicationService applicationService,
        AIInterviewSettings aiInterviewSettings,
        IJobRequirementService jobRequirementService = null)
    {
        _creditService = creditService;
        _workContext = workContext;
        _productAttributeService = productAttributeService;
        _jobInterviewExperienceService = jobInterviewExperienceService;
        _productService = productService;
        _productTemplateService = productTemplateService;
        _applicationService = applicationService;
        _jobRequirementService = jobRequirementService;
        _aiInterviewSettings = aiInterviewSettings;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        if (additionalData is not ProductDetailsModel model)
            return Content("");
        var productId = model.Id;
        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return Content("");

        var productTemplate = await _productTemplateService.GetProductTemplateByIdAsync(product.ProductTemplateId);
        if (productTemplate == null ||
            !string.Equals(productTemplate.ViewPath, AIInterviewDefaults.JobProductTemplateViewPath, StringComparison.OrdinalIgnoreCase))
            return Content("");

        await _jobInterviewExperienceService.EnsureInterviewDifficultyAttributeAsync(product);
        await EnsureDifficultyAttributeModelAsync(model, productId);

        var jobRequirements = _jobRequirementService == null
            ? new JobRequirementsModel
            {
                ResumeRequired = _aiInterviewSettings.ResumeRequired,
                InterviewRequired = _aiInterviewSettings.InterviewRequired
            }
            : await _jobRequirementService.GetRequirementsAsync(product);
        ViewBag.ResumeRequired = jobRequirements.ResumeRequired;
        ViewBag.InterviewRequired = jobRequirements.InterviewRequired;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var hasCredits = false;
        var alreadyApplied = false;
        if (customer != null)
        {
            var wallet = await _creditService.GetOrCreateWalletAsync(customer.Id);
            hasCredits = wallet.Balance >= 1;

            var applications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id) ?? new List<JobApplication>();
            alreadyApplied = applications.Any(application =>
                application.ProductId == productId &&
                !JobApplicationStatuses.CanReapply(application.Status));
        }

        ViewBag.HasCredits = hasCredits;
        ViewBag.AlreadyApplied = alreadyApplied;
        ViewBag.ProductId = productId;
        ViewBag.IsAuthenticated = customer != null;
        ViewBag.CreditPurchasePageUrl = NormalizeCreditPurchasePageUrl(_aiInterviewSettings?.CreditPurchasePageUrl);

        var sponsorToken = HttpContext?.Request?.Query?["sponsorToken"].ToString() ?? "";
        ViewBag.SponsorToken = sponsorToken;

        // Verify sponsor token validity
        bool hasSponsorCredits = false;
        if (!string.IsNullOrEmpty(sponsorToken))
        {
            var inviteService = HttpContext.RequestServices.GetService(typeof(ISponsorInviteService)) as ISponsorInviteService;
            if (inviteService != null)
            {
                var invite = await inviteService.GetSponsorInviteByCodeAsync(sponsorToken);
                if (invite != null && !invite.IsAccepted && (!invite.ExpiryDateUtc.HasValue || invite.ExpiryDateUtc > DateTime.UtcNow) && invite.Email.Equals(customer.Email, StringComparison.OrdinalIgnoreCase))
                {
                    var sponsorWallet = await _creditService.GetOrCreateWalletAsync(invite.SponsorId);
                    if (sponsorWallet.Balance >= 1)
                    {
                        hasSponsorCredits = true;
                    }
                }
            }
        }

        ViewBag.HasSponsorCredits = hasSponsorCredits;

        return View("~/Plugins/Misc.AIInterview/Views/Shared/Components/AIInterviewProductDetails/Default.cshtml", model);
    }

    protected virtual string NormalizeCreditPurchasePageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return AIInterviewDefaults.DefaultCreditPurchasePageUrl;

        if (Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;

        return url.StartsWith("/", StringComparison.Ordinal) ? url : "/" + url;
    }

    protected virtual async Task EnsureDifficultyAttributeModelAsync(ProductDetailsModel model, int productId)
    {
        if (model.ProductAttributes.Any(attribute =>
                string.Equals(attribute.Name, AIInterviewDefaults.InterviewDifficultyAttributeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(attribute.TextPrompt, AIInterviewDefaults.InterviewDifficultyAttributeName, StringComparison.OrdinalIgnoreCase)))
            return;

        var mappings = await _productAttributeService.GetProductAttributeMappingsByProductIdAsync(productId);
        foreach (var mapping in mappings)
        {
            var attribute = await _productAttributeService.GetProductAttributeByIdAsync(mapping.ProductAttributeId);
            if (attribute == null ||
                !string.Equals(attribute.Name, AIInterviewDefaults.InterviewDifficultyAttributeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = await _productAttributeService.GetProductAttributeValuesAsync(mapping.Id);
            model.ProductAttributes.Insert(0, new ProductDetailsModel.ProductAttributeModel
            {
                Id = mapping.Id,
                ProductId = productId,
                ProductAttributeId = attribute.Id,
                Name = attribute.Name,
                TextPrompt = string.IsNullOrWhiteSpace(mapping.TextPrompt) ? attribute.Name : mapping.TextPrompt,
                IsRequired = mapping.IsRequired,
                AttributeControlType = mapping.AttributeControlType,
                Values = values.Select(value => new ProductDetailsModel.ProductAttributeValueModel
                {
                    Id = value.Id,
                    Name = value.Name,
                    IsPreSelected = value.IsPreSelected
                }).ToList()
            });
            break;
        }
    }
}
