namespace Nop.Plugin.Widgets.AISearch.Models;

public class SearchResultModel
{
    public string Message { get; set; }
    public bool HasResults { get; set; }
    public List<ProductCardModel> Products { get; set; } = new();
}
