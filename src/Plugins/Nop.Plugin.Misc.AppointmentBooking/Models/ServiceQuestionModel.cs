using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Models;

/// <summary>
/// Represents an intake question shown to customers
/// </summary>
public record ServiceQuestionModel : BaseNopEntityModel
{
    public string QuestionText { get; set; }

    public string QuestionType { get; set; }

    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }

    public string OptionsJson { get; set; }
}
