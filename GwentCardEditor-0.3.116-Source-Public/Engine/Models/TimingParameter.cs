namespace AbilityAnalyzer.Models;

public sealed class TimingParameter
{
    public int Index { get; set; }
    public int VariableId { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public int Value { get; set; }
    public bool HasIncomingConnection { get; set; }
}
