using System.Text;
using System.Text.Json;

namespace GwentCardEditor;

public sealed record AbilityMeaningAuditRow(int CardId, string NameRu, string NameEn, bool Leader, bool Token,
    int[] NumberIds, string Role, string LabelRole, string? HelpKey, int Value, string Context,
    IReadOnlyList<AbilityMeaningFact> Facts, IReadOnlyList<DescriptionBinding> Bindings,
    IReadOnlyDictionary<string, string> Excerpts, string Evidence, string TooltipRu, string TooltipEn);
public sealed record AbilityMeaningLock(int CardId, string Name, int NumberId, string Role, int Value, string Reason);
public sealed record AbilityMeaningAuditReport(string Version, DateTime CreatedUtc, string Source, string? ArchiveHash,
    int Cards, int EditableValues, int DisplayedSettings, IReadOnlyDictionary<string, int> Evidence,
    IReadOnlyDictionary<string, int> Roles, IReadOnlyList<AbilityMeaningAuditRow> Rows)
{
    public IReadOnlyList<AbilityMeaningLock> SemanticLocks { get; init; } = [];
}

public sealed partial class EditorProject
{
    public AbilityMeaningAuditReport CreateAbilityMeaningAudit()
    {
        if (HasChanges) throw new InvalidOperationException("AuditRequiresCleanProject");
        VerifyAuditSource();
        var rows = new List<AbilityMeaningAuditRow>();
        var locks = new List<AbilityMeaningLock>();
        foreach (var card in Cards.Where(c => c.Editable).OrderBy(c => c.Id))
        {
            locks.AddRange(Numbers(card.Id).Where(n => !n.Editable && n.Reason is "SingleSelectionOnly" or "SingleRowSelection" or "SurvivalCheck"
                or "OrderActivationSignal" or "ImmunityArmorBookkeeping" or "SelectionColorAnchor" or "PowerExtremumSelection" or "ImplicitPowerCondition"
                or "SingleStrengthSource" or "UnusedOrderCondition" or "ImplicitChargeCondition" or "SelfRevealPresentation" or "ChargeActivationSignal" or "ArmorConversionReset")
                .Select(n => new AbilityMeaningLock(card.Id, Name(card.Id, "ru-ru"), n.Id, n.Role, n.Value, n.Reason)));
            foreach (var group in NumberGroups(card.Id))
            {
                var number = group.Display;
                var bindings = DescriptionBindings(card.Id, group.Members.Select(n => n.Id));
                var excerpts = bindings.Where(b => b.Language is "ru-ru" or "en-us" or "zh-cn")
                    .ToDictionary(b => b.Language, b => DescriptionExcerpt(card.Id, b));
                string? help = number.HelpKey ?? AbilityMeaningVocabulary.Help(number.Role);
                bool copyContext = number.Role != "copy-count" || (number.MeaningFacts.Any(f => f.Kind is "CopySource" or "Template")
                    && (number.MeaningFacts.Any(f => f.Kind == "CopyRelativePlace")
                        || number.MeaningFacts.Any(f => f.Kind == "CopyPlace") && number.MeaningFacts.Any(f => f.Kind == "CopyPlayer")));
                string evidence = help == null ? "UnknownRole" : excerpts.Values.Any(s => s.Length > 0) ? "DescriptionLinked"
                    : number.MeaningFacts.Count > 0 && copyContext ? "GraphContext"
                    : number.Role is "initial-charges" or "initial-patience" or "initial-timer" or "cooldown" or "cooldown-reduction" or "fee-cost"
                        or "tribute-cost" or "profit" or "adrenaline-threshold" or "bloodthirst" or "berserk-threshold" ? "IntrinsicSetting"
                    : "ContextNeedsReview";
                rows.Add(new(card.Id, Name(card.Id, "ru-ru"), Name(card.Id, "en-us"), card.Type == 1, card.IsToken,
                    group.Members.Select(n => n.Id).ToArray(), number.Role, number.LabelRole ?? number.Role, help,
                    number.DisplayValue, number.Context, number.MeaningFacts, bindings, excerpts, evidence,
                    Text(card.Id, "ru-ru", "tooltip"), Text(card.Id, "en-us", "tooltip")));
            }
        }
        VerifyAuditSource();
        return new("1.0", DateTime.UtcNow, SourcePath, Client?.ArchiveHash, rows.Select(r => r.CardId).Distinct().Count(),
            rows.Sum(r => r.NumberIds.Length), rows.Count, rows.GroupBy(r => r.Evidence).ToDictionary(g => g.Key, g => g.Count()),
            rows.GroupBy(r => r.Role).ToDictionary(g => g.Key, g => g.Count()), rows) { SemanticLocks = locks };
    }

