using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using GwentCardEditor;

internal static class AuditTests
{
    public static IReadOnlyList<string> Run(string scratch)
    {
        var passed = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed.Add(message); }
        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (InvalidOperationException e) when (e.Message == reason) { passed.Add("audit reject " + reason); return; }
            throw new Exception("Expected " + reason);
        }
        string source = Path.Combine(scratch, "numbers-source");
        var hashes = Directory.GetFiles(source).ToDictionary(p => Path.GetFileName(p)!, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        var project = new EditorProject(source);
        var before = project.Numbers(11).ToArray();
        AuditReport report = project.CreateNumericAudit();
        Check(!project.HasChanges && !project.CanUndo, "audit leaves project and history unchanged");
        Check(project.Numbers(11).SequenceEqual(before), "audit does not unlock values");
        Check(report.Cards.All(c => c.Id != 17 && c.Id != 18), "audit excludes tutorial cards from research scope");
        Check(report.Rows.Any(r => r.CardId == 11 && r.Id == 134 && r.Classification == "AlreadyEditableUpstream" && r.EditableSources.SequenceEqual(new[] { 200 })), "audit recognizes an already-editable source");
        Check(report.Rows.Any(r => r.Id == 141 && r.Classification == "AliasOutput" && r.Scope == "AdditionalEndpoint"), "audit inventories previously omitted getter output separately");
        Check(report.Rows.Single(r => r.Id == 134).Incoming.Single().VariableReference == 200, "audit records variable references in connections");
        Check(report.Groups.Sum(g => g.Values) == report.Rows.Length, "audit groups cover every row exactly once");
        Check(report.BlockedReasons.Values.Sum() == report.Rows.Count(r => r.Scope == "BlockedSetting"), "audit blocked counts match current inspector");
        Check(report.Groups.All(g => g.SampleRows.All(i => i >= 0 && i < report.Rows.Length)), "audit samples reference valid rows");
        Check(project.CreateNumericAudit().Groups.Select(g => g.Key).SequenceEqual(report.Groups.Select(g => g.Key)), "audit pattern keys are deterministic");
        string output = Path.Combine(scratch, "audit-output");
        project.ExportNumericAudit(output);
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "numeric-audit.json"))))
            Check(json.RootElement.GetProperty("Rows").GetArrayLength() == report.Rows.Length, "audit JSON round-trip");
        Check(File.ReadAllText(Path.Combine(output, "REPORT_RU.md")).Contains("не разблокирует"), "audit report states read-only boundary");
        foreach (var (name, hash) in hashes)
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, name)))) == hash, "audit preserves source " + name);
        Reject(() => project.ExportNumericAudit(output), "ExportExists");
        Reject(() => project.ExportNumericAudit(Path.Combine(source, "inside")), "ExportInsideSource");
        project.Apply(11, project.CurrentEdit(11) with { Numbers = new Dictionary<int, int> { [200] = 4 } });
        Reject(() => project.CreateNumericAudit(), "AuditRequiresCleanProject");
        project.Undo();
        string remapped = Path.Combine(scratch, "audit-remapped");
        Directory.CreateDirectory(remapped);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(remapped, Path.GetFileName(file)));
        var graph = XDocument.Load(Path.Combine(remapped, "Abilities.xml"));
        foreach (var attribute in graph.Root!.Elements("Ability").SelectMany(a => a.Descendants()).Attributes()
            .Where(a => a.Name.LocalName is "Id" or "SrcId" or "DstId" or "VarId"))
            if (int.TryParse(attribute.Value, out int id)) attribute.Value = (id + 2000).ToString();
        graph.Save(Path.Combine(remapped, "Abilities.xml"));
        var remappedReport = new EditorProject(remapped).CreateNumericAudit();
        Check(remappedReport.Groups.Select(g => g.Key).Order().SequenceEqual(report.Groups.Select(g => g.Key).Order()), "audit grouping ignores local graph IDs");
        File.AppendAllText(Path.Combine(source, "Abilities.xml"), " ");
        Reject(() => project.CreateNumericAudit(), "SourceChanged");
        return passed;
    }
}
