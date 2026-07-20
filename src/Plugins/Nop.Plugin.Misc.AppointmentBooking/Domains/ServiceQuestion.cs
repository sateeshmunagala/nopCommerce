using Nop.Core;

namespace Nop.Plugin.Misc.AppointmentBooking.Domains;

/// <summary>
/// Represents an intake question for a bookable service
/// </summary>
public class ServiceQuestion : BaseEntity
{
    public int ServiceId { get; set; }

    public string QuestionText { get; set; }

    public string QuestionType { get; set; }

    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }

    public string OptionsJson { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}
