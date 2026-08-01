using FluentValidation;
using Nop.Plugin.Misc.SqlReports.Admin.Models;
using Nop.Plugin.Misc.SqlReports.Domain;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.Misc.SqlReports.Admin.Validators;

public class SqlReportParameterValidator : BaseNopValidator<SqlReportParameterModel>
{
    public SqlReportParameterValidator(ILocalizationService localizationService)
    {
        RuleFor(model => model.Name)
            .NotEmpty()
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.SqlReports.Parameter.Fields.Name.Required"));

        RuleFor(model => model.ParameterName)
            .NotEmpty()
            .Matches(@"^@?[A-Za-z_][A-Za-z0-9_]*$")
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.SqlReports.Parameter.Fields.ParameterName.Invalid"));

        RuleFor(model => model.DataType)
            .NotEmpty();

        SetDatabaseValidationRules<SqlReportParameter>();
    }
}
