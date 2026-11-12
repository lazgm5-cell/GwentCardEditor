namespace AbilityAnalyzer.Models;

/// <summary>
/// Canonical card category encoding used by Templates.xml.
/// Slot is the e0/e1/e2 index inside PrimaryCategory or Categories.
/// </summary>
public sealed class CategoryDefinition
{
    public string Name { get; init; } = "";
    public int Slot { get; init; }
    public ulong Mask { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
}
