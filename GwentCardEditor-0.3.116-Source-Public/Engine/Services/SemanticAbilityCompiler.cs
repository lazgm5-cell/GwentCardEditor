using AbilityAnalyzer.Models;

namespace AbilityAnalyzer.Services;

/// <summary>
/// Compiles a human-readable Ability recipe into the low-level ComposeAbility
/// format used by AbilityGraphComposer. Version 0.27.0 deliberately supports a
/// small verified vocabulary rather than guessing arbitrary graph structures.
/// </summary>
public static class SemanticAbilityCompiler
{
    private const int DeployTriggerSource = 152308;
    private const int CoralSource = 152107;
    private const int RandomEnemyRowSource = 201645;
    private const int LocationToListSource = 203069;
    private const int WeatherSource = 202879;

    private static readonly Dictionary<string, int> WeatherTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["frost"] = 1,
            ["fog"] = 2,
            ["rain"] = 4,
            ["cataclysm"] = 32
        };

    public static ModPackRecipe Compile(
        TemplateDocument templates,
        SemanticAbilityRecipe semantic)
    {
        if (semantic.FormatVersion != 1)
            throw new InvalidDataException(
                $"Неподдерживаемый semantic formatVersion={semantic.FormatVersion}.");

        CardRecord target = CardResolver.ResolveUnique(templates, semantic.TargetCard);
        ValidateRecipe(semantic);

        var operation = new ModPackOperationRecipe
        {
            Id = "semantic-build-" + target.Id,
            Type = "ComposeAbility",
            Description = semantic.Description,
            TargetCardAbilityId = target.Id,
            ClearTemporaryVariables = true,
            ClearPersistentVariables = true,
            StrictBoundaryValidation = true
        };

        int x = 0;
        operation.Fragments.Add(new ComposeAbilityFragmentRecipe
        {
            Name = "deploy-trigger",
            SourceCardAbilityId = DeployTriggerSource,
            NodeIds = new List<int> { 610 },
            XOffset = x,
            YOffset = 0
        });

        var deployChain = new List<SemanticFragmentPort>
        {
            new("deploy-trigger", null, 612)
        };

        for (int index = 0; index < semantic.DeployActions.Count; index++)
        {
            x += 450;
            SemanticFragmentPort port = AddAction(
                operation,
                semantic.DeployActions[index],
                $"deploy-{index + 1}",
                x,
                0);
            deployChain.Add(port);
        }
        LinkChain(operation, deployChain, "deploy");

        for (int triggerIndex = 0; triggerIndex < semantic.Triggers.Count; triggerIndex++)
        {
            SemanticAbilityTriggerRecipe trigger = semantic.Triggers[triggerIndex];
            string prefix = $"trigger-{triggerIndex + 1}";
            int y = 900 + triggerIndex * 900;

            if (!trigger.Type.Equals("OnDiscard", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Semantic trigger '{trigger.Type}' пока не поддерживается.");
            if (!trigger.OwnerTurnOnly || !trigger.PerCard)
            {
                throw new InvalidOperationException(
                    "OnDiscard в 0.27.0 поддерживает только ownerTurnOnly=true и perCard=true.");
            }

            operation.Fragments.Add(new ComposeAbilityFragmentRecipe
            {
                Name = prefix + "-on-discard",
                SourceCardAbilityId = CoralSource,
                NodeIds = new List<int> { 188, 201, 212, 224, 231, 237, 240 },
                XOffset = 0,
                YOffset = y
            });

            var triggerChain = new List<SemanticFragmentPort>
            {
                new(prefix + "-on-discard", null, 215)
            };

            int triggerX = 600;
            for (int actionIndex = 0; actionIndex < trigger.Actions.Count; actionIndex++)
            {
                SemanticFragmentPort port = AddAction(
                    operation,
                    trigger.Actions[actionIndex],
                    $"{prefix}-action-{actionIndex + 1}",
                    triggerX,
                    y);
                triggerChain.Add(port);
                triggerX += 700;
            }
            LinkChain(operation, triggerChain, prefix);
        }

        return new ModPackRecipe
        {
            FormatVersion = 1,
            Name = string.IsNullOrWhiteSpace(semantic.Name)
                ? $"Semantic Ability: {target.DebugName}"
                : semantic.Name,
            Description = semantic.Description,
            OutputFileName = string.IsNullOrWhiteSpace(semantic.OutputFileName)
                ? $"Abilities.semantic.{target.Id}.xml"
                : semantic.OutputFileName,
            Operations = new List<ModPackOperationRecipe> { operation }
        };
    }

    private static SemanticFragmentPort AddAction(
        ModPackOperationRecipe operation,
        SemanticAbilityActionRecipe action,
        string prefix,
        int x,
        int y)
    {
        switch (action.Type.Trim().ToLowerInvariant())
        {
            case "discardcardfromhand":
                return AddDiscard(operation, action, prefix, x, y);

            case "drawcards":
                return AddDraw(operation, action, prefix, x, y);

            case "applyweather":
                return AddWeather(operation, action, prefix, x, y);

            default:
                throw new InvalidOperationException(
                    $"Semantic action '{action.Type}' пока не поддерживается.");
        }
    }

    private static SemanticFragmentPort AddDiscard(
        ModPackOperationRecipe operation,
        SemanticAbilityActionRecipe action,
        string prefix,
        int x,
        int y)
    {
        if (action.Count < 1 || action.Count > 10)
            throw new InvalidOperationException("DiscardCardFromHand count должен быть 1–10.");

        string fragment = prefix + "-discard";
        operation.Fragments.Add(new ComposeAbilityFragmentRecipe
        {
            Name = fragment,
            SourceCardAbilityId = CoralSource,
            NodeIds = new List<int> { 7, 54, 65, 69, 75, 82, 291 },
            XOffset = x,
            YOffset = y
        });

        operation.Links.Add(new ComposeAbilityLinkRecipe
        {
            SourceFragment = fragment,
            SourceEndpointId = 8,
            TargetFragment = fragment,
            TargetEndpointId = 295,
            Description = "hand cards -> hand-not-empty branch"
        });
        operation.Links.Add(new ComposeAbilityLinkRecipe
        {
            SourceFragment = fragment,
            SourceEndpointId = 293,
            TargetFragment = fragment,
            TargetEndpointId = 55,
            Description = "hand exists -> request discard choice"
        });

        operation.FragmentPatches.Add(new FragmentPatchRecipe
        {
            Fragment = fragment,
            SourceNodeId = 54,
            FieldName = "MinChoices",
            Value = action.Count.ToString()
        });
        operation.FragmentPatches.Add(new FragmentPatchRecipe
        {
            Fragment = fragment,
            SourceNodeId = 54,
            FieldName = "MaxChoices",
            Value = action.Count.ToString()
        });

        return new SemanticFragmentPort(fragment, 292, 71);
    }

    private static SemanticFragmentPort AddDraw(
        ModPackOperationRecipe operation,
        SemanticAbilityActionRecipe action,
        string prefix,
        int x,
        int y)
    {
        if (action.Count < 1 || action.Count > 10)
            throw new InvalidOperationException("DrawCards count должен быть 1–10.");

        string fragment = prefix + "-draw";
        operation.Fragments.Add(new ComposeAbilityFragmentRecipe
        {
            Name = fragment,
            SourceCardAbilityId = CoralSource,
            NodeIds = new List<int> { 151, 161 },
            XOffset = x,
            YOffset = y
        });
        operation.FragmentPatches.Add(new FragmentPatchRecipe
        {
            Fragment = fragment,
            SourceNodeId = 151,
            FieldName = "Num",
            Value = action.Count.ToString()
        });
        return new SemanticFragmentPort(fragment, 152, 153);
    }

    private static SemanticFragmentPort AddWeather(
        ModPackOperationRecipe operation,
        SemanticAbilityActionRecipe action,
        string prefix,
        int x,
        int y)
    {
        if (!action.Target.Equals("randomEnemyRow", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ApplyWeather в 0.27.0 поддерживает только target='randomEnemyRow'.");
        }
        if (!WeatherTokens.TryGetValue(action.Weather.Trim(), out int token))
        {
            throw new InvalidOperationException(
                "Неизвестная погода. Поддерживаются: frost, fog, rain, cataclysm.");
        }
        if (action.Duration < 1 || action.Duration > 99)
            throw new InvalidOperationException("Weather duration должен быть 1–99.");

        string rows = prefix + "-random-enemy-row";
        string convert = prefix + "-location-to-list";
        string weather = prefix + "-weather";

        operation.Fragments.Add(new ComposeAbilityFragmentRecipe
        {
            Name = rows,
            SourceCardAbilityId = RandomEnemyRowSource,
            NodeIds = new List<int> { 199, 205, 304 },
            XOffset = x,
            YOffset = y
        });
        operation.Fragments.Add(new ComposeAbilityFragmentRecipe
        {
            Name = convert,
            SourceCardAbilityId = LocationToListSource,
            NodeIds = new List<int> { 238 },
            XOffset = x + 350,
            YOffset = y
        });
        operation.Fragments.Add(new ComposeAbilityFragmentRecipe
        {
            Name = weather,
            SourceCardAbilityId = WeatherSource,
            NodeIds = new List<int> { 92, 198 },
            XOffset = x + 650,
            YOffset = y
        });

        operation.Links.Add(new ComposeAbilityLinkRecipe
        {
            SourceFragment = rows,
            SourceEndpointId = 309,
            TargetFragment = convert,
            TargetEndpointId = 239,
            Description = "random Location -> LocationList"
        });
        operation.Links.Add(new ComposeAbilityLinkRecipe
        {
            SourceFragment = convert,
            SourceEndpointId = 240,
            TargetFragment = weather,
            TargetEndpointId = 96,
            Description = "random row list -> weather targets"
        });
        operation.Links.Add(new ComposeAbilityLinkRecipe
        {
            SourceFragment = rows,
            SourceEndpointId = 306,
            TargetFragment = weather,
            TargetEndpointId = 93,
            Description = "row selected -> apply weather"
        });

        operation.FragmentPatches.Add(new FragmentPatchRecipe
        {
            Fragment = weather,
            SourceNodeId = 92,
            FieldName = "Tokens",
            Value = token.ToString()
        });
        operation.FragmentPatches.Add(new FragmentPatchRecipe
        {
            Fragment = weather,
            SourceNodeId = 92,
            FieldName = "OverrideTime",
            Value = action.Duration.ToString()
        });

        return new SemanticFragmentPort(rows, 305, 94, weather);
    }

    private static void LinkChain(
        ModPackOperationRecipe operation,
        IReadOnlyList<SemanticFragmentPort> chain,
        string descriptionPrefix)
    {
        for (int index = 0; index < chain.Count - 1; index++)
        {
            SemanticFragmentPort current = chain[index];
            SemanticFragmentPort next = chain[index + 1];
            if (!current.ExitEndpoint.HasValue || !next.EntryEndpoint.HasValue)
                throw new InvalidOperationException("Semantic action chain has no flow port.");

            operation.Links.Add(new ComposeAbilityLinkRecipe
            {
                SourceFragment = current.ExitFragment,
                SourceEndpointId = current.ExitEndpoint.Value,
                TargetFragment = next.EntryFragment,
                TargetEndpointId = next.EntryEndpoint.Value,
                Description = $"{descriptionPrefix}: semantic action {index + 1} -> {index + 2}"
            });
        }
    }

    private static void ValidateRecipe(SemanticAbilityRecipe semantic)
    {
        if (string.IsNullOrWhiteSpace(semantic.TargetCard))
            throw new InvalidOperationException("Не указан targetCard.");
        if (semantic.DeployActions.Count == 0 && semantic.Triggers.Count == 0)
            throw new InvalidOperationException("Semantic recipe не содержит действий.");
        foreach (SemanticAbilityTriggerRecipe trigger in semantic.Triggers)
        {
            if (trigger.Actions.Count == 0)
                throw new InvalidOperationException($"Trigger '{trigger.Type}' не содержит actions.");
        }
    }

    private sealed record SemanticFragmentPort(
        string EntryFragment,
        int? EntryEndpoint,
        int? ExitEndpoint,
        string? ExplicitExitFragment = null)
    {
        public string ExitFragment => ExplicitExitFragment ?? EntryFragment;
    }
}
