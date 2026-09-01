namespace Nop.Plugin.Misc.JobSupport.Contracts;

public record ContactRevealDecision
{
    public bool Succeeded { get; init; }
    public bool AlreadyRevealed { get; init; }
    public string Email { get; init; }
    public string Phone { get; init; }
    public int RemainingCredits { get; init; }
    public string MessageKey { get; init; }
}

public record SubscriptionSummary
{
    public SubscriptionStatus Status { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public int AllottedCredits { get; init; }
    public int UsedCredits { get; init; }
    public int RemainingCredits => Math.Max(0, AllottedCredits - UsedCredits);
}
