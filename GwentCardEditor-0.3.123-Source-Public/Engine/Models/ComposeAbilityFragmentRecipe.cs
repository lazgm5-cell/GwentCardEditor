namespace AbilityAnalyzer.Models;

public sealed class ComposeAbilityFragmentRecipe
{
    public string Name { get; set; } = "";
    public int SourceCardAbilityId { get; set; }
    public List<int> NodeIds { get; set; } = new();
    public Dictionary<int, int> ExternalVariableMap { get; set; } = new();
    public List<int> AllowDroppedIncomingEndpointIds { get; set; } = new();
    public int XOffset { get; set; }
    public int YOffset { get; set; }
}
