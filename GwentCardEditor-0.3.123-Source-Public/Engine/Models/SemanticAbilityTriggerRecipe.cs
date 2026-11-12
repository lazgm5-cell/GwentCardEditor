namespace AbilityAnalyzer.Models;

/// <summary>
/// High-level trigger used by the semantic Ability builder.
/// Supported in 0.27.0: OnDiscard during the owner's turn, once per discarded card.
/// </summary>
public sealed class SemanticAbilityTriggerRecipe
{
    public string Type { get; set; } = "";
    public bool OwnerTurnOnly { get; set; } = true;
    public bool PerCard { get; set; } = true;
    public List<SemanticAbilityActionRecipe> Actions { get; set; } = new();
}
