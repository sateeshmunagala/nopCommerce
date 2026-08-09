namespace Nop.Plugin.Misc.AIInterview.Models;

public record InstituteCandidateModel
{
    public int CustomerId { get; init; }
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

public record InstituteApplicantLedgerFilterModel
{
    public int ApplicantCustomerId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 12;
}

public record InstituteApplicantLedgerRowModel
{
    public DateTime CreatedOnUtc { get; init; }
    public string Action { get; init; }
    public decimal Amount { get; init; }
    public decimal RunningBalance { get; init; }
    public string Source { get; init; }
    public string Remarks { get; init; }
}

public record InstituteApplicantLedgerModalModel
{
    public int ApplicantCustomerId { get; init; }
    public string ApplicantName { get; init; }
    public string ApplicantEmail { get; init; }
    public decimal CurrentBalance { get; init; }
    public decimal TotalDeposits { get; init; }
    public decimal TotalWithdrawals { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 12;
    public int TotalRows { get; init; }
    public int TotalPages { get; init; }
    public InstituteApplicantLedgerFilterModel Filters { get; init; }
    public IList<InstituteApplicantLedgerRowModel> Rows { get; init; } = new List<InstituteApplicantLedgerRowModel>();
}

public record InstituteApplicantInterviewsPageModel
{
    public int ApplicantCustomerId { get; init; }
    public string ApplicantName { get; init; }
    public string ApplicantEmail { get; init; }
    public IList<InterviewHistoryItemModel> Sessions { get; init; } = new List<InterviewHistoryItemModel>();
}