    public AbilityMeaningAuditReport ExportAbilityMeaningAudit(string target)
    {
        string destination = Path.GetFullPath(target);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new InvalidOperationException("ExportExists");
        string relative = Path.GetRelativePath(DirectoryPath, destination);
        if (relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relative))
            throw new InvalidOperationException("ExportInsideSource");
        var report = CreateAbilityMeaningAudit();
        string parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".meaning-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(Path.Combine(staging, "ability-meanings.json"), JsonSerializer.Serialize(report, options), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "needs-review.json"), JsonSerializer.Serialize(report.Rows.Where(r => r.Evidence is "ContextNeedsReview" or "UnknownRole"), options), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "semantic-locks.json"), JsonSerializer.Serialize(report.SemanticLocks, options), new UTF8Encoding(false));
            var md = new StringBuilder("# Аудит смысла изменяемых параметров\n\n");
            md.AppendLine($"Источник: `{report.Source}`\n\nКарт: {report.Cards}; изменяемых значений: {report.EditableValues}; полей после объединения пар: {report.DisplayedSettings}.\n");
            md.AppendLine("Это структурный аудит, не проверка всех карт в игре. Связь с переменной текста не доказывает, что все условия ветки описаны. Строки ContextNeedsReview требуют дальнейшего разбора; они не выдаются за полностью подтверждённые. Способности и файлы игры не меняются.\n");
            foreach (var item in report.Evidence.OrderBy(p => p.Key)) md.AppendLine($"- {item.Key}: {item.Value}");
            md.AppendLine("\n## Типы настроек\n\n| Тип | Полей | Без полного контекста |\n|---|---:|---:|");
            foreach (var group in report.Rows.GroupBy(r => r.Role).OrderBy(g => g.Key))
                md.AppendLine($"| {group.Key} | {group.Count()} | {group.Count(r => r.Evidence is "ContextNeedsReview" or "UnknownRole")} |");
            md.AppendLine("\n## Требуют уточнения контекста\n\nID поля оставлены только в техническом отчёте, не в интерфейсе. Подробности, тексты карт и признаки графа — в JSON.\n");
            foreach (var group in report.Rows.Where(r => r.Evidence is "ContextNeedsReview" or "UnknownRole").GroupBy(r => r.CardId))
                md.AppendLine($"- {group.Key}: {group.First().NameRu} — {string.Join(", ", group.Select(r => r.Role).Distinct())}");
            File.WriteAllText(Path.Combine(staging, "REPORT_RU.md"), md.ToString(), new UTF8Encoding(false));
            var readiness = ReadinessAudit.Create(report);
            File.WriteAllText(Path.Combine(staging, "readiness-summary.json"), JsonSerializer.Serialize(readiness, options), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "REVIEW_QUEUE_RU.md"), ReadinessAudit.RenderQueue(readiness), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "GAMEPLAY_CHECKLIST_RU.md"), ReadinessAudit.RenderGameplayChecklist(readiness), new UTF8Encoding(false));
            VerifyAuditSource();
            Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        return report;
    }
}
