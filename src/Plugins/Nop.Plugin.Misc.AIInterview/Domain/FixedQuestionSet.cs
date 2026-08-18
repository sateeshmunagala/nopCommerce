using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class FixedQuestionSet : BaseEntity
{
    public int VendorId { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}

public class FixedQuestionSetItem : BaseEntity
{
    public int FixedQuestionSetId { get; set; }
    public int SequenceNumber { get; set; }
    public string QuestionText { get; set; }
    public string RubricHint { get; set; }
    public string ExpectedSignalNotes { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
