namespace AbilityAnalyzer.Models;

public sealed class ModPackRecipe
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string OutputFileName { get; set; } = "Abilities.modpack.xml";
    public List<ModPackOperationRecipe> Operations { get; set; } = new();
}
