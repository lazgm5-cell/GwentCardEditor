namespace AbilityAnalyzer.Models;

/// <summary>
/// Human-readable Ability recipe. The compiler translates these operations into
/// a low-level ComposeAbility recipe with verified source fragments.
/// </summary>
public sealed class SemanticAbilityRecipe
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string TargetCard { get; set; } = "";
    public string OutputFileName { get; set; } = "Abilities.semantic.xml";
    public List<SemanticAbilityActionRecipe> DeployActions { get; set; } = new();
    public List<SemanticAbilityTriggerRecipe> Triggers { get; set; } = new();
}
