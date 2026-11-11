using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed record AuditEndpoint(int Id, int? NodeId, string NodeType, string Field, string Type,
    string Name, string? Value, string Semantics, int? VariableReference);
public sealed record AuditRow(int CardId, int Id, string Name, int Value, string Role, string Reason,
    string Classification, string Scope, string Pattern, string Tier, bool Named, string[] Placeholders,
    AuditEndpoint Location, AuditEndpoint[] Incoming, AuditEndpoint[] Consumers, AuditEndpoint[] References,
    int[] EditableSources);
public sealed record AuditCard(int Id, string NameRu, string NameEn, string DebugName, int Faction,
    string TooltipRu, string TooltipEn, int? GraphId, int[] DeclaredAbilities, string GraphHash, string Status);
public sealed record AuditGroup(string Key, string Reason, string Classification, string Scope, string Pattern,
    string Tier, int Values, int Cards, int[] CardIds, int[] SampleRows);
public sealed record AuditReport(string Version, DateTime CreatedUtc, string Source, Dictionary<string, string> InputHashes,
    int CatalogCards, int TutorialCards, int SupportedCards, int SkippedCards, int EditableValues,
    Dictionary<string, int> BlockedReasons, int AdditionalValues, AuditCard[] Cards, AuditRow[] Rows, AuditGroup[] Groups);

