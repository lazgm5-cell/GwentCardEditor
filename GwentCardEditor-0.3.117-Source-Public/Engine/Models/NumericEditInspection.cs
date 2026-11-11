namespace AbilityAnalyzer.Models;

public sealed class NumericEditInspection
{
    public CardRecord Card { get; set; } = new();
    public string AbilitiesPath { get; set; } = "";
    public string TemplatesPath { get; set; } = "";
    public string AbilitiesSha256 { get; set; } = "";
    public string TemplatesSha256 { get; set; } = "";
    public IReadOnlyList<NumericValueDescriptor> Values { get; set; } = Array.Empty<NumericValueDescriptor>();
}
