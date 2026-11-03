using Microsoft.AspNetCore.Http;
using System.Globalization;
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
    private const string MinimumScoreFieldName = "AIInterviewJobMinimumScore";
    private const string QuestionCountFieldName = "AIInterviewJobQuestionCount";

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
            !request.Form.ContainsKey(InterviewRequiredFieldName) &&
            !request.Form.ContainsKey(MinimumScoreFieldName) &&
            !request.Form.ContainsKey(QuestionCountFieldName))
        {
            return;
        }

        if (!await _jobRequirementService.IsJobProductAsync(product))
            return;

        var resumeRequired = IsTrue(request.Form[ResumeRequiredFieldName]);
        var interviewRequired = IsTrue(request.Form[InterviewRequiredFieldName]);
        var minimumScore = ParseDecimal(request.Form[MinimumScoreFieldName]);
        var questionCount = ParseInt(request.Form[QuestionCountFieldName], 3);

        await _jobRequirementService.SaveRequirementsAsync(product, resumeRequired, interviewRequired, minimumScore, questionCount);
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

    protected static decimal ParseDecimal(StringValues values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var parsed) ||
                decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
        }

        return 0m;
    }

    protected static int ParseInt(StringValues values, int fallback)
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

        return fallback;
    }
}