public sealed partial class EditorProject
{
    public AuditReport CreateNumericAudit()
    {
        if (HasChanges) throw new InvalidOperationException("AuditRequiresCleanProject");
        VerifyAuditSource();
        var cards = new List<AuditCard>();
        var rows = new List<AuditRow>();
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        int editable = 0;
        foreach (CardEntry card in Cards.Where(c => c.Editable).OrderBy(c => c.Id))
        {
            XElement? graph = CurrentAbility(card.Id);
            int[] declared = Template(card.Id).Element("AbilityIds")?.Elements().Select(x => (int?)x ?? 0).ToArray() ?? [];
            cards.Add(new(card.Id, Name(card.Id, "ru-ru"), Name(card.Id, "en-us"), card.DebugName, card.Faction,
                Text(card.Id, "ru-ru", "tooltip"), Text(card.Id, "en-us", "tooltip"), (int?)graph?.Attribute("Id"), declared,
                graph == null ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(graph.ToString(SaveOptions.DisableFormatting)))),
                graph == null ? "NoOwnCardAbility" : declared.Length != 1 || declared[0] != card.Id ? "LinkedAbility" : "OwnCardAbility"));
            if (graph == null) continue;
            var known = Numbers(card.Id).ToDictionary(v => v.Id);
            editable += known.Values.Count(v => v.Editable);
            foreach (var number in known.Values.Where(v => !v.Editable)) reasons[number.Reason] = reasons.GetValueOrDefault(number.Reason) + 1;
            var endpoints = graph.Descendants().Where(x => (int?)x.Attribute("Id") != null)
                .GroupBy(x => (int)x.Attribute("Id")!).ToDictionary(g => g.Key, g => g.First());
            XElement[] nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
            XElement[] connections = graph.Element("Connections")?.Elements().ToArray() ?? [];
            var raw = SafeNumericEditorService.InspectGraph(graph).Where(v => v.VariableId.HasValue).ToDictionary(v => v.VariableId!.Value);
            var numeric = graph.Descendants().Where(x => (string?)x.Attribute("Type") == "IntVar"
                && (int?)x.Attribute("Id") != null && int.TryParse((string?)x.Attribute("V"), out _))
                .GroupBy(x => (int)x.Attribute("Id")!).Select(g => g.First());
            foreach (XElement value in numeric)
            {
                int id = (int)value.Attribute("Id")!;
                if (known.TryGetValue(id, out var status) && status.Editable) continue;
                bool named = value.Parent?.Name.LocalName is "TemporaryVariables" or "PersistentVariables";
                string name = (string?)value.Attribute("Name") ?? "";
                int[] getterOutputs = nodes.Where(n => (string?)n.Attribute("Type") == "GetVarNode" && (int?)n.Attribute("VarId") == id)
                    .Select(n => (int?)n.Element("Out")?.Attribute("Id")).Where(n => n.HasValue).Select(n => n!.Value).ToArray();
                var sources = getterOutputs.Append(id).ToHashSet();
                var consumers = connections.Where(c => sources.Contains((int?)c.Attribute("SrcId") ?? -1))
                    .Select(c => (int?)c.Attribute("DstId") ?? -1).Distinct().Select(Endpoint).ToArray();
                var incoming = connections.Where(c => (int?)c.Attribute("DstId") == id)
                    .Select(c => (int?)c.Attribute("SrcId") ?? -1).Distinct().Select(Endpoint).ToArray();
                var references = nodes.Where(n => n.Attributes().Any(a => a.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(a.Value, out int v) && v == id)).Select(n => Endpoint((int)n.Attribute("Id")!)).ToArray();
                var rootRefs = graph.Attributes().Where(a => a.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(a.Value, out int v) && v == id).Select(a => a.Name.LocalName).Order().ToArray();
                string reason = status?.Reason ?? "UnclassifiedNumeric";
                int[] editableSources = incoming.Where(e => e.NodeType == "GetVarNode" && e.VariableReference.HasValue)
                    .Select(e => e.VariableReference!.Value).Where(v => known.TryGetValue(v, out var source) && source.Editable).Distinct().Order().ToArray();
                string classification = Classify(reason, named, incoming, consumers, rootRefs, references, editableSources, value);
                string scope = status != null ? "BlockedSetting" : "AdditionalEndpoint";
                string pattern = named ? string.Join(" + ", consumers.Select(Signature).Distinct().Order()) : Signature(Endpoint(id));
                if (named && rootRefs.Length > 0) pattern += " | metadata:" + string.Join(",", rootRefs);
                if (incoming.Length > 0) pattern += " <- " + string.Join(" + ", incoming.Select(Signature).Distinct().Order());
                if (classification == "RuntimeState") pattern += " | writers:" + string.Join(" + ", references.Where(e => e.NodeType != "GetVarNode").Select(Signature).Distinct().Order());
                if (pattern.Length == 0) pattern = "No explicit data consumers";
                string[] aliases = name.Length == 0 ? [] : AvailableLanguages.SelectMany(language => Regex.Matches(Text(card.Id, language, "tooltip"), @"\{([^{}]+)\}")
                        .Select(m => m.Groups[1].Value)).Where(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)
                            || s.StartsWith(name + "-", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
                rows.Add(new(card.Id, id, name, (int)value.Attribute("V")!, status?.Role ?? raw.GetValueOrDefault(id)?.Role ?? "unclassified",
                    reason, classification, scope, pattern, Tier(classification), named, aliases, Endpoint(id), incoming, consumers, references, editableSources));
            }
            foreach (var value in known.Values.Where(v => v.Id < 0 && !v.Editable))
            {
                string field = value.Id == -1 ? "InitalTapCharges" : "InitialTimer";
                var location = new AuditEndpoint(value.Id, null, "Template", field, "IntVar", field, value.Value.ToString(), "", null);
                rows.Add(new(card.Id, value.Id, value.Name, value.Value, value.Role, value.Reason, "TemplateInitialization", "BlockedSetting",
                    "Template." + field, "StateInitialization", false, [], location, [], [], [], []));
            }

            AuditEndpoint Endpoint(int endpoint)
            {
                if (!endpoints.TryGetValue(endpoint, out var element)) return new(endpoint, null, "MissingEndpoint", "?", "?", "", null, "", null);
                XElement? node = element.AncestorsAndSelf().FirstOrDefault(e => e.Parent?.Name.LocalName == "Nodes");
                string field = node == null ? element.Parent?.Name.LocalName ?? "?" : element == node ? "(node)"
                    : string.Join("/", element.AncestorsAndSelf().TakeWhile(e => e != node).Reverse().Select(e => NormalizeField(e.Name.LocalName)));
                string enums = node == null ? "" : string.Join(",", node.Elements().Where(e => ((string?)e.Attribute("Type"))?.StartsWith("EnumVar", StringComparison.Ordinal) == true)
                    .Select(e => e.Name.LocalName + ":" + (string?)e.Attribute("GArg") + "=" + (string?)e.Attribute("V")).Order());
                return new(endpoint, (int?)node?.Attribute("Id"), (string?)node?.Attribute("Type") ?? "Variable", field,
                    (string?)element.Attribute("Type") ?? "?", (string?)element.Attribute("Name") ?? "", (string?)element.Attribute("V"), enums, (int?)node?.Attribute("VarId"));
            }
        }
        var groups = rows.Select((row, index) => (row, index)).GroupBy(x => string.Join("|", x.row.Scope, x.row.Reason, x.row.Classification, x.row.Pattern))
            .Select(g => new AuditGroup(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(g.Key)))[..16], g.First().row.Reason,
                g.First().row.Classification, g.First().row.Scope, g.First().row.Pattern, g.First().row.Tier, g.Count(),
                g.Select(x => x.row.CardId).Distinct().Count(), g.Select(x => x.row.CardId).Distinct().Order().ToArray(), g.Take(8).Select(x => x.index).ToArray()))
            .OrderBy(g => g.Scope).ThenByDescending(g => g.Cards).ThenBy(g => g.Pattern, StringComparer.Ordinal).ToArray();
        VerifyAuditSource();
        var hashes = new Dictionary<string, string>(sourceHashes);
        if (Client != null) hashes["source:data_definitions"] = Client.ArchiveHash;
        return new("1.0", DateTime.UtcNow, SourcePath, hashes, Cards.Count, Cards.Count(c => c.IsTutorial), cards.Count,
            Cards.Count(c => !c.Editable), editable, reasons, rows.Count(r => r.Scope == "AdditionalEndpoint"), cards.ToArray(), rows.ToArray(), groups);
    }

    public AuditReport ExportNumericAudit(string target)
    {
        string destination = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new InvalidOperationException("ExportExists");
        if (destination.Equals(DirectoryPath, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("ExportInsideSource");
        AuditReport report = CreateNumericAudit();
        string parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".numeric-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Path.Combine(staging, "numeric-audit.json"), JsonSerializer.Serialize(report, options), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "numeric-audit-summary.json"), JsonSerializer.Serialize(new
            {
                report.Version, report.CreatedUtc, report.Source, report.CatalogCards, report.TutorialCards, report.SupportedCards, report.SkippedCards,
                report.EditableValues, report.BlockedReasons, report.AdditionalValues,
                Classifications = report.Rows.GroupBy(r => new { r.Scope, r.Classification }).Select(g => new { g.Key.Scope, g.Key.Classification, Values = g.Count(), Cards = g.Select(r => r.CardId).Distinct().Count() }),
                ResearchGroups = report.Groups.Where(g => g.Tier != "NoUnlock").OrderBy(g => g.Scope).ThenByDescending(g => g.Cards).Take(40)
            }, options), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "REPORT_RU.md"), NumericAuditMarkdown.Render(report), new UTF8Encoding(false));
            VerifyAuditSource();
            Directory.Move(staging, destination);
        }
        finally
        {
            // Only this export's newly-created sibling staging directory can be removed.
            if (Directory.Exists(staging) && Path.GetFullPath(staging).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(staging, true);
        }
        return report;
    }

    private void VerifyAuditSource()
    {
        Client?.Verify();
        if (!SourceFiles().Select(Path.GetFileName).Order().SequenceEqual(sourceHashes.Keys.Order())) throw new InvalidOperationException("SourceChanged");
        foreach (var (name, hash) in sourceHashes) if (Hash(Path.Combine(DirectoryPath, name)) != hash) throw new InvalidOperationException("SourceChanged");
    }
    private static string NormalizeField(string value) => value.Length > 1 && value[0] == 'e' && int.TryParse(value[1..], out _) ? "e[]" : value;
    private static string Signature(AuditEndpoint endpoint) => endpoint.NodeType + "." + endpoint.Field + (endpoint.Semantics.Length == 0 ? "" : " [" + endpoint.Semantics + "]");
    private static string Classify(string reason, bool named, AuditEndpoint[] incoming, AuditEndpoint[] consumers,
        string[] metadata, AuditEndpoint[] references, int[] sources, XElement value)
    {
        if (reason is "GraphHasErrors" or "SharedAbility" or "ExternalValue") return reason;
        if (reason == "ComputedValue") return incoming.Length == 1 && sources.Length == 1 ? "AlreadyEditableUpstream" : "ComputedDataflow";
        if (reason == "RuntimeVariable") return "RuntimeState";
        if (metadata.Length > 0) return "MetadataReference";
        if (named && references.Any(r => r.NodeType != "GetVarNode")) return "RuntimeState";
        if (reason == "UnclassifiedNumeric" && (string?)value.Parent?.Attribute("Type") == "GetVarNode") return "AliasOutput";
        if (incoming.Length > 0) return "UnclassifiedComputedEndpoint";
        if (named)
        {
            if (consumers.Length == 0) return "NoExplicitConsumers";
            if (consumers.Any(e => e.NodeType == "IntOpNode" || e.NodeType.StartsWith("Int", StringComparison.Ordinal))) return "IntegerOperation";
            if (consumers.Select(e => e.NodeType + "." + e.Field).Distinct().Count() > 1) return "MultipleConsumers";
            return "UnrecognizedConsumer";
        }
        if (value.Name.LocalName is "Out" or "DV" or "Result" || consumers.Length > 0) return "UnclassifiedDataPort";
        if (((string?)value.Parent?.Attribute("Type"))?.StartsWith("Int", StringComparison.Ordinal) == true) return "IntegerOperation";
        return reason == "UnclassifiedNumeric" ? "UnclassifiedNodeConstant" : "UnrecognizedOperation";
    }
    private static string Tier(string classification) => classification switch
    {
        "UnrecognizedConsumer" or "UnrecognizedOperation" or "UnclassifiedNodeConstant" => "FieldClassifier",
        "TemplateInitialization" or "RuntimeState" => "StateInitialization",
        "IntegerOperation" or "MultipleConsumers" => "FormulaTrace",
        "MetadataReference" or "NoExplicitConsumers" or "ExternalValue" or "SharedAbility" => "EngineReference",
        _ => "NoUnlock"
    };
}

