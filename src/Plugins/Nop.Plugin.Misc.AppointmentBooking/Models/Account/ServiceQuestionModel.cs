using Nop.Plugin.Misc.AppointmentBooking.Domains;

namespace Nop.Plugin.Misc.AppointmentBooking.Models.Account;

/// <summary>
/// Represents vendor-managed intake questions for a service
/// </summary>
public record ServiceQuestionModel
{
    public int ServiceId { get; set; }

    public string ServiceName { get; set; }

    public IList<ServiceQuestion> Questions { get; set; } = new List<ServiceQuestion>();

    public string QuestionText { get; set; }

    public string QuestionType { get; set; } = "Text";

    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }

    public string OptionsJson { get; set; }
}
