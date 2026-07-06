using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Catalog;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Infrastructure;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Media;
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
    private readonly IInterviewSessionService _interviewSessionService;
    private readonly IJobRequirementService _jobRequirementService;
    private readonly ISponsorInviteService _sponsorInviteService;
    private readonly AIInterviewSettings _aiInterviewSettings;
    private readonly IDownloadService _downloadService;

    public AIInterviewProductDetailsViewComponent(ICreditService creditService,
        IWorkContext workContext,
        IProductAttributeService productAttributeService,
        IJobInterviewExperienceService jobInterviewExperienceService,
        IProductService productService,
        IProductTemplateService productTemplateService,
        IApplicationService applicationService,
        IInterviewSessionService interviewSessionService,
        AIInterviewSettings aiInterviewSettings,
        IJobRequirementService jobRequirementService,
        ISponsorInviteService sponsorInviteService,
        IDownloadService downloadService)
    {
        _creditService = creditService;
        _workContext = workContext;
        _productAttributeService = productAttributeService;
        _jobInterviewExperienceService = jobInterviewExperienceService;
        _productService = productService;
        _productTemplateService = productTemplateService;
        _applicationService = applicationService;
        _interviewSessionService = interviewSessionService;
        _jobRequirementService = jobRequirementService;
        _sponsorInviteService = sponsorInviteService;
        _aiInterviewSettings = aiInterviewSettings;
        _downloadService = downloadService;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData, string formDomId = null, string contextId = null)
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
            ? new JobRequirementsModel()
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
            ViewBag.AvailableResumes = await ResumeSelectionHelper.BuildResumeSelectListAsync(applications, _downloadService);
        }
        else
            ViewBag.AvailableResumes = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();

        ViewBag.HasCredits = hasCredits;
        ViewBag.AlreadyApplied = alreadyApplied;
        ViewBag.ProductId = productId;
        ViewBag.IsAuthenticated = customer != null;
        ViewBag.CreditPurchasePageUrl = NormalizeCreditPurchasePageUrl(_aiInterviewSettings?.CreditPurchasePageUrl);
        ViewBag.ProductFormId = string.IsNullOrWhiteSpace(formDomId) ? "product-details-form" : formDomId;
        ViewBag.JobAiContextId = string.IsNullOrWhiteSpace(contextId) ? $"job-{productId}" : contextId;

        var sponsorToken = HttpContext?.Request?.Query?["sponsorToken"].ToString() ?? "";
        ViewBag.SponsorToken = sponsorToken;

        bool hasSponsorCredits = false;
        if (customer != null && !string.IsNullOrEmpty(sponsorToken) && _sponsorInviteService != null)
        {
            var invite = await _sponsorInviteService.GetSponsorInviteByCodeAsync(sponsorToken);
            if (invite != null &&
                invite.ProductId == productId &&
                invite.IsActive &&
                (!invite.ExpiryDateUtc.HasValue || invite.ExpiryDateUtc > DateTime.UtcNow) &&
                string.Equals(invite.Email, customer.Email, StringComparison.OrdinalIgnoreCase))
            {
                var sponsorWallet = await _creditService.GetOrCreateWalletAsync(invite.SponsorId);
                var sponsoredAttempts = _interviewSessionService == null
                    ? 0
                    : await _interviewSessionService.GetSponsorInviteAttemptCountAsync(invite.Id);

                if (sponsorWallet.Balance >= 1 && sponsoredAttempts < invite.MaxAttempts)
                {
                    hasSponsorCredits = true;
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
