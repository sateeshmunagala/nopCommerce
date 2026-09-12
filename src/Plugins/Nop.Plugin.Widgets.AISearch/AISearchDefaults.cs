using Nop.Core.Caching;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Widgets.AISearch;

public static class AISearchDefaults
{
    public const string SystemName = "Widgets.AISearch";
    public const string ConfigurationRouteName = "AISearch.Configure";
    public const string ReindexRouteName = "AISearch.ReindexNow";
    public const string PublicSearchRouteName = "AISearch.PublicSearch";
    public static string WidgetZone => PublicWidgetZones.Footer;
    public const string ScheduleTaskName = "AI Search: sync product embeddings";
    public const string ScheduleTaskType = "Nop.Plugin.Widgets.AISearch.Services.ProductEmbeddingSyncTask";

    public static CacheKey ProductEmbeddingByProductStoreCacheKey =>
        new("Nop.Plugin.Widgets.AISearch.embedding.{0}-{1}");
}
