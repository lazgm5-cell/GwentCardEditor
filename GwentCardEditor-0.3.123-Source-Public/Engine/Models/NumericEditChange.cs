namespace AbilityAnalyzer.Models;

public sealed class NumericEditChange
{
    public NumericValueDescriptor Target { get; set; } = new();
    public int NewValue { get; set; }
}
