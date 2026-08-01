using FluentValidation;
using Nop.Plugin.Misc.SqlReports.Admin.Models;
using Nop.Plugin.Misc.SqlReports.Domain;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.Misc.SqlReports.Admin.Validators;

public class SqlReportValidator : BaseNopValidator<SqlReportModel>
{
    public SqlReportValidator(ILocalizationService localizationService)
    {
        RuleFor(model => model.Name)
            .NotEmpty()
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.SqlReports.Report.Fields.Name.Required"));

        RuleFor(model => model.SqlQuery)
            .NotEmpty()
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.SqlReports.Report.Fields.SqlQuery.Required"));

        SetDatabaseValidationRules<SqlReport>();
    }
}
