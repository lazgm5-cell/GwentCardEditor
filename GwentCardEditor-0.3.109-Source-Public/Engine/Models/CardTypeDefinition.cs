namespace AbilityAnalyzer.Models;

public sealed class CardTypeDefinition
{
    public string Name { get; init; } = "";
    public int Value { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
}