internal static class NumericAuditMarkdown
{
    public static string Render(AuditReport report)
    {
        var text = new StringBuilder();
        string Cell(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        var cards = report.Cards.ToDictionary(c => c.Id);
        text.AppendLine("# Аудит неподтверждённых числовых параметров");
        text.AppendLine().AppendLine($"Источник: `{report.Source}`. Время UTC: {report.CreatedUtc:O}.");
        text.AppendLine().AppendLine("Отчёт только читает данные. Он не разблокирует параметры, не меняет файлы игры и не подтверждает игровые значения по одному лишь тексту описания.");
        text.AppendLine().AppendLine($"Каталог: {report.CatalogCards} карт; туториальных: {report.TutorialCards}; поддерживаемых: {report.SupportedCards}; вне поддерживаемого редактором набора: {report.SkippedCards}.");
        text.AppendLine($"Доступных значений: {report.EditableValues}; заблокированных настроек: {report.BlockedReasons.Values.Sum()}; дополнительных нераспознанных портов/значений: {report.AdditionalValues}.");
        text.AppendLine("Дополнительные значения — отдельная инвентаризация, а не обещание новых настроек. Среди них есть выходы вычислений, индексы и служебные данные.");
        text.AppendLine().AppendLine("## Причины текущей блокировки").AppendLine().AppendLine("| Причина | Значений |").AppendLine("|---|---:|");
        foreach (var pair in report.BlockedReasons.OrderByDescending(p => p.Value)) text.AppendLine($"| {pair.Key} | {pair.Value} |");
        text.AppendLine().AppendLine("## Структурные группы").AppendLine().AppendLine("| Область | Классификация | Значений | Карт |").AppendLine("|---|---|---:|---:|");
        foreach (var g in report.Rows.GroupBy(r => new { r.Scope, r.Classification }).OrderBy(g => g.Key.Scope).ThenByDescending(g => g.Count()))
            text.AppendLine($"| {g.Key.Scope} | {g.Key.Classification} | {g.Count()} | {g.Select(r => r.CardId).Distinct().Count()} |");
        text.AppendLine().AppendLine("## Частые группы для исследования");
        text.AppendLine("Группы ниже ранжируются по числу карт. Числа карт между группами могут пересекаться. Совпадение структуры — гипотеза для общего правила, не разрешение редактирования.");
        foreach (string scope in new[] { "BlockedSetting", "AdditionalEndpoint" })
        {
            text.AppendLine().AppendLine(scope == "BlockedSetting" ? "### Уже найденные, но заблокированные настройки" : "### Дополнительные узлы вне нынешнего распознавания");
            foreach (var group in report.Groups.Where(g => g.Scope == scope && g.Tier != "NoUnlock").OrderByDescending(g => g.Cards).ThenBy(g => g.Pattern, StringComparer.Ordinal).Take(15))
            {
                text.AppendLine().AppendLine($"#### {group.Key}: {group.Classification}");
                text.AppendLine($"{group.Values} значений на {group.Cards} картах. Направление: {group.Tier}. Причина: {group.Reason}.");
                text.AppendLine().AppendLine("```text").AppendLine(group.Pattern).AppendLine("```");
                foreach (int index in group.SampleRows.Take(3))
                {
                    var row = report.Rows[index]; var card = cards[row.CardId];
                    text.AppendLine($"- {card.Id} {Cell(card.NameRu)} ({Cell(card.NameEn)}): #{row.Id} {Cell(row.Name)}, V={row.Value}; переменные описания: {Cell(string.Join(", ", row.Placeholders))}.");
                }
                text.AppendLine("Проверка: проследить входы/выходы и обновления состояния; определить одну семантику; добавить позитивные и негативные тесты; затем предложить 1–3 игровых примера. До этого параметр скрыт.");
            }
        }
        text.AppendLine().AppendLine("## Что не нужно разблокировать повторно");
        text.AppendLine("AlreadyEditableUpstream: вычисляемое поле получает значение из уже доступной переменной. AliasOutput: служебный выход GetVarNode. Эти поля сами по себе не являются новыми настройками.");
        text.AppendLine().AppendLine("## Файлы и воспроизводимость");
        text.AppendLine("`numeric-audit.json` содержит все строки, локальные ID, прямые связи, узлы ссылок, флаги операций, тексты RU/EN, перечень объявленных способностей и SHA-256 входов/графов. `numeric-audit-summary.json` — компактные итоги.");
        text.AppendLine("Неподдерживаемые карты и карты без собственного CardAbility не включаются в оценки расширения. Частичные описания/отсутствие явных связей не считаются доказательством неиспользуемости.");
        return text.ToString();
    }
}
