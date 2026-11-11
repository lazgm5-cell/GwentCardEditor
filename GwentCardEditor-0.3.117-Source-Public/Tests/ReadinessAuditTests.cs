using System.Text.Json;
using GwentCardEditor;

internal static class ReadinessAuditTests
{
    public static IReadOnlyList<string> Run(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        AbilityMeaningAuditRow Row(int number, string evidence, DescriptionBinding[] bindings, string role = "damage", string? label = null) =>
            new(100, "Тестовая карта", "Test card", false, false, [number], role, label ?? role, "Test", 1, "Deploy", [], bindings,
                new Dictionary<string, string>(), evidence, "Описание", "Description");
        var rows = new[]
        {
            Row(1, "DescriptionLinked", [new("ru-ru", "Direct", ["Damage"]), new("en-us", "Formula", ["Damage"]), new("zh-cn", "MissingText", [])]),
            Row(2, "GraphContext", [new("ru-ru", "Direct", ["Damage"]), new("en-us", "Unlinked", [])]),
            Row(3, "ContextNeedsReview", [new("ru-ru", "Unlinked", [])]),
            Row(4, "ContextNeedsReview", [new("ru-ru", "Direct", [])]),
            Row(5, "UnknownRole", [new("ru-ru", "Direct", ["Damage"])]),
            Row(6, "GraphContext", [new("ru-ru", "MissingText", [])]),
            Row(7, "ContextNeedsReview", [new("ru-ru", "Unlinked", [])], "strengthen", "veteran-strengthen")
        };
        var synthetic = new AbilityMeaningAuditReport("test", DateTime.UtcNow, "test", null, 1, 7, 7,
            new Dictionary<string, int>(), new Dictionary<string, int>(), rows)
            { SemanticLocks = [new(100, "Тестовая карта", 8, "boost", 0, "OrderActivationSignal")] };
        var report = ReadinessAudit.Create(synthetic);
        ReadinessSetting Setting(int id) => report.Settings.Single(r => r.NumberIds.Contains(id));
        Check(Setting(1).Status == "StructurallyLinked" && Setting(1).UnlinkedLanguages.Length == 0, "missing source translation is not treated as broken translation");
        Check(Setting(2).Status == "TextReview" && Setting(2).TextStatus == "PartiallyLinked", "known graph and incomplete text are separate from unknown meaning");
        Check(Setting(3).Status == "ContextReview" && Setting(3).Priority == 1, "plain unexplained effect prioritized for meaning review");
        Check(Setting(4).Status == "StructurallyLinked", "literal quantity binding with no aliases is recognized");
        Check(Setting(5).Status == "ContextReview", "unknown semantic role is not approved merely by matching a variable");
        Check(Setting(6).Status == "MissingDescription", "missing entire description is distinguished");
        Check(Setting(7).Priority == 2, "specialized known label is not equated with an unexplained plain effect");
        Check(report.Statuses.Values.Sum() == 7 && report.Groups.Sum(g => g.Settings) == 7 && report.SemanticLocks == 1, "audit totals do not double count semantic locks");
        Check(report.Groups.All(g => g.Cards == 1), "one card with multiple settings is not counted as multiple cards in a group");
        string checklist = ReadinessAudit.RenderGameplayChecklist(report);
        Check(checklist.Contains("- [ ]") && !checklist.Contains("- [x]", StringComparison.OrdinalIgnoreCase), "gameplay checklist never claims completed game tests");
        Check(checklist.Contains("не обходить блокировку") && checklist.Contains("MyProject"), "gameplay checklist retains compatibility and isolated test guidance");
        Check(ReadinessAudit.RenderQueue(report).Contains("не означает найденную ошибку"), "queue does not classify all review candidates as bugs");
        Check(ReadinessAudit.Create(synthetic with { Rows = [] , Cards = 0, EditableValues = 0, DisplayedSettings = 0 }).Settings.Count == 0, "empty audit supported");
        string target = Path.Combine(scratch, "readiness-report");
        var original = project.ExportAbilityMeaningAudit(target);
        var actual = JsonSerializer.Deserialize<ReadinessReport>(File.ReadAllText(Path.Combine(target, "readiness-summary.json")))!;
        Check(actual.Settings.Count == original.DisplayedSettings && actual.Statuses.Values.Sum() == original.DisplayedSettings, "real audit covers every displayed setting exactly once");
        Check(actual.EditableValues == original.EditableValues && actual.Cards == original.Cards && actual.SemanticLocks == original.SemanticLocks.Count, "real audit totals preserve source meaning report");
        Check(actual.Settings.All(r => original.Rows.Any(o => o.CardId == r.CardId && o.NumberIds.SequenceEqual(r.NumberIds))), "every readiness row traces to an existing editable group");
        Check(File.ReadAllText(Path.Combine(target, "GAMEPLAY_CHECKLIST_RU.md")).Contains("Селективная мутация"), "real gameplay checklist exported");
        Check(File.ReadAllText(Path.Combine(target, "REVIEW_QUEUE_RU.md")).Contains("Группы для разбора"), "grouped review queue exported");
        foreach (string file in new[] { "ability-meanings.json", "needs-review.json", "semantic-locks.json", "REPORT_RU.md" })
            Check(File.Exists(Path.Combine(target, file)), "legacy audit artifact retained " + file);
        Check(!project.HasChanges, "audit never modifies the project");
        project.Client!.Verify();
        return passed;
    }
}
