using FluentMigrator;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/07/29 13:00:00", "Misc.AIInterview make mock practice skill mappings optional", MigrationProcessType.Update)]
public class MockPracticeSkillMappingOptionalMigration : Migration
{
    private static readonly string[] SkillLabels =
    [
        "practice skill",
        "skill",
        "skills",
        "interview skill"
    ];

    private readonly INopDataProvider _dataProvider;

    public MockPracticeSkillMappingOptionalMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        var templateIds = _dataProvider.GetTable<ProductTemplate>()
            .Where(template =>
                template.ViewPath == AIInterviewDefaults.MockPracticeProductTemplateViewPath ||
                template.Name == AIInterviewDefaults.MockPracticeProductTemplateName)
            .Select(template => template.Id)
            .ToList();

        if (templateIds.Count == 0)
            return;

        var productIds = _dataProvider.GetTable<Product>()
            .Where(product => templateIds.Contains(product.ProductTemplateId))
            .Select(product => product.Id)
            .ToList();
        if (productIds.Count == 0)
            return;

        var requiredMappings = _dataProvider.GetTable<ProductAttributeMapping>()
            .Where(mapping => productIds.Contains(mapping.ProductId) && mapping.IsRequired)
            .ToList();
        if (requiredMappings.Count == 0)
            return;

        var attributeIds = requiredMappings
            .Select(mapping => mapping.ProductAttributeId)
            .Distinct()
            .ToList();
        var attributeNames = _dataProvider.GetTable<ProductAttribute>()
            .Where(attribute => attributeIds.Contains(attribute.Id))
            .ToDictionary(attribute => attribute.Id, attribute => attribute.Name);

        foreach (var mapping in requiredMappings)
        {
            attributeNames.TryGetValue(mapping.ProductAttributeId, out var attributeName);
            if (!IsSkillLabel(attributeName) && !IsSkillLabel(mapping.TextPrompt))
                continue;

            mapping.IsRequired = false;
            _dataProvider.UpdateEntity(mapping);
        }
    }

    public override void Down()
    {
    }

    private static bool IsSkillLabel(string value)
    {
        var normalizedValue = NormalizeAttributeLabel(value);
        return !string.IsNullOrWhiteSpace(normalizedValue) &&
            SkillLabels.Any(label => normalizedValue.Contains(label, StringComparison.Ordinal));
    }

    private static string NormalizeAttributeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = new string(value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ')
            .ToArray());

        return string.Join(" ", sanitized
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }
}
