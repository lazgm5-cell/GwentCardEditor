using AbilityAnalyzer.Services;

namespace AbilityAnalyzer.Models;

public sealed class OrderMechanicsRecipe
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Card { get; set; } = "";
    public List<OrderMechanicOperationRecipe> Operations { get; set; } = new();
    public bool UpdateTooltip { get; set; } = true;
    public string Tooltip { get; set; } = "";
    public string OutputDirectoryName { get; set; } = "OrderMechanics.output";
}

public sealed class OrderMechanicOperationRecipe
{
    public string Type { get; set; } = "";

    // AddOrder / AddChargesToOrder / ConvertDeployToOrder
    public int Charges { get; set; } = 1;
    public bool VisibleCharges { get; set; }
    public bool Zeal { get; set; }

    // AddOrder: fixed choices which are created and immediately played.
    public List<string> Choices { get; set; } = new();

    // AddCooldownToOrder
    public int Cooldown { get; set; } = 1;
    public bool Resupply { get; set; }
    public int ResupplyAmount { get; set; } = 1;

    // Optional low-level selector for an existing Order effect's terminal node.
    // The node's FlowOut becomes the entry into the cooldown branch.
    public int? OrderTerminalNodeId { get; set; }
}


public sealed class OrderTerminalCandidate
{
    public int NodeId { get; init; }
    public int FlowOutId { get; init; }
    public string NodeType { get; init; } = "";
    public string Summary { get; init; } = "";
}

public sealed class OrderMechanicsPreview
{
    public CardRecord Card { get; init; } = new();
    public AbilityDocument Abilities { get; init; } = null!;
    public TemplateDocument Templates { get; init; } = null!;
    public GraphValidationReport GraphValidation { get; init; } = new();
    public string TooltipKey { get; init; } = "";
    public string TooltipValue { get; init; } = "";
    public List<string> Changes { get; } = new();
    public List<string> Warnings { get; } = new();
}

public sealed class OrderMechanicsResult
{
    public string OutputDirectory { get; init; } = "";
    public string AbilitiesFile { get; init; } = "";
    public string TemplatesFile { get; init; } = "";
    public string LocalizationFile { get; init; } = "";
    public string ReportFile { get; init; } = "";
    public OrderMechanicsPreview Preview { get; init; } = null!;
}
