namespace AbilityAnalyzer.Models;

public sealed class ComposeAbilityLinkRecipe
{
    public string SourceFragment { get; set; } = "";
    public int SourceEndpointId { get; set; }
    public string TargetFragment { get; set; } = "";
    public int TargetEndpointId { get; set; }
    public string Description { get; set; } = "";
}
