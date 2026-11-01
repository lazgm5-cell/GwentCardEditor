namespace AbilityAnalyzer.Models;

/// <summary>
/// High-level action used by the semantic Ability builder.
/// Supported in 0.27.0: DiscardCardFromHand, DrawCards and ApplyWeather.
/// </summary>
public sealed class SemanticAbilityActionRecipe
{
    public string Type { get; set; } = "";
    public int Count { get; set; } = 1;
    public string Weather { get; set; } = "cataclysm";
    public string Target { get; set; } = "randomEnemyRow";
    public int Duration { get; set; } = 1;
}
