namespace AbilityAnalyzer.Models;

public sealed class SemanticCardPackRecipe
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string OutputDirectoryName { get; set; } = "SemanticCardPack.output";
    public List<SemanticCardPackOperationRecipe> Operations { get; set; } = new();
}

public sealed class SemanticCardPackOperationRecipe
{
    public string Type { get; set; } = "";
    public string Card { get; set; } = "";
    public int Amount { get; set; }
    public int PowerMax { get; set; }
    public int Duration { get; set; }
    public int DeployAmount { get; set; }
    public int OrderAmount { get; set; }
    public int WitcherInfusionBoost { get; set; }
    public int MinTargets { get; set; } = 1;
    public int MaxTargets { get; set; } = 2;
    public int Damage { get; set; } = 1;
}

public sealed class SemanticCardPackPreview
{
    public AbilityAnalyzer.Services.AbilityDocument Abilities { get; init; } = null!;
    public AbilityAnalyzer.Services.TemplateDocument Templates { get; init; } = null!;
    public Dictionary<string, string> LocalizationEdits { get; init; } = new(StringComparer.Ordinal);
    public List<int> ChangedCardIds { get; } = new();
    public List<string> Changes { get; } = new();
    public List<string> Warnings { get; } = new();
    public Dictionary<int, AbilityAnalyzer.Models.GraphValidationReport> Validation { get; } = new();
    public int? AddedInjectableAbilityId { get; set; }
}

public sealed class SemanticCardPackResult
{
    public string OutputDirectory { get; init; } = "";
    public string AbilitiesFile { get; init; } = "";
    public string TemplatesFile { get; init; } = "";
    public string LocalizationFile { get; init; } = "";
    public string ReportFile { get; init; } = "";
    public SemanticCardPackPreview Preview { get; init; } = null!;
}
