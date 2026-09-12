using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Nop.Plugin.Widgets.AISearch.Services;

public partial class ProductContentBuilder : IProductContentBuilder
{
    private readonly IProductTagService _productTagService;
    private readonly ISpecificationAttributeService _specificationAttributeService;

    public ProductContentBuilder(IProductTagService productTagService,
        ISpecificationAttributeService specificationAttributeService)
    {
        _productTagService = productTagService;
        _specificationAttributeService = specificationAttributeService;
    }

    public async Task<ProductContent> BuildContentAsync(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var parts = new List<string>
        {
            Clean(product.Name),
            Clean(product.ShortDescription),
            Clean(product.FullDescription)
        };

        var mappings = await _specificationAttributeService.GetProductSpecificationAttributesAsync(product.Id);
        foreach (var mapping in mappings)
        {
            var option = await _specificationAttributeService.GetSpecificationAttributeOptionByIdAsync(mapping.SpecificationAttributeOptionId);
            var attribute = option == null
                ? null
                : await _specificationAttributeService.GetSpecificationAttributeByIdAsync(option.SpecificationAttributeId);
            var value = string.IsNullOrWhiteSpace(mapping.CustomValue) ? option?.Name : mapping.CustomValue;

            if (attribute != null && !string.IsNullOrWhiteSpace(value))
                parts.Add($"{Clean(attribute.Name)}: {Clean(value)}");
        }

        var tags = await _productTagService.GetAllProductTagsByProductIdAsync(product.Id);
        if (tags.Any())
            parts.Add($"Tags: {string.Join(", ", tags.Select(tag => Clean(tag.Name)).Where(name => name.Length > 0))}");

        var sourceText = string.Join(Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceText)));
        return new ProductContent(sourceText, hash);
    }

    private static string Clean(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var withoutTags = HtmlTagRegex().Replace(value, " ");
        return WhitespaceRegex().Replace(System.Net.WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
