using Nop.Plugin.Misc.JobSupport.Contracts;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportLegacyParityService : IJobSupportLegacyParityService
{
    public ProfileComparisonResult Compare(PagedProfileSearchResult expected,
        PagedProfileSearchResult actual,
        bool ignoreFormatting = false)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var result = new ProfileComparisonResult();
        var expectedItems = expected.Items ?? new List<ProfileSearchResult>();
        var actualItems = actual.Items ?? new List<ProfileSearchResult>();

        CompareMembership(expectedItems, actualItems, result);
        CompareOrder(expectedItems, actualItems, result);
        ComparePaging(expected, actual, result);
        CompareFields(expectedItems, actualItems, ignoreFormatting, result);

        return result;
    }

    private static void CompareMembership(IList<ProfileSearchResult> expected,
        IList<ProfileSearchResult> actual,
        ProfileComparisonResult result)
    {
        var actualCounts = actual.GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var expectedItem in expected)
        {
            if (actualCounts.TryGetValue(expectedItem.Id, out var count) && count > 0)
                actualCounts[expectedItem.Id] = count - 1;
            else
                result.MissingFromPlugin.Add(expectedItem.Id);
        }

        var expectedCounts = expected.GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var actualItem in actual)
        {
            if (expectedCounts.TryGetValue(actualItem.Id, out var count) && count > 0)
                expectedCounts[actualItem.Id] = count - 1;
            else
                result.UnexpectedInPlugin.Add(actualItem.Id);
        }
    }

    private static void CompareOrder(IList<ProfileSearchResult> expected,
        IList<ProfileSearchResult> actual,
        ProfileComparisonResult result)
    {
        var comparableCount = Math.Min(expected.Count, actual.Count);
        for (var index = 0; index < comparableCount; index++)
        {
            if (expected[index].Id != actual[index].Id)
            {
                result.OrderDifferences.Add(
                    $"Position {index}: expected profile {expected[index].Id}, actual profile {actual[index].Id}.");
            }
        }

        if (expected.Count != actual.Count)
            result.OrderDifferences.Add($"Row count: expected {expected.Count}, actual {actual.Count}.");
    }

    private static void ComparePaging(PagedProfileSearchResult expected,
        PagedProfileSearchResult actual,
        ProfileComparisonResult result)
    {
        AddDifference(result.PagingDifferences, nameof(expected.PageIndex), expected.PageIndex, actual.PageIndex);
        AddDifference(result.PagingDifferences, nameof(expected.PageSize), expected.PageSize, actual.PageSize);
        AddDifference(result.PagingDifferences, nameof(expected.TotalRecords), expected.TotalRecords, actual.TotalRecords);
        AddDifference(result.PagingDifferences, nameof(expected.OutputTotalRecords), expected.OutputTotalRecords, actual.OutputTotalRecords);
        AddDifference(result.PagingDifferences, nameof(expected.ReturnedRowCount), expected.ReturnedRowCount, actual.ReturnedRowCount);
    }

    private static void CompareFields(IList<ProfileSearchResult> expected,
        IList<ProfileSearchResult> actual,
        bool ignoreFormatting,
        ProfileComparisonResult result)
    {
        var actualById = actual.GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => new Queue<ProfileSearchResult>(group));

        foreach (var expectedItem in expected)
        {
            if (!actualById.TryGetValue(expectedItem.Id, out var matches) || matches.Count == 0)
                continue;

            var actualItem = matches.Dequeue();
            AddFieldDifference(result, expectedItem.Id, nameof(expectedItem.Id), expectedItem.Id, actualItem.Id);
            AddFieldDifference(result, expectedItem.Id, nameof(expectedItem.CustomerProfileTypeId), expectedItem.CustomerProfileTypeId, actualItem.CustomerProfileTypeId);
            AddStringFieldDifference(result, expectedItem.Id, nameof(expectedItem.ProfileType), expectedItem.ProfileType, actualItem.ProfileType, ignoreFormatting);
            AddStringFieldDifference(result, expectedItem.Id, nameof(expectedItem.PrimaryTechnology), expectedItem.PrimaryTechnology, actualItem.PrimaryTechnology, ignoreFormatting);
            AddStringFieldDifference(result, expectedItem.Id, nameof(expectedItem.SecondaryTechnology), expectedItem.SecondaryTechnology, actualItem.SecondaryTechnology, ignoreFormatting);
            AddStringFieldDifference(result, expectedItem.Id, nameof(expectedItem.CurrentAvailability), expectedItem.CurrentAvailability, actualItem.CurrentAvailability, ignoreFormatting);
            AddFieldDifference(result, expectedItem.Id, nameof(expectedItem.ProfileShortListed), expectedItem.ProfileShortListed, actualItem.ProfileShortListed);
            AddFieldDifference(result, expectedItem.Id, nameof(expectedItem.InterestSent), expectedItem.InterestSent, actualItem.InterestSent);
            AddFieldDifference(result, expectedItem.Id, nameof(expectedItem.PremiumCustomer), expectedItem.PremiumCustomer, actualItem.PremiumCustomer);
            AddStringFieldDifference(result, expectedItem.Id, nameof(expectedItem.Slug), expectedItem.Slug, actualItem.Slug, ignoreFormatting);
        }
    }

    private static void AddStringFieldDifference(ProfileComparisonResult result,
        int profileId,
        string fieldName,
        string expected,
        string actual,
        bool ignoreFormatting)
    {
        var equal = ignoreFormatting
            ? string.Equals(NormalizeFormatting(expected), NormalizeFormatting(actual), StringComparison.OrdinalIgnoreCase)
            : string.Equals(expected, actual, StringComparison.Ordinal);

        if (!equal)
            result.FieldDifferences.Add($"Profile {profileId}, {fieldName}: expected '{expected}', actual '{actual}'.");
    }

    private static void AddFieldDifference<T>(ProfileComparisonResult result,
        int profileId,
        string fieldName,
        T expected,
        T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            result.FieldDifferences.Add($"Profile {profileId}, {fieldName}: expected '{expected}', actual '{actual}'.");
    }

    private static void AddDifference<T>(IList<string> differences,
        string fieldName,
        T expected,
        T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            differences.Add($"{fieldName}: expected '{expected}', actual '{actual}'.");
    }

    private static string NormalizeFormatting(string value)
    {
        if (value == null)
            return null;

        return string.Join(",", value.Split(',')
            .Select(part => string.Join(" ", part.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))));
    }
}
