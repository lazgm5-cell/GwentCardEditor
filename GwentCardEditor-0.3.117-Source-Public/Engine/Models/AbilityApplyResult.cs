namespace AbilityAnalyzer.Models;

/// <summary>
/// Result of applying one Ability mod-pack to an in-memory document.
/// </summary>
public sealed class AbilityApplyResult
{
    public List<CompositionResult> Compositions { get; } = new();
    public List<int> ChangedAbilityIds { get; } = new();
}
