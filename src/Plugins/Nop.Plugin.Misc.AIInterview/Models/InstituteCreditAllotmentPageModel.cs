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
    public decimal TotalCredits { get; init; }
    public decimal AvailableCredits { get; init; }
    public decimal ConsumedCredits { get; init; }
    public IList<InstituteCreditCandidateModel> AcceptedCandidates { get; init; }
        = new List<InstituteCreditCandidateModel>();
    public int SelectedCandidateCustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Remarks { get; set; }
}

public record InstituteDashboardPageModel
{
    public string ActiveTab { get; init; }
    public int SelectedNavigationTab { get; init; }
    public string VendorName { get; init; }
    public string JoinUrl { get; init; }
    public string JoinUrlUnavailableMessage { get; init; }
    public decimal TotalCredits { get; init; }
    public decimal AvailableCredits { get; init; }
    public decimal ConsumedCredits { get; init; }
    public IList<InstituteCandidateModel> Candidates { get; init; } = new List<InstituteCandidateModel>();
    public string TransferMessage { get; init; }
    public bool TransferSucceeded { get; init; }
}
