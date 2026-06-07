namespace Nop.Plugin.Misc.AIInterview.Domain;

public static class JobApplicationStatuses
{
    public const string Pending = "Pending";
    public const string Applied = "Applied";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Reviewed = "Reviewed";
    public const string Shortlisted = "Shortlisted";
    public const string Rejected = "Rejected";
    public const string Withdrawn = "Withdrawn";

    public static readonly string[] All =
    [
        Pending,
        Applied,
        InProgress,
        Completed,
        Reviewed,
        Shortlisted,
        Rejected,
        Withdrawn
    ];

    public static bool CanReapply(string status)
    {
        return string.Equals(status, Rejected, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, Withdrawn, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValid(string status)
    {
        return All.Contains(status, StringComparer.OrdinalIgnoreCase);
    }

    public static string Normalize(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return Pending;

        var compactStatus = status.Replace(" ", string.Empty);
        return All.FirstOrDefault(value => string.Equals(value, compactStatus, StringComparison.OrdinalIgnoreCase))
            ?? status;
    }
}
