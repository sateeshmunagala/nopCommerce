using Nop.Data;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class FixedQuestionSetService : IFixedQuestionSetService
{
    private const int MaxQuestionCount = 10;
    private readonly IRepository<FixedQuestionSet> _questionSetRepository;
    private readonly IRepository<FixedQuestionSetItem> _questionItemRepository;
    private readonly INopDataProvider _dataProvider;

    public FixedQuestionSetService(IRepository<FixedQuestionSet> questionSetRepository,
        IRepository<FixedQuestionSetItem> questionItemRepository,
        INopDataProvider dataProvider)
    {
        _questionSetRepository = questionSetRepository;
        _questionItemRepository = questionItemRepository;
        _dataProvider = dataProvider;
    }

    public async Task<IList<FixedQuestionSetDetails>> GetAllAsync(int vendorId, bool includeInactive = false)
    {
        EnsureVendorId(vendorId);
        var sets = await _questionSetRepository.GetAllAsync(query => query
            .Where(set => set.VendorId == vendorId && (includeInactive || set.IsActive))
            .OrderBy(set => set.Name)
            .ThenBy(set => set.Id));

        var results = new List<FixedQuestionSetDetails>();
        foreach (var set in sets)
            results.Add(new FixedQuestionSetDetails(set, await GetAuthorizedItemsAsync(vendorId, set.Id, includeInactive)));

        return results;
    }

    public async Task<FixedQuestionSetDetails> GetByIdAsync(int vendorId, int questionSetId, bool includeInactive = false)
    {
        var set = await GetAuthorizedSetAsync(vendorId, questionSetId, includeInactive);
        return set == null ? null : new FixedQuestionSetDetails(set, await GetAuthorizedItemsAsync(vendorId, set.Id, includeInactive));
    }

    public async Task<FixedQuestionSetDetails> CreateAsync(int vendorId, string name, IList<FixedQuestionSetItem> items)
    {
        EnsureVendorId(vendorId);
        var normalizedItems = NormalizeItems(items);
        var now = DateTime.UtcNow;
        using var transaction = _dataProvider.CreateTransactionScope();
        var set = new FixedQuestionSet
        {
            VendorId = vendorId,
            Name = NormalizeName(name),
            IsActive = true,
            CreatedOnUtc = now,
            UpdatedOnUtc = now
        };
        await _questionSetRepository.InsertAsync(set);

        foreach (var item in normalizedItems)
        {
            item.FixedQuestionSetId = set.Id;
            item.CreatedOnUtc = now;
            item.UpdatedOnUtc = now;
        }
        await _questionItemRepository.InsertAsync(normalizedItems);
        transaction.Complete();
        return new FixedQuestionSetDetails(set, normalizedItems);
    }

    public async Task<FixedQuestionSetDetails> UpdateAsync(int vendorId, int questionSetId, string name, IList<FixedQuestionSetItem> items)
    {
        var set = await GetAuthorizedSetAsync(vendorId, questionSetId, true)
            ?? throw new InvalidOperationException("Question set was not found for this vendor.");
        var normalizedItems = NormalizeItems(items);
        var existingItems = await GetAuthorizedItemsAsync(vendorId, set.Id, true);
        var now = DateTime.UtcNow;
        using var transaction = _dataProvider.CreateTransactionScope();

        set.Name = NormalizeName(name);
        set.IsActive = true;
        set.UpdatedOnUtc = now;
        await _questionSetRepository.UpdateAsync(set);

        if (existingItems.Any())
            await _questionItemRepository.DeleteAsync(existingItems);

        foreach (var item in normalizedItems)
        {
            item.Id = 0;
            item.FixedQuestionSetId = set.Id;
            item.CreatedOnUtc = now;
            item.UpdatedOnUtc = now;
        }
        await _questionItemRepository.InsertAsync(normalizedItems);
        transaction.Complete();
        return new FixedQuestionSetDetails(set, normalizedItems);
    }

    public async Task DeleteAsync(int vendorId, int questionSetId)
    {
        var set = await GetAuthorizedSetAsync(vendorId, questionSetId, true)
            ?? throw new InvalidOperationException("Question set was not found for this vendor.");
        set.IsActive = false;
        set.UpdatedOnUtc = DateTime.UtcNow;
        await _questionSetRepository.UpdateAsync(set);
    }

    public async Task<FixedQuestionSetDetails> CloneAsync(int vendorId, int questionSetId, string name = null)
    {
        var source = await GetByIdAsync(vendorId, questionSetId)
            ?? throw new InvalidOperationException("Question set was not found for this vendor.");
        var cloneName = string.IsNullOrWhiteSpace(name) ? $"{source.QuestionSet.Name} - Copy" : name;
        return await CreateAsync(vendorId, cloneName, source.Items);
    }

    public async Task ReorderAsync(int vendorId, int questionSetId, IReadOnlyDictionary<int, int> itemSequences)
    {
        var set = await GetAuthorizedSetAsync(vendorId, questionSetId)
            ?? throw new InvalidOperationException("Question set was not found for this vendor.");
        var items = await GetAuthorizedItemsAsync(vendorId, set.Id, true);
        if (itemSequences == null || itemSequences.Count != items.Count ||
            items.Any(item => !itemSequences.ContainsKey(item.Id)))
        {
            throw new ArgumentException("A normalized sequence is required for every question item.", nameof(itemSequences));
        }

        var normalizedSequences = itemSequences.Values.OrderBy(value => value).ToList();
        if (!normalizedSequences.SequenceEqual(Enumerable.Range(1, items.Count)))
            throw new ArgumentException("Question item sequences must be contiguous and start at one.", nameof(itemSequences));

        var now = DateTime.UtcNow;
        using var transaction = _dataProvider.CreateTransactionScope();
        foreach (var item in items)
        {
            item.SequenceNumber = 1000000 + item.Id;
            item.UpdatedOnUtc = now;
        }
        await _questionItemRepository.UpdateAsync(items);

        foreach (var item in items)
            item.SequenceNumber = itemSequences[item.Id];
        await _questionItemRepository.UpdateAsync(items);
        transaction.Complete();
    }

    protected virtual async Task<FixedQuestionSet> GetAuthorizedSetAsync(int vendorId, int questionSetId, bool includeInactive = false)
    {
        EnsureVendorId(vendorId);
        if (questionSetId <= 0)
            return null;

        return (await _questionSetRepository.GetAllAsync(query => query.Where(set =>
            set.Id == questionSetId && set.VendorId == vendorId && (includeInactive || set.IsActive)))).FirstOrDefault();
    }

    protected virtual async Task<IList<FixedQuestionSetItem>> GetAuthorizedItemsAsync(int vendorId, int questionSetId, bool includeInactive)
    {
        var authorizedSet = await GetAuthorizedSetAsync(vendorId, questionSetId, includeInactive);
        if (authorizedSet == null)
            return new List<FixedQuestionSetItem>();

        return await _questionItemRepository.GetAllAsync(query => query
            .Where(item => item.FixedQuestionSetId == authorizedSet.Id && (includeInactive || item.IsActive))
            .OrderBy(item => item.SequenceNumber)
            .ThenBy(item => item.Id));
    }

    protected virtual IList<FixedQuestionSetItem> NormalizeItems(IEnumerable<FixedQuestionSetItem> items)
    {
        var normalized = (items ?? Enumerable.Empty<FixedQuestionSetItem>())
            .Where(item => item != null && item.IsActive && !string.IsNullOrWhiteSpace(item.QuestionText))
            .OrderBy(item => item.SequenceNumber > 0 ? item.SequenceNumber : int.MaxValue)
            .ThenBy(item => item.Id)
            .Take(MaxQuestionCount)
            .Select((item, index) => new FixedQuestionSetItem
            {
                SequenceNumber = index + 1,
                QuestionText = Truncate(item.QuestionText.Trim(), 2000),
                RubricHint = Truncate(item.RubricHint?.Trim(), 2000),
                ExpectedSignalNotes = Truncate(item.ExpectedSignalNotes?.Trim(), 2000),
                IsActive = true
            })
            .ToList();

        if (!normalized.Any())
            throw new ArgumentException("At least one active question is required.", nameof(items));

        return normalized;
    }

    protected virtual string NormalizeName(string name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Question set name is required.", nameof(name));
        return normalized.Length <= 300 ? normalized : normalized[..300];
    }

    protected static void EnsureVendorId(int vendorId)
    {
        if (vendorId <= 0)
            throw new ArgumentOutOfRangeException(nameof(vendorId));
    }

    protected static string Truncate(string value, int maxLength)
    {
        return string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
    }
}
