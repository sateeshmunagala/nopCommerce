using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Models.Admin;

public record ServiceQuestionAdminModel : BaseNopEntityModel
{
    public int ServiceId { get; set; }

    public string QuestionText { get; set; }

    public string QuestionType { get; set; }

    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }

    public string OptionsJson { get; set; }
}
