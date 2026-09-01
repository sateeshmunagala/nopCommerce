using System.Text;
using LinqToDB;
using LinqToDB.Data;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Plugin.Misc.JobSupport.Domain;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.JobSupport.Services.Migration;

public partial class JobSupportReconciliationService : IJobSupportReconciliationService
{
    private const int MAX_EXPORTED_IDENTIFIERS = 1000;

    private readonly INopDataProvider _dataProvider;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderItem> _orderItemRepository;
    private readonly IRepository<ShoppingCartItem> _shoppingCartItemRepository;
    private readonly IRepository<JobSupportContactReveal> _contactRevealRepository;
    private readonly IRepository<JobSupportMigrationCheckpoint> _checkpointRepository;
    private readonly IRepository<JobSupportProfile> _profileRepository;
    private readonly IRepository<JobSupportProfileView> _profileViewRepository;
    private readonly IRepository<JobSupportRelationship> _relationshipRepository;
    private readonly IRepository<JobSupportSubscription> _subscriptionRepository;
    private readonly ILogger _logger;
    private readonly JobSupportSettings _settings;

    public JobSupportReconciliationService(INopDataProvider dataProvider,
        IRepository<Order> orderRepository,
        IRepository<OrderItem> orderItemRepository,
        IRepository<ShoppingCartItem> shoppingCartItemRepository,
        IRepository<JobSupportContactReveal> contactRevealRepository,
        IRepository<JobSupportMigrationCheckpoint> checkpointRepository,
        IRepository<JobSupportProfile> profileRepository,
        IRepository<JobSupportProfileView> profileViewRepository,
        IRepository<JobSupportRelationship> relationshipRepository,
        IRepository<JobSupportSubscription> subscriptionRepository,
        ILogger logger,
        JobSupportSettings settings)
    {
        _dataProvider = dataProvider;
        _orderRepository = orderRepository;
        _orderItemRepository = orderItemRepository;
        _shoppingCartItemRepository = shoppingCartItemRepository;
        _contactRevealRepository = contactRevealRepository;
        _checkpointRepository = checkpointRepository;
        _profileRepository = profileRepository;
        _profileViewRepository = profileViewRepository;
        _relationshipRepository = relationshipRepository;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
        _settings = settings;
    }

