using FluentValidation;
using Nop.Plugin.Misc.SqlReports.Admin.Models;
using Nop.Plugin.Misc.SqlReports.Domain;
using Nop.Plugin.Misc.SqlReports.Services;
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
            .Must(dataType => SqlReportDataType.All.Contains(dataType))
            .WithMessage("Select a valid parameter data type.");

        When(model => SqlReportDataType.IsNumber(model.DataType) && !string.IsNullOrWhiteSpace(model.DefaultValue), () =>
        {
            RuleFor(model => model.DefaultValue)
                .Must(value => decimal.TryParse(value, out _))
                .WithMessage("Default value must be numeric.");
        });

        SetDatabaseValidationRules<SqlReportParameter>();
    }
}
