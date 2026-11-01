using AbilityAnalyzer.Models;
using System.Text;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public static class OrderMechanicsService
{
    private const int ChoicePlaySource = 202220;       // Scepter of Storms
    private const int CooldownSource = 122302;         // Reinforced Ballista

    public static IReadOnlyList<OrderTerminalCandidate> FindOrderTerminalCandidates(
        string abilitiesPath,
        int cardId)
    {
        XElement ability = AbilityDocument.Load(abilitiesPath).FindRequiredCardAbility(cardId);
        return FindOrderTerminalCandidates(ability);
    }

    public static IReadOnlyList<OrderTerminalCandidate> FindOrderTerminalCandidates(XElement ability)
    {
        XElement nodes = ability.Element("Nodes")
            ?? throw new InvalidDataException("Ability не содержит Nodes.");
        XElement connections = ability.Element("Connections")
            ?? throw new InvalidDataException("Ability не содержит Connections.");

        var nodeById = nodes.Elements()
            .Where(node => node.Attribute("Id") != null)
            .ToDictionary(node => (int)node.Attribute("Id")!);
        var endpointOwner = new Dictionary<int, int>();
        foreach (XElement node in nodes.Elements())
        {
            int? nodeId = (int?)node.Attribute("Id");
            if (!nodeId.HasValue)
                continue;
            foreach (XElement endpoint in node.DescendantsAndSelf()
                         .Where(element => element.Attribute("Id") != null))
                endpointOwner[(int)endpoint.Attribute("Id")!] = nodeId.Value;
        }

        var edges = new Dictionary<int, HashSet<int>>();
        var usedSourceEndpoints = new HashSet<int>();
        foreach (XElement connection in connections.Elements())
        {
            int? source = (int?)connection.Attribute("SrcId");
            int? target = (int?)connection.Attribute("DstId");
            if (!source.HasValue || !target.HasValue)
                continue;
            usedSourceEndpoints.Add(source.Value);
            if (!endpointOwner.TryGetValue(source.Value, out int sourceNode) ||
                !endpointOwner.TryGetValue(target.Value, out int targetNode))
                continue;
            if (!edges.TryGetValue(sourceNode, out HashSet<int>? targets))
            {
                targets = new HashSet<int>();
                edges[sourceNode] = targets;
            }
            targets.Add(targetNode);
        }

        List<int> starts = nodes.Elements()
            .Where(node => string.Equals(
                (string?)node.Attribute("Type"), "TappedTrigger", StringComparison.Ordinal))
            .Select(node => (int)node.Attribute("Id")!)
            .ToList();
        if (starts.Count == 0)
            return Array.Empty<OrderTerminalCandidate>();

        var reachable = new HashSet<int>(starts);
        var queue = new Queue<int>(starts);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (!edges.TryGetValue(current, out HashSet<int>? targets))
                continue;
            foreach (int target in targets)
            {
                if (reachable.Add(target))
                    queue.Enqueue(target);
            }
        }

        var result = new List<OrderTerminalCandidate>();
        foreach (int nodeId in reachable.OrderBy(value => value))
        {
            if (!nodeById.TryGetValue(nodeId, out XElement? node))
                continue;
            string nodeType = (string?)node.Attribute("Type") ?? "";
            if (nodeType.EndsWith("Trigger", StringComparison.Ordinal))
                continue;

            XElement? flowOut = node.Element("FlowOut");
            int? flowOutId = (int?)flowOut?.Attribute("Id");
            if (!flowOutId.HasValue || usedSourceEndpoints.Contains(flowOutId.Value))
                continue;

            result.Add(new OrderTerminalCandidate
            {
                NodeId = nodeId,
                FlowOutId = flowOutId.Value,
                NodeType = nodeType,
                Summary = DescribeOrderNode(node)
            });
        }
        return result;
    }

    public static OrderMechanicsPreview BuildPreview(
        string abilitiesPath,
        string templatesPath,
        string localizationPath,
        OrderMechanicsRecipe recipe)
    {
        ValidateRecipe(recipe);
        TemplateDocument sourceTemplates = TemplateDocument.Load(templatesPath);
        CardRecord card = CardResolver.ResolveUnique(sourceTemplates, recipe.Card);
        TemplateDocument templates = sourceTemplates.DeepClone();
        AbilityDocument abilities = AbilityDocument.Load(abilitiesPath).DeepClone();
        XElement template = templates.FindById(card.Id)
            ?? throw new InvalidOperationException($"Template {card.Id} не найден.");
        XElement ability = abilities.FindRequiredCardAbility(card.Id);

        var changes = new List<string>();
        var warnings = new List<string>();
        foreach (OrderMechanicOperationRecipe operation in recipe.Operations)
        {
            switch (operation.Type.Trim().ToLowerInvariant())
            {
                case "addorder":
                    AddOrder(abilities, template, ability, operation, changes);
                    break;
                case "addchargestoorder":
                    AddCharges(template, ability, operation, changes);
                    break;
                case "addcooldowntoorder":
                    AddCooldown(abilities, template, ability, operation, changes);
                    break;
                case "convertdeploytoorder":
                    ConvertDeployToOrder(template, ability, operation, changes);
                    break;
                case "convertordertodeploy":
                    ConvertOrderToDeploy(template, ability, changes);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Неподдерживаемая order operation: {operation.Type}");
            }
        }

        GraphValidationReport validation = GraphValidator.Validate(ability);
        if (!validation.IsValid)
        {
            string details = string.Join(Environment.NewLine, validation.Issues
                .Where(issue => issue.Severity == ValidationSeverity.Error)
                .Select(issue => $"[{issue.Code}] {issue.Message}"));
            throw new InvalidOperationException("Итоговый граф Ability недействителен:" + Environment.NewLine + details);
        }

        string tooltipKey = $"{card.Id}_tooltip";
        string oldTooltip = LocalizationService.ReadValue(localizationPath, tooltipKey) ?? "";
        string tooltip = !recipe.UpdateTooltip
            ? oldTooltip
            : !string.IsNullOrWhiteSpace(recipe.Tooltip)
                ? recipe.Tooltip
                : BuildAutomaticTooltip(oldTooltip, recipe.Operations);
        if (recipe.UpdateTooltip && string.IsNullOrWhiteSpace(tooltip))
            throw new InvalidOperationException(
                $"Для {tooltipKey} не удалось построить tooltip автоматически. " +
                "Укажите текст вручную или используйте исправный исходный ru-ru.csv.");

        IReadOnlyList<string> unknown = LocalizationService.FindUnknownPlaceholders(
            abilities, card.Id, tooltip);
        foreach (string placeholder in unknown)
            warnings.Add($"Неизвестный placeholder в tooltip: {{{placeholder}}}");

        var preview = new OrderMechanicsPreview
        {
            Card = card,
            Abilities = abilities,
            Templates = templates,
            GraphValidation = validation,
            TooltipKey = tooltipKey,
            TooltipValue = tooltip
        };
        preview.Changes.AddRange(changes);
        preview.Warnings.AddRange(warnings);
        foreach (ValidationIssue issue in validation.Issues.Where(i => i.Severity == ValidationSeverity.Warning))
            preview.Warnings.Add($"[{issue.Code}] {issue.Message}");
        return preview;
    }

    public static OrderMechanicsResult ApplyAndSave(
        string abilitiesPath,
        string templatesPath,
        string localizationPath,
        OrderMechanicsRecipe recipe,
        string outputDirectory,
        bool overwrite)
    {
        OrderMechanicsPreview preview = BuildPreview(
            abilitiesPath, templatesPath, localizationPath, recipe);
        string fullOutput = Path.GetFullPath(outputDirectory.Trim().Trim('"'));
        if (Directory.Exists(fullOutput) && !overwrite)
            throw new IOException($"Выходной каталог уже существует: {fullOutput}");
        if (File.Exists(fullOutput))
            throw new IOException($"Путь занят файлом: {fullOutput}");

        string parent = Path.GetDirectoryName(fullOutput)
            ?? throw new InvalidOperationException("Не удалось определить родительский каталог.");
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, "." + Path.GetFileName(fullOutput) + "." + Guid.NewGuid().ToString("N") + ".staging");
        string abilitiesOutput = Path.Combine(staging, "Abilities.modified.xml");
        string templatesOutput = Path.Combine(staging, "Templates.modified.xml");
        string localizationOutput = Path.Combine(staging, "ru-ru.modified.csv");
        string reportOutput = Path.Combine(staging, "order-mechanics-report.txt");

        try
        {
            Directory.CreateDirectory(staging);
            preview.Abilities.SaveAs(abilitiesOutput, false);
            preview.Templates.SaveAs(templatesOutput, false);
            if (recipe.UpdateTooltip)
            {
                LocalizationService.ApplyEditsAndSave(
                    localizationPath,
                    new Dictionary<string, string> { [preview.TooltipKey] = preview.TooltipValue },
                    localizationOutput,
                    false);
            }
            else
            {
                File.Copy(localizationPath, localizationOutput, false);
            }

            File.WriteAllText(reportOutput, BuildReport(recipe, preview), new UTF8Encoding(false));
            AbilityDocument savedAbilities = AbilityDocument.Load(abilitiesOutput);
            TemplateDocument.Load(templatesOutput);
            GraphValidationReport savedValidation = GraphValidator.Validate(
                savedAbilities.FindRequiredCardAbility(preview.Card.Id));
            if (!savedValidation.IsValid)
                throw new InvalidDataException("Сохранённый Ability graph не прошёл повторную проверку.");
            if (recipe.UpdateTooltip)
            {
                string? storedTooltip = LocalizationService.ReadValue(
                    localizationOutput, preview.TooltipKey);
                if (!string.Equals(storedTooltip, preview.TooltipValue, StringComparison.Ordinal))
                    throw new InvalidDataException("Проверка сохранённого tooltip не пройдена.");
            }

            string? backup = null;
            try
            {
                if (Directory.Exists(fullOutput))
                {
                    backup = fullOutput + "." + Guid.NewGuid().ToString("N") + ".backup";
                    Directory.Move(fullOutput, backup);
                }
                Directory.Move(staging, fullOutput);
                if (backup != null && Directory.Exists(backup))
                    Directory.Delete(backup, true);
            }
            catch
            {
                if (!Directory.Exists(fullOutput) && backup != null && Directory.Exists(backup))
                    Directory.Move(backup, fullOutput);
                throw;
            }
        }
        catch
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            throw;
        }

        return new OrderMechanicsResult
        {
            OutputDirectory = fullOutput,
            AbilitiesFile = Path.Combine(fullOutput, "Abilities.modified.xml"),
            TemplatesFile = Path.Combine(fullOutput, "Templates.modified.xml"),
            LocalizationFile = Path.Combine(fullOutput, "ru-ru.modified.csv"),
            ReportFile = Path.Combine(fullOutput, "order-mechanics-report.txt"),
            Preview = preview
        };
    }

    private static string BuildAutomaticTooltip(
        string oldTooltip,
        IReadOnlyList<OrderMechanicOperationRecipe> operations)
    {
        string tooltip = oldTooltip.Trim();
        if (tooltip == "\\n")
            tooltip = "";

        foreach (OrderMechanicOperationRecipe operation in operations)
        {
            string type = operation.Type.Trim().ToLowerInvariant();
            if (type == "convertdeploytoorder")
            {
                tooltip = ConvertDeployTooltipToOrder(tooltip, operation.Zeal);
            }
            else if (type == "convertordertodeploy")
            {
                tooltip = ConvertOrderTooltipToDeploy(tooltip);
            }
            else if (type == "addcooldowntoorder")
            {
                if (operation.Resupply &&
                    !tooltip.Contains("<keyword=resupply>", StringComparison.OrdinalIgnoreCase))
                {
                    tooltip = AppendTooltipLine(
                        tooltip,
                        "<keyword=resupply>Восполнение</keyword>.");
                }

                if (!tooltip.Contains("<keyword=cooldown>", StringComparison.OrdinalIgnoreCase) &&
                    !tooltip.Contains("{Cooldown}", StringComparison.Ordinal))
                {
                    tooltip = AppendTooltipLine(
                        tooltip,
                        "<keyword=cooldown>Ходов до восстановления</keyword>: {Cooldown}.");
                }
            }
            else if (type == "addchargestoorder")
            {
                if (!tooltip.Contains("<keyword=charge>", StringComparison.OrdinalIgnoreCase) &&
                    !tooltip.Contains("{Charges}", StringComparison.Ordinal))
                {
                    tooltip = AppendTooltipLine(
                        tooltip,
                        "<keyword=charge>Заряды</keyword>: {Charges}.");
                }
            }
        }

        return tooltip;
    }

    private static string AppendTooltipLine(string tooltip, string line) =>
        string.IsNullOrWhiteSpace(tooltip)
            ? line
            : tooltip.TrimEnd() + "\\n" + line;

    private static string ConvertDeployTooltipToOrder(string tooltip, bool zeal)
    {
        string converted = tooltip.Replace(
            "<keyword=deploy>Размещение</keyword>",
            "<keyword=order>Приказ</keyword>",
            StringComparison.OrdinalIgnoreCase);
        if (zeal && !converted.Contains("<keyword=zeal>", StringComparison.OrdinalIgnoreCase))
            converted = "<keyword=zeal>Рвение</keyword>. " + converted;
        return converted;
    }

    private static string ConvertOrderTooltipToDeploy(string tooltip)
    {
        string converted = tooltip.Replace(
            "<keyword=order>Приказ</keyword>",
            "<keyword=deploy>Размещение</keyword>",
            StringComparison.OrdinalIgnoreCase);
        const string zealPrefix = "<keyword=zeal>Рвение</keyword>. ";
        if (converted.StartsWith(zealPrefix, StringComparison.OrdinalIgnoreCase))
            converted = converted[zealPrefix.Length..];
        return converted;
    }

    private static void ConvertDeployToOrder(
        XElement template,
        XElement ability,
        OrderMechanicOperationRecipe operation,
        List<string> changes)
    {
        if (operation.Charges < 1)
            throw new InvalidOperationException("ConvertDeployToOrder: Charges должны быть положительными.");

        XElement nodes = ability.Element("Nodes")
            ?? throw new InvalidDataException("Ability не содержит Nodes.");
        XElement connections = ability.Element("Connections")
            ?? throw new InvalidDataException("Ability не содержит Connections.");
        List<XElement> deploys = nodes.Elements()
            .Where(node => string.Equals((string?)node.Attribute("Type"), "PlayedTrigger", StringComparison.Ordinal))
            .ToList();
        if (deploys.Count != 1)
            throw new InvalidOperationException(
                $"ConvertDeployToOrder требует ровно один PlayedTrigger; найдено {deploys.Count}.");
        if (nodes.Elements().Any(node =>
                string.Equals((string?)node.Attribute("Type"), "TappedTrigger", StringComparison.Ordinal)))
            throw new InvalidOperationException("Ability уже содержит TappedTrigger; второй Приказ не создаётся.");

        XElement deploy = deploys[0];
        XElement priority = deploy.Element("Priority")
            ?? throw new InvalidDataException("PlayedTrigger не содержит Priority.");
        XElement flowOut = deploy.Element("FlowOut")
            ?? throw new InvalidDataException("PlayedTrigger не содержит FlowOut.");
        HashSet<int> retainedIds = new()
        {
            RequiredId(deploy, "PlayedTrigger"),
            RequiredId(priority, "PlayedTrigger.Priority"),
            RequiredId(flowOut, "PlayedTrigger.FlowOut")
        };
        HashSet<int> removedEndpointIds = deploy.DescendantsAndSelf()
            .Select(element => (int?)element.Attribute("Id"))
            .Where(id => id.HasValue && !retainedIds.Contains(id.Value))
            .Select(id => id!.Value)
            .ToHashSet();
        List<XElement> unsafeConnections = connections.Elements()
            .Where(connection =>
            {
                int? source = (int?)connection.Attribute("SrcId");
                int? target = (int?)connection.Attribute("DstId");
                return (source.HasValue && removedEndpointIds.Contains(source.Value))
                    || (target.HasValue && removedEndpointIds.Contains(target.Value));
            })
            .ToList();
        if (unsafeConnections.Count > 0)
        {
            string details = string.Join(", ", unsafeConnections.Select(connection =>
                $"{(string?)connection.Attribute("SrcId")}->{(string?)connection.Attribute("DstId")}"));
            throw new InvalidOperationException(
                "PlayedTrigger использует CardPlayed/From/To/Forced; безопасная замена невозможна. Connections: " + details);
        }

        var order = new XElement(
            deploy.Name,
            deploy.Attributes().Select(attribute => new XAttribute(attribute)));
        order.SetAttributeValue("Type", "TappedTrigger");
        order.Add(new XElement(priority));
        order.Add(new XElement(flowOut));
        deploy.ReplaceWith(order);

        SetTemplateValue(template, "InitialTimer", "-1", changes);
        SetTemplateValue(template, "TapActive", "True", changes);
        SetTemplateValue(template, "InitalTapCharges", operation.Charges.ToString(), changes);
        SetTemplateValue(template, "VisibleTapCharges", operation.VisibleCharges ? "True" : "False", changes);
        SetTemplateValue(template, "SummoningSickness", operation.Zeal ? "False" : "True", changes);
        int triggerId = RequiredId(order, "TappedTrigger");
        int flowOutId = RequiredId(flowOut, "TappedTrigger.FlowOut");
        changes.Add(
            $"Ability: PlayedTrigger Id={triggerId} -> TappedTrigger; " +
            $"FlowOut Id={flowOutId} preserved.");
    }

    private static void ConvertOrderToDeploy(
        XElement template,
        XElement ability,
        List<string> changes)
    {
        XElement nodes = ability.Element("Nodes")
            ?? throw new InvalidDataException("Ability не содержит Nodes.");
        List<XElement> orders = nodes.Elements()
            .Where(node => string.Equals((string?)node.Attribute("Type"), "TappedTrigger", StringComparison.Ordinal))
            .ToList();
        if (orders.Count != 1)
            throw new InvalidOperationException(
                $"ConvertOrderToDeploy требует ровно один TappedTrigger; найдено {orders.Count}.");
        if (nodes.Elements().Any(node =>
                string.Equals((string?)node.Attribute("Type"), "PlayedTrigger", StringComparison.Ordinal)))
            throw new InvalidOperationException("Ability уже содержит PlayedTrigger; второй Deploy не создаётся.");

        string[] unsupportedOrderInfrastructure =
        {
            "CardTimerElapsedTrigger",
            "ChangeTapCardChargesNode",
            "ChangeCardTimerNode",
            "ChangeOwnerTapActiveNode",
            "RefreshTargetsTrigger"
        };
        List<string> presentInfrastructure = nodes.Elements()
            .Select(node => (string?)node.Attribute("Type") ?? "")
            .Where(type => unsupportedOrderInfrastructure.Contains(type, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToList();
        if (presentInfrastructure.Count > 0)
            throw new InvalidOperationException(
                "ConvertOrderToDeploy поддерживает только простой одноразовый Приказ без cooldown/charges/refresh infrastructure. " +
                "Найдены: " + string.Join(", ", presentInfrastructure));

        int charges = int.TryParse(template.Element("InitalTapCharges")?.Value, out int parsedCharges)
            ? parsedCharges
            : 0;
        int timer = int.TryParse(template.Element("InitialTimer")?.Value, out int parsedTimer)
            ? parsedTimer
            : int.MinValue;
        if (charges != 1 || timer != -1)
            throw new InvalidOperationException(
                $"ConvertOrderToDeploy поддерживает простой одноразовый Приказ: InitalTapCharges=1 и InitialTimer=-1; " +
                $"получено {charges} и {timer}.");

        XElement order = orders[0];
        XElement priority = order.Element("Priority")
            ?? throw new InvalidDataException("TappedTrigger не содержит Priority.");
        XElement flowOut = order.Element("FlowOut")
            ?? throw new InvalidDataException("TappedTrigger не содержит FlowOut.");
        int next = NextLocalId(ability);
        int cardPlayed = next++;
        int from = next++;
        int to = next++;
        int forced = next++;

        var deploy = new XElement(
            order.Name,
            order.Attributes().Select(attribute => new XAttribute(attribute)));
        deploy.SetAttributeValue("Type", "PlayedTrigger");
        deploy.Add(
            new XElement(priority),
            new XElement(flowOut),
            SimpleVariable("CardPlayed", "CardVar", cardPlayed),
            CardPositionVariable("From", from),
            CardPositionVariable("To", to),
            SimpleVariable("Forced", "BoolVar", forced, "False"));
        order.ReplaceWith(deploy);
        ability.SetAttributeValue("NId", Math.Max(next, NextLocalId(ability)));

        SetTemplateValue(template, "InitialTimer", "-1", changes);
        SetTemplateValue(template, "TapActive", "False", changes);
        SetTemplateValue(template, "InitalTapCharges", "0", changes);
        SetTemplateValue(template, "VisibleTapCharges", "False", changes);
        SetTemplateValue(template, "SummoningSickness", "True", changes);
        int triggerId = RequiredId(deploy, "PlayedTrigger");
        int flowOutId = RequiredId(flowOut, "PlayedTrigger.FlowOut");
        changes.Add(
            $"Ability: TappedTrigger Id={triggerId} -> PlayedTrigger; " +
            $"FlowOut Id={flowOutId} preserved.");
    }

    private static XElement SimpleVariable(
        string name,
        string type,
        int id,
        string? value = null)
    {
        var element = new XElement(name,
            new XAttribute("Type", type),
            new XAttribute("Id", id),
            new XAttribute("ExternalOut", "False"),
            new XAttribute("ExternalIn", "False"),
            new XAttribute("ResetInInactive", "False"));
        if (value != null)
            element.SetAttributeValue("V", value);
        return element;
    }

    private static XElement CardPositionVariable(string name, int id) =>
        new(name,
            new XAttribute("Type", "CardPositionVar"),
            new XAttribute("Id", id),
            new XAttribute("ExternalOut", "False"),
            new XAttribute("ExternalIn", "False"),
            new XAttribute("ResetInInactive", "False"),
            new XElement("V",
                new XAttribute("Type", "CardPosition"),
                new XAttribute("PlayerId", 0),
                new XAttribute("Location", 0),
                new XAttribute("Index", 0)));

    private static int RequiredId(XElement element, string description) =>
        (int?)element.Attribute("Id")
        ?? throw new InvalidDataException($"{description} не содержит Id.");

    private static void AddOrder(
        AbilityDocument document,
        XElement template,
        XElement ability,
        OrderMechanicOperationRecipe operation,
        List<string> changes)
    {
        if (ability.Element("Nodes")!.Elements().Any(n => (string?)n.Attribute("Type") == "TappedTrigger"))
            throw new InvalidOperationException("У карты уже есть TappedTrigger. Используйте другую операцию.");
        if (operation.Choices.Count < 2)
            throw new InvalidOperationException("AddOrder требует минимум две Choices.");
        if (operation.Charges < 1)
            throw new InvalidOperationException("Charges должны быть положительными.");

        // Resolve choices against the same Templates.xml through the document root.
        var cards = template.Document?.Root == null
            ? new List<CardRecord>()
            : template.Document.Root.Elements("Template")
                .Where(e => (string?)e.Attribute("Type") == "CardTemplate")
                .Select(e => new CardRecord { Id = (int?)e.Attribute("Id") ?? 0, DebugName = (string?)e.Attribute("DebugName") ?? "" })
                .ToList();
        var choiceIds = new List<int>();
        foreach (string choice in operation.Choices)
        {
            CardRecord resolved = ResolveCard(cards, choice);
            choiceIds.Add(resolved.Id);
        }

        XElement source = document.FindRequiredCardAbility(ChoicePlaySource);
        int variableId = AddChoiceVariable(ability, choiceIds);
        Dictionary<int, int> map = AppendFragment(
            ability,
            source,
            new[] { 8, 19, 22, 30, 33, 45, 51, 61, 225 },
            new Dictionary<int, int> { [1] = variableId });
        AddConnection(ability, map[227], map[9]);

        SetTemplateValue(template, "TapActive", "True", changes);
        SetTemplateValue(template, "InitalTapCharges", operation.Charges.ToString(), changes);
        SetTemplateValue(template, "VisibleTapCharges", operation.VisibleCharges ? "True" : "False", changes);
        SetTemplateValue(template, "SummoningSickness", operation.Zeal ? "False" : "True", changes);
        changes.Add($"Ability: added Order that chooses and immediately plays {string.Join(" / ", choiceIds)}.");
    }

    private static void AddCharges(
        XElement template,
        XElement ability,
        OrderMechanicOperationRecipe operation,
        List<string> changes)
    {
        RequireOrder(ability);
        if (operation.Charges < 1)
            throw new InvalidOperationException("Charges должны быть положительными.");
        SetTemplateValue(template, "TapActive", "True", changes);
        SetTemplateValue(template, "InitalTapCharges", operation.Charges.ToString(), changes);
        SetTemplateValue(template, "VisibleTapCharges", operation.VisibleCharges ? "True" : "False", changes);
        if (operation.Zeal)
            SetTemplateValue(template, "SummoningSickness", "False", changes);
    }

    private static void AddCooldown(
        AbilityDocument document,
        XElement template,
        XElement ability,
        OrderMechanicOperationRecipe operation,
        List<string> changes)
    {
        RequireOrder(ability);
        if (operation.Cooldown < 1 || operation.Cooldown > 99)
            throw new InvalidOperationException("Cooldown должен быть 1–99.");
        if (operation.ResupplyAmount != 1)
            throw new NotSupportedException("В 0.27.9 ResupplyAmount поддерживает только значение 1.");
        if (ability.Element("Nodes")!.Elements().Any(n => (string?)n.Attribute("Type") == "CardTimerElapsedTrigger"))
            throw new InvalidOperationException("У Ability уже есть cooldown/timer infrastructure.");

        AddOrUpdateIntVariable(ability, "Cooldown", operation.Cooldown);
        if (operation.Resupply)
            AddOrUpdateIntVariable(ability, "Cooldown2", operation.ResupplyAmount);

        int terminalFlow = FindTerminalFlowOut(ability, operation.OrderTerminalNodeId);
        XElement source = document.FindRequiredCardAbility(CooldownSource);
        Dictionary<int, int> map = AppendFragment(
            ability,
            source,
            new[] { 1147, 1155, 1162, 1165, 1170, 1182, 1185, 1194, 1197 },
            new Dictionary<int, int>());
        XElement startTimer = FindNodeById(ability, map[1185]);
        startTimer.Element("Value")!.SetAttributeValue("V", operation.Cooldown);
        AddConnection(ability, terminalFlow, map[1186]);

        if (operation.Resupply)
        {
            AppendFragment(
                ability,
                source,
                new[] { 1093, 1039 },
                new Dictionary<int, int>());
        }

        SetTemplateValue(template, "TapActive", "True", changes);
        XElement charges = template.Element("InitalTapCharges")
            ?? throw new InvalidDataException("Template не содержит InitalTapCharges.");
        if (!int.TryParse(charges.Value, out int chargeCount) || chargeCount < 1)
            SetTemplateValue(template, "InitalTapCharges", "1", changes);
        changes.Add($"Ability: added Cooldown {operation.Cooldown}" + (operation.Resupply ? " + Resupply (Warfare, -1)." : "."));
    }

    private static Dictionary<int, int> AppendFragment(
        XElement target,
        XElement source,
        IEnumerable<int> nodeIds,
        IReadOnlyDictionary<int, int> externalVariableMap)
    {
        XElement targetNodes = target.Element("Nodes")!;
        XElement targetConnections = target.Element("Connections")!;
        XElement sourceNodes = source.Element("Nodes")!;
        XElement sourceConnections = source.Element("Connections")!;
        HashSet<int> requested = nodeIds.ToHashSet();
        List<XElement> selected = sourceNodes.Elements()
            .Where(n => requested.Contains((int)n.Attribute("Id")!))
            .Select(n => new XElement(n))
            .ToList();
        if (selected.Count != requested.Count)
            throw new InvalidOperationException("Не все native source nodes найдены.");

        HashSet<int> endpoints = selected.SelectMany(n => n.DescendantsAndSelf())
            .Where(e => e.Attribute("Id") != null)
            .Select(e => (int)e.Attribute("Id")!)
            .ToHashSet();
        List<XElement> conns = sourceConnections.Elements()
            .Where(c => endpoints.Contains((int)c.Attribute("SrcId")!) && endpoints.Contains((int)c.Attribute("DstId")!))
            .Select(c => new XElement(c))
            .ToList();

        int next = NextLocalId(target);
        var map = new Dictionary<int, int>();
        foreach (int old in selected.Concat(conns)
                     .SelectMany(e => e.DescendantsAndSelf())
                     .Where(e => e.Attribute("Id") != null)
                     .Select(e => (int)e.Attribute("Id")!))
        {
            if (!map.ContainsKey(old))
                map[old] = next++;
        }

        foreach (XElement root in selected.Concat(conns))
        {
            foreach (XElement e in root.DescendantsAndSelf())
            {
                XAttribute? id = e.Attribute("Id");
                if (id != null && map.TryGetValue((int)id, out int newId))
                    id.Value = newId.ToString();
                foreach (string attrName in new[] { "SrcId", "DstId", "VarId" })
                {
                    XAttribute? a = e.Attribute(attrName);
                    if (a == null) continue;
                    int old = (int)a;
                    if (map.TryGetValue(old, out int remapped))
                        a.Value = remapped.ToString();
                    else if (attrName == "VarId" && externalVariableMap.TryGetValue(old, out int variable))
                        a.Value = variable.ToString();
                }
            }
        }

        int nodeIndex = targetNodes.Elements().Count();
        foreach (XElement node in selected)
        {
            node.Name = "e" + nodeIndex++;
            targetNodes.Add(node);
        }
        int connectionIndex = targetConnections.Elements().Count();
        foreach (XElement connection in conns)
        {
            connection.Name = "e" + connectionIndex++;
            targetConnections.Add(connection);
        }
        target.SetAttributeValue("NId", Math.Max(next, NextLocalId(target)));
        return map;
    }

    private static int AddChoiceVariable(XElement ability, IReadOnlyList<int> ids)
    {
        XElement temporary = ability.Element("TemporaryVariables")!;
        int id = NextLocalId(ability);
        var value = new XElement("V");
        for (int index = 0; index < ids.Count; index++)
        {
            value.Add(new XElement("e" + index,
                new XAttribute("Type", "CardDefinition"),
                new XAttribute("TemplateId", ids[index]),
                new XAttribute("IsPremium", "False")));
        }
        var variable = new XElement("e" + temporary.Elements().Count(),
            new XAttribute("Type", "CardDefinitionListVar"),
            new XAttribute("Id", id),
            new XAttribute("Name", "OrderChoices"),
            new XAttribute("ExternalOut", "False"),
            new XAttribute("ExternalIn", "False"),
            new XAttribute("ResetInInactive", "False"),
            new XAttribute("HideInTooltip", "False"),
            value);
        temporary.Add(variable);
        ability.SetAttributeValue("NId", id + 1);
        return id;
    }

    private static void AddOrUpdateIntVariable(XElement ability, string name, int value)
    {
        XElement temporary = ability.Element("TemporaryVariables")!;
        XElement? existing = temporary.Elements().FirstOrDefault(v =>
            string.Equals((string?)v.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.SetAttributeValue("V", value);
            return;
        }
        int id = NextLocalId(ability);
        temporary.Add(new XElement("e" + temporary.Elements().Count(),
            new XAttribute("Type", "IntVar"),
            new XAttribute("Id", id),
            new XAttribute("Name", name),
            new XAttribute("ExternalOut", "False"),
            new XAttribute("ExternalIn", "False"),
            new XAttribute("ResetInInactive", "False"),
            new XAttribute("V", value)));
        ability.SetAttributeValue("NId", id + 1);
    }

    private static int FindTerminalFlowOut(XElement ability, int? nodeId)
    {
        XElement nodes = ability.Element("Nodes")!;
        if (nodeId.HasValue)
        {
            XElement node = nodes.Elements().FirstOrDefault(n => (int?)n.Attribute("Id") == nodeId.Value)
                ?? throw new InvalidOperationException($"Order terminal Node Id={nodeId} не найден.");
            XElement output = node.Element("FlowOut")
                ?? throw new InvalidOperationException($"Node Id={nodeId} не содержит FlowOut.");
            return (int)output.Attribute("Id")!;
        }

        IReadOnlyList<OrderTerminalCandidate> candidates = FindOrderTerminalCandidates(ability);
        if (candidates.Count == 1)
            return candidates[0].FlowOutId;
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "Не удалось найти последнее действие существующего Приказа автоматически.");

        string details = string.Join(Environment.NewLine, candidates.Select((candidate, index) =>
            $"  {index + 1}. {candidate.NodeType}: {candidate.Summary}"));
        throw new InvalidOperationException(
            "У Приказа найдено несколько возможных последних действий. " +
            "Выберите одно из них в интерактивном режиме:" + Environment.NewLine + details);
    }

    private static string DescribeOrderNode(XElement node)
    {
        string type = (string?)node.Attribute("Type") ?? "UnknownNode";
        int id = (int?)node.Attribute("Id") ?? 0;
        var details = new List<string>();

        AddValue("Power");
        AddValue("Damage");
        AddValue("Boost");
        AddValue("Tokens");
        AddValue("Value");
        AddValue("Num");
        AddValue("Operation");
        AddValue("SubAbilityID", attribute: false);

        return details.Count == 0
            ? $"{type} (внутренний узел {id})"
            : $"{type}: {string.Join(", ", details)}";

        void AddValue(string name, bool attribute = true)
        {
            XElement? child = node.Element(name);
            string? value = attribute
                ? (string?)child?.Attribute("V")
                : child?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                details.Add($"{name}={value}");
        }
    }

    private static void RequireOrder(XElement ability)
    {
        if (!ability.Element("Nodes")!.Elements().Any(n => (string?)n.Attribute("Type") == "TappedTrigger"))
            throw new InvalidOperationException("У карты нет TappedTrigger/Order.");
    }

    private static void AddConnection(XElement ability, int source, int target)
    {
        XElement connections = ability.Element("Connections")!;
        int id = NextLocalId(ability);
        connections.Add(new XElement("e" + connections.Elements().Count(),
            new XAttribute("Type", "Connection"),
            new XAttribute("Id", id),
            new XAttribute("SrcId", source),
            new XAttribute("DstId", target)));
        ability.SetAttributeValue("NId", id + 1);
    }

    private static XElement FindNodeById(XElement ability, int id) =>
        ability.Element("Nodes")!.Elements().First(n => (int?)n.Attribute("Id") == id);

    private static int NextLocalId(XElement ability)
    {
        int max = ability.Elements()
            .Where(e => e.Name.LocalName is "TemporaryVariables" or "PersistentVariables" or "Nodes" or "Connections")
            .SelectMany(e => e.DescendantsAndSelf())
            .Where(e => e.Attribute("Id") != null)
            .Select(e => (int)e.Attribute("Id")!)
            .DefaultIfEmpty(0).Max();
        int nid = (int?)ability.Attribute("NId") ?? 1;
        return Math.Max(max + 1, nid);
    }

    private static void SetTemplateValue(XElement template, string field, string value, List<string> changes)
    {
        XElement element = template.Element(field)
            ?? throw new InvalidDataException($"Template не содержит {field}.");
        string old = element.Value;
        element.Value = value;
        if (old != value)
            changes.Add($"Template/{field}: {old} -> {value}");
    }

    private static CardRecord ResolveCard(IEnumerable<CardRecord> cards, string query)
    {
        string trimmed = query.Trim();
        if (int.TryParse(trimmed, out int id))
            return cards.Single(c => c.Id == id);
        List<CardRecord> exact = cards.Where(c => c.DebugName.Equals(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count != 1)
            throw new InvalidOperationException($"Choice card '{query}' не найдена однозначно.");
        return exact[0];
    }

    private static void ValidateRecipe(OrderMechanicsRecipe recipe)
    {
        if (recipe.FormatVersion != 1)
            throw new InvalidDataException($"Неподдерживаемый FormatVersion={recipe.FormatVersion}.");
        if (string.IsNullOrWhiteSpace(recipe.Card))
            throw new InvalidDataException("Не указана Card.");
        if (recipe.Operations.Count == 0)
            throw new InvalidDataException("Recipe не содержит Operations.");
    }

    private static string BuildReport(OrderMechanicsRecipe recipe, OrderMechanicsPreview preview)
    {
        var b = new StringBuilder();
        b.AppendLine($"Order mechanics: {recipe.Name}");
        b.AppendLine($"Card: {preview.Card.Id} {preview.Card.DebugName}");
        b.AppendLine($"Graph errors: {preview.GraphValidation.ErrorCount}");
        b.AppendLine($"Graph warnings: {preview.GraphValidation.WarningCount}");
        b.AppendLine("Changes:");
        foreach (string change in preview.Changes) b.AppendLine("  " + change);
        b.AppendLine("Warnings:");
        foreach (string warning in preview.Warnings) b.AppendLine("  " + warning);
        return b.ToString();
    }
}