    public async Task<IReadOnlyList<JobSupportMigrationCheckpoint>> GetCheckpointsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _checkpointRepository.Table.OrderBy(checkpoint => checkpoint.Id).Take(100).ToListAsync();
    }

    public async Task<ReconciliationResult> ReconcileAsync(CancellationToken cancellationToken)
    {
        var identifiers = new List<string>();
        cancellationToken.ThrowIfCancellationRequested();
        var customerTable = NameCompatibilityManager.GetTableName(typeof(Customer));
        var profileTable = NameCompatibilityManager.GetTableName(typeof(JobSupportProfile));
        var countRows = await _dataProvider.QueryAsync<LegacyCountSource>(
            $"SELECT COUNT_BIG(*) AS [Count] FROM [{customerTable}] WHERE [Deleted] = 0 AND [CustomerProfileTypeId] > 0");
        var legacyProfileCount = countRows.FirstOrDefault()?.Count ?? 0;
        var pluginProfileCount = await _profileRepository.Table.CountAsync();
        var missingProfileIds = await _dataProvider.QueryAsync<LegacyIdentifierSource>(
            $"SELECT TOP (@Limit) customer.[Id] FROM [{customerTable}] customer LEFT JOIN [{profileTable}] profile ON profile.[CustomerId] = customer.[Id] WHERE customer.[Deleted] = 0 AND customer.[CustomerProfileTypeId] > 0 AND profile.[Id] IS NULL ORDER BY customer.[Id]",
            new DataParameter("Limit", MAX_EXPORTED_IDENTIFIERS, LinqToDB.DataType.Int32));
        identifiers.AddRange(missingProfileIds.Select(row => $"Profiles:{row.Id}"));
        AddCountMismatch(identifiers, "Profiles", legacyProfileCount, pluginProfileCount);

        var legacyRelationshipCount = await _shoppingCartItemRepository.Table.CountAsync(item => item.ShoppingCartTypeId >= 2 && item.ShoppingCartTypeId <= 11);
        var pluginRelationshipCount = await _relationshipRepository.Table.CountAsync();
        AddCountMismatch(identifiers, "Relationships", legacyRelationshipCount, pluginRelationshipCount);

        var legacyViewCount = await _shoppingCartItemRepository.Table.CountAsync(item => item.ShoppingCartTypeId >= 12 && item.ShoppingCartTypeId <= 13);
        var pluginViewCount = await _profileViewRepository.Table.CountAsync();
        AddCountMismatch(identifiers, "Views", legacyViewCount, pluginViewCount);

        var planProductIds = new[]
        {
            _settings.ThreeMonthSubscriptionProductId,
            _settings.SixMonthSubscriptionProductId,
            _settings.OneYearSubscriptionProductId
        }.Where(id => id > 0).Distinct().ToArray();
        if (planProductIds.Length > 0)
        {
            var paidOrderIds = _orderRepository.Table.Where(order => !order.Deleted && order.PaidDateUtc != null).Select(order => order.Id);
            var legacySubscriptionCount = await _orderItemRepository.Table.CountAsync(item =>
                planProductIds.Contains(item.ProductId) && paidOrderIds.Contains(item.OrderId));
            var pluginSubscriptionCount = await _subscriptionRepository.Table.CountAsync();
            AddCountMismatch(identifiers, "Subscriptions", legacySubscriptionCount, pluginSubscriptionCount);
            var missingOrderItemIds = await _orderItemRepository.Table
                .Where(item => planProductIds.Contains(item.ProductId) && paidOrderIds.Contains(item.OrderId) &&
                    !_subscriptionRepository.Table.Any(subscription => subscription.OrderItemId == item.Id))
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .Take(Math.Max(0, MAX_EXPORTED_IDENTIFIERS - identifiers.Count))
                .ToListAsync();
            identifiers.AddRange(missingOrderItemIds.Select(id => $"Subscriptions:{id}"));
        }

        var duplicateRevealCount = await _contactRevealRepository.Table
            .GroupBy(reveal => new { reveal.ViewerCustomerId, reveal.TargetProfileId })
            .Where(group => group.Count() > 1)
            .CountAsync();
        if (duplicateRevealCount > 0)
            identifiers.Add($"Reveals:duplicates:{duplicateRevealCount}");

        identifiers = identifiers.Distinct(StringComparer.Ordinal).Take(MAX_EXPORTED_IDENTIFIERS).ToList();
        var executedOnUtc = DateTime.UtcNow;
        await SaveMismatchCountsAsync(identifiers, executedOnUtc, cancellationToken);
        foreach (var identifier in identifiers)
            await _logger.WarningAsync($"JobSupport reconciliation mismatch {identifier}.");
        return new ReconciliationResult
        {
            ExecutedOnUtc = executedOnUtc,
            MismatchCount = identifiers.Count,
            SanitizedMismatchIdentifiers = identifiers
        };
    }

    public async Task<string> ExportSanitizedMismatchesAsync(CancellationToken cancellationToken)
    {
        var checkpoints = await GetCheckpointsAsync(cancellationToken);
        var identifiers = checkpoints.SelectMany(checkpoint => (checkpoint.ErrorLog ?? string.Empty)
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            .Where(line => line.StartsWith("mismatch:", StringComparison.Ordinal))
            .Select(line => line["mismatch:".Length..])
            .Distinct(StringComparer.Ordinal)
            .Take(MAX_EXPORTED_IDENTIFIERS)
            .ToList();
        var csv = new StringBuilder("Step,Identifier");
        foreach (var identifier in identifiers)
        {
            var separator = identifier.IndexOf(':');
            var step = separator < 0 ? identifier : identifier[..separator];
            var value = separator < 0 ? string.Empty : identifier[(separator + 1)..];
            csv.AppendLine().Append(Escape(step)).Append(',').Append(Escape(value));
        }
        return csv.ToString();
    }

    private async Task SaveMismatchCountsAsync(IList<string> identifiers, DateTime executedOnUtc, CancellationToken cancellationToken)
    {
        var checkpoints = await GetCheckpointsAsync(cancellationToken);
        foreach (var checkpoint in checkpoints)
        {
            var stepIdentifiers = identifiers.Where(identifier => BelongsTo(checkpoint.MigrationName, identifier)).ToList();
            checkpoint.MismatchCount = stepIdentifiers.Count;
            var retained = (checkpoint.ErrorLog ?? string.Empty).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.StartsWith("mismatch:", StringComparison.Ordinal));
            checkpoint.ErrorLog = string.Join(Environment.NewLine,
                retained.Concat(stepIdentifiers.Select(identifier => $"mismatch:{identifier}")).TakeLast(100));
            checkpoint.LastExecutedOnUtc = executedOnUtc;
            checkpoint.UpdatedOnUtc = executedOnUtc;
        }
        if (checkpoints.Count > 0)
            await _checkpointRepository.UpdateAsync(checkpoints.ToList(), false);
    }

    private static void AddCountMismatch(ICollection<string> identifiers, string step, long legacyCount, long pluginCount)
    {
        if (legacyCount != pluginCount)
            identifiers.Add($"{step}:count:{legacyCount}:{pluginCount}");
    }

    private static bool BelongsTo(string migrationName, string identifier) => migrationName switch
    {
        "SkillsAndAttributes" => identifier.StartsWith("Skills:", StringComparison.Ordinal),
        "ViewsAndReveals" => identifier.StartsWith("Views:", StringComparison.Ordinal) || identifier.StartsWith("Reveals:", StringComparison.Ordinal),
        _ => identifier.StartsWith($"{migrationName}:", StringComparison.Ordinal)
    };

    private static string Escape(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    // Compatibility source projections retained for rollback migration; remove in JobSupport 2.0.0.
    private sealed class LegacyCountSource
    {
        public long Count { get; set; }
    }

    private sealed class LegacyIdentifierSource
    {
        public int Id { get; set; }
    }
}
