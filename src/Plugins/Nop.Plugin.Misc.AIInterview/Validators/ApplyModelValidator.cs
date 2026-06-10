using FluentValidation;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.Misc.AIInterview.Validators;

public class ApplyModelValidator : BaseNopValidator<ApplyModel>
{
    public ApplyModelValidator(ILocalizationService localizationService)
    {
        RuleFor(x => x.JobTitle).NotEmpty().WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.JobTitle.Required"));

        RuleFor(x => x.ResumeFile)
            .Must(x => x == null || (x.Length <= 5 * 1024 * 1024 && (x.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || x.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))))
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid"))
            .When(x => x.ResumeFile != null);
    }
}
