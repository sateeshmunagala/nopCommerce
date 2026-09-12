using Nop.Core.Domain.Catalog;

namespace Nop.Plugin.Widgets.AISearch.Services;

public record ProductContent(string SourceText, string ContentHash);

public interface IProductContentBuilder
{
    Task<ProductContent> BuildContentAsync(Product product);
}
