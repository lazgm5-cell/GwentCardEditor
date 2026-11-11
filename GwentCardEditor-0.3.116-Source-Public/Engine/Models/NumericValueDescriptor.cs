namespace AbilityAnalyzer.Models;

public sealed class NumericValueDescriptor
{
    public int Index { get; set; }
    public string Key { get; set; } = "";
    public string Role { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Source { get; set; } = "";
    public string Path { get; set; } = "";
    public int CurrentValue { get; set; }
    public int MinimumValue { get; set; } = -999999;
    public int MaximumValue { get; set; } = 999999;
    public bool Editable { get; set; }
    public string? ReadOnlyReason { get; set; }
    public string TargetKind { get; set; } = "";
    public string? TemplateField { get; set; }
    public int? VariableId { get; set; }
    public int? NodeId { get; set; }
    public string? NodeType { get; set; }
    public string? FieldName { get; set; }
}
