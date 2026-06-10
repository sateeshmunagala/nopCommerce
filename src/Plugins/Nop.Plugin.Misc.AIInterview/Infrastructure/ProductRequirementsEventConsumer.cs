using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;
using Nop.Services.Events;
using Nop.Plugin.Misc.AIInterview.Services;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public class ProductRequirementsEventConsumer : IConsumer<EntityInsertedEvent<Product>>, IConsumer<EntityUpdatedEvent<Product>>
{
    private const string ResumeRequiredFieldName = "AIInterviewJobResumeRequired";
    private const string InterviewRequiredFieldName = "AIInterviewJobInterviewRequired";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IJobRequirementService _jobRequirementService;

    public ProductRequirementsEventConsumer(IHttpContextAccessor httpContextAccessor,
        IJobRequirementService jobRequirementService)
    {
        _httpContextAccessor = httpContextAccessor;
        _jobRequirementService = jobRequirementService;
    }

    public async Task HandleEventAsync(EntityInsertedEvent<Product> eventMessage)
    {
        await PersistRequirementsAsync(eventMessage?.Entity);
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Product> eventMessage)
    {
        await PersistRequirementsAsync(eventMessage?.Entity);
    }

    protected virtual async Task PersistRequirementsAsync(Product product)
    {
        if (product == null || _jobRequirementService == null)
            return;

        var httpContext = _httpContextAccessor?.HttpContext;
        var request = httpContext?.Request;
        if (request == null || !request.HasFormContentType)
            return;

        if (!request.Form.ContainsKey(ResumeRequiredFieldName) &&
            !request.Form.ContainsKey(InterviewRequiredFieldName))
        {
            return;
        }

        if (!await _jobRequirementService.IsJobProductAsync(product))
            return;

        var resumeRequired = IsTrue(request.Form[ResumeRequiredFieldName]);
        var interviewRequired = IsTrue(request.Form[InterviewRequiredFieldName]);

        await _jobRequirementService.SaveRequirementsAsync(product, resumeRequired, interviewRequired);
    }

    protected static bool IsTrue(StringValues values)
    {
        foreach (var value in values)
        {
            if (bool.TryParse(value, out var parsed) && parsed)
                return true;
        }

        return false;
    }
}
