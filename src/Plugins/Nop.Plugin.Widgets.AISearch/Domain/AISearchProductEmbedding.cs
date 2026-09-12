using Nop.Core;

namespace Nop.Plugin.Widgets.AISearch.Domain;

public class AISearchProductEmbedding : BaseEntity
{
    public int ProductId { get; set; }
    public int StoreId { get; set; }
    public string ContentHash { get; set; }
    public string SourceText { get; set; }
    public bool Published { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
