namespace Nop.Plugin.Misc.AIInterview.Models;

public record InstituteCreditCandidateModel
{
    public int CustomerId { get; init; }
    public string Email { get; init; }
    public string CustomerName { get; init; }
    public decimal CreditBalance { get; init; }
}

public record InstituteCreditAllotmentPageModel
{
    public decimal InstituteBalance { get; init; }
    public IList<InstituteCreditCandidateModel> AcceptedCandidates { get; init; }
        = new List<InstituteCreditCandidateModel>();
    public int SelectedCandidateCustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Remarks { get; set; }
}
