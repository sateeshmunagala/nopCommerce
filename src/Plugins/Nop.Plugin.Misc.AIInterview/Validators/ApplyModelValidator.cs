using FluentValidation;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.Misc.AIInterview.Validators;

public class ApplyModelValidator : BaseNopValidator<ApplyModel>
{
    public ApplyModelValidator(ILocalizationService localizationService, AIInterviewSettings aiInterviewSettings)
    {
        RuleFor(x => x.JobTitle).NotEmpty().WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.JobTitle.Required"));

        RuleFor(x => x.ResumeFile).NotEmpty()
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Required"))
            .When(x => aiInterviewSettings.ResumeRequired);
    }
}
