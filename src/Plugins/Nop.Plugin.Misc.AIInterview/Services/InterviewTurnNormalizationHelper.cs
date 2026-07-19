using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Services;

internal static class InterviewTurnNormalizationHelper
{
    internal static IReadOnlyList<InterviewTurn> GetVisibleRuntimeTurns(IEnumerable<InterviewTurn> turns, int maxQuestions)
    {
        var canonicalTurns = GetCanonicalTurns(turns, maxQuestions);
        var answeredTurns = canonicalTurns
            .Where(HasAnswer)
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList();

        var activePendingTurn = GetActivePendingTurn(canonicalTurns, maxQuestions);
        if (activePendingTurn != null)
            answeredTurns.Add(activePendingTurn);

        return answeredTurns;
    }

    internal static IReadOnlyList<InterviewTurn> GetCompletedReportTurns(IEnumerable<InterviewTurn> turns, int maxQuestions)
    {
        return GetCanonicalTurns(turns, maxQuestions)
            .Where(HasAnswer)
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList();
    }

    internal static InterviewTurn GetActivePendingTurn(IEnumerable<InterviewTurn> turns, int maxQuestions)
    {
        var canonicalTurns = GetCanonicalTurns(turns, maxQuestions);
        var answeredCount = canonicalTurns.Count(HasAnswer);
        if (answeredCount >= maxQuestions)
            return null;

        var nextSequenceNumber = Math.Clamp(answeredCount + 1, 1, maxQuestions);
        return canonicalTurns.FirstOrDefault(turn =>
            turn.SequenceNumber == nextSequenceNumber &&
            !HasAnswer(turn));
    }

    internal static int GetAnsweredCount(IEnumerable<InterviewTurn> turns, int maxQuestions)
    {
        return GetCompletedReportTurns(turns, maxQuestions).Count;
    }

    internal static int GetNextSequenceNumber(IEnumerable<InterviewTurn> turns, int maxQuestions)
    {
        var answeredCount = GetAnsweredCount(turns, maxQuestions);
        return answeredCount >= maxQuestions
            ? maxQuestions
            : answeredCount + 1;
    }

    internal static IReadOnlyList<InterviewTurn> GetStalePendingTurns(IEnumerable<InterviewTurn> turns, int maxQuestions)
    {
        var orderedTurns = GetOrderedTurns(turns, maxQuestions);
        if (!orderedTurns.Any())
            return Array.Empty<InterviewTurn>();

        var activePendingTurn = GetActivePendingTurn(orderedTurns, maxQuestions);
        return orderedTurns
            .Where(turn => !HasAnswer(turn) && turn.Id != activePendingTurn?.Id)
            .ToList();
    }

    internal static IReadOnlyList<InterviewTurn> GetCanonicalTurns(IEnumerable<InterviewTurn> turns, int maxQuestions)
    {
        return GetOrderedTurns(turns, maxQuestions)
            .GroupBy(turn => turn.SequenceNumber)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var answeredTurn = group
                    .Where(HasAnswer)
                    .OrderByDescending(turn => turn.AnsweredOnUtc ?? DateTime.MinValue)
                    .ThenByDescending(turn => turn.Id)
                    .FirstOrDefault();
                if (answeredTurn != null)
                    return answeredTurn;

                return group
                    .OrderByDescending(turn => turn.AskedOnUtc)
                    .ThenByDescending(turn => turn.Id)
                    .FirstOrDefault();
            })
            .Where(turn => turn != null)
            .ToList();
    }

    internal static bool HasAnswer(InterviewTurn turn)
    {
        return !string.IsNullOrWhiteSpace(turn?.AnswerText);
    }

    private static IReadOnlyList<InterviewTurn> GetOrderedTurns(IEnumerable<InterviewTurn> turns, int maxQuestions)
    {
        return (turns ?? Enumerable.Empty<InterviewTurn>())
            .Where(turn => turn != null
                && turn.SequenceNumber > 0
                && turn.SequenceNumber <= maxQuestions
                && !string.IsNullOrWhiteSpace(turn.QuestionText))
            .OrderBy(turn => turn.SequenceNumber)
            .ThenBy(turn => turn.Id)
            .ToList();
    }
}
