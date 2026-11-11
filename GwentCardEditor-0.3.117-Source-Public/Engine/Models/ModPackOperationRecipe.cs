namespace AbilityAnalyzer.Models;

public sealed class ModPackOperationRecipe
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public int TargetCardAbilityId { get; set; }
    public bool ClearTemporaryVariables { get; set; }
    public bool ClearPersistentVariables { get; set; }
    public bool StrictBoundaryValidation { get; set; } = true;

    public List<ComposeAbilityFragmentRecipe> Fragments { get; set; } = new();
    public List<ComposeAbilityLinkRecipe> Links { get; set; } = new();
    public List<FragmentPatchRecipe> FragmentPatches { get; set; } = new();

    // Generic field patch operation.
    public int? NodeId { get; set; }
    public string NodeType { get; set; } = "";
    public int? NodeOccurrence { get; set; }
    public string FieldName { get; set; } = "";
    public string AttributeName { get; set; } = "V";
    public string Value { get; set; } = "";
}
