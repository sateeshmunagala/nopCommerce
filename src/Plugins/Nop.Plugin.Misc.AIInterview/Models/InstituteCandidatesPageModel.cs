namespace Nop.Plugin.Misc.AIInterview.Models;

public record InstituteCandidateModel
{
    public int InviteId { get; init; }
    public string Email { get; init; }
    public string CustomerName { get; init; }
    public bool IsAccepted { get; init; }
    public bool IsActive { get; init; }
    public decimal CreditBalance { get; init; }
    public DateTime CreatedOnUtc { get; init; }
}

public record InstituteCandidatesPageModel
{
    public IList<InstituteCandidateModel> Candidates { get; init; } = new List<InstituteCandidateModel>();
}
