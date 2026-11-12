using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.IO.Compression;
using System.Xml.Linq;
using GwentCardEditor;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

if (args.Length > 0 && args[0] == "--copy-hash")
{
    using var archive = ZipFile.OpenRead(args[1]);
    using var xml = archive.GetEntry("Abilities.xml")!.Open();
    var sub = new XElement(XDocument.Load(xml).Root!.Elements().Single(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == 30));
    sub.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is "X" or "Y").Remove();
    string canonical = XElement.Parse(sub.ToString(SaveOptions.DisableFormatting)).ToString(SaveOptions.DisableFormatting);
    Console.WriteLine(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    return;
}

if (args.Length > 0 && args[0] == "--meaning-report")
{
    string cache = Path.Combine(Path.GetTempPath(), "GwentMeaningReport-" + Guid.NewGuid().ToString("N"));
    try
    {
        var project = ClientSource.Open(args[1], cache);
        var report = project.ExportAbilityMeaningAudit(args[2]);
        if (report.Rows.Any(r => r.HelpKey == null)) throw new Exception("Missing role explanation");
        if (report.Rows.Any(r => r.Facts.Any(f => f.Kind == "Comparison" && !new[] { "=", "!=", "<", ">", "<=", ">=" }.Contains(f.Value)))) throw new Exception("Unknown comparison");
        if (report.Rows.Any(r => r.Excerpts.Values.Any(e => e.Contains('<') || e.Contains('{')))) throw new Exception("Unclean excerpt");
        var groups = project.NumberGroups(112112);
        if (!groups.Any(g => g.Display.Role == "choices" && g.Members.Count == 2)) throw new Exception("Card choice pair not grouped");
        int previews = 0;
        foreach (var row in report.Rows.GroupBy(r => r.Role).Select(g => g.First()))
        {
            var selected = project.NumberGroups(row.CardId).Single(g => g.Members.Select(n => n.Id).SequenceEqual(row.NumberIds));
            foreach (int next in new[] { selected.Display.Value + 1, selected.Display.Value - 1, selected.Display.Minimum, selected.Display.Maximum }.Distinct()
                .Where(n => n != selected.Display.Value && n >= selected.Display.Minimum && n <= selected.Display.Maximum))
            {
                var edit = project.CurrentEdit(row.CardId) with { Numbers = selected.Members.ToDictionary(n => n.Id, n => next) };
                try
                {
                    if (project.Preview(row.CardId, edit).Count == 0) throw new Exception("Meaning-role edit preview empty: " + row.Role);
                    previews++; break;
                }
                catch (InvalidOperationException e) when (e.Message == "InvalidTargetLimits") { }
            }
        }
        if (project.HasChanges) throw new Exception("Audit previews modified the project");
        var semanticLock = report.SemanticLocks.First(l => l.Reason == "SingleSelectionOnly" && project.Card(l.CardId).Type != 1);
        bool blocked = false;
        try { project.Preview(semanticLock.CardId, project.CurrentEdit(semanticLock.CardId) with { Numbers = new Dictionary<int, int> { [semanticLock.NumberId] = semanticLock.Value + 1 } }); }
        catch (InvalidOperationException e) when (e.Message == "SingleSelectionOnly") { blocked = true; }
        if (!blocked) throw new Exception("First-card-only selection was not blocked by backend");
        foreach (var row in report.Rows.Where(r => r.Role is "choices" or "template-choices" or "row-targets" && r.NumberIds.Length == 2))
        {
            var members = project.Numbers(row.CardId).Where(n => row.NumberIds.Contains(n.Id)).ToArray();
            if (members.Length != 2 || members[0].Value != members[1].Value) throw new Exception("Unsafe selection pair");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { report.Cards, report.EditableValues, report.DisplayedSettings, report.Evidence, Roles=report.Roles.Count, PreviewedRoles=previews }));
    }
    finally { if (Directory.Exists(cache)) Directory.Delete(cache, true); }
    return;
}

if (args.Length > 0 && args[0] == "--audit")
{
    string cache = Path.Combine(Path.GetTempPath(), "GwentMeaningAudit-" + Guid.NewGuid().ToString("N"));
    try
    {
        var project = ClientSource.Open(args[1], cache);
        var graphs = XDocument.Load(Path.Combine(project.DirectoryPath, "Abilities.xml")).Root!.Elements()
            .Where(e => (string?)e.Attribute("Type") == "CardAbility").ToDictionary(e => (int)e.Attribute("Id")!);
        var rows = project.Cards.Where(c => c.Editable).SelectMany(card => project.Numbers(card.Id).Where(n => n.Editable).Select(number =>
        {
            var graph = graphs.GetValueOrDefault(card.Id);
            var element = graph?.Descendants().FirstOrDefault(e => (int?)e.Attribute("Id") == number.Id);
            var sources = (graph?.Element("Nodes")?.Elements().Where(n => (string?)n.Attribute("Type") == "GetVarNode" && (int?)n.Attribute("VarId") == number.Id)
                .Select(n => (int?)n.Element("Out")?.Attribute("Id") ?? -1) ?? []).Append(number.Id).ToHashSet();
            var destinations = graph?.Element("Connections")?.Elements().Where(c => sources.Contains((int?)c.Attribute("SrcId") ?? -1))
                .Select(c => (int?)c.Attribute("DstId") ?? -1).ToHashSet() ?? [];
            var endpoints = graph?.Descendants().Where(e => destinations.Contains((int?)e.Attribute("Id") ?? -1)).ToArray() ?? [];
            string Endpoint(XElement e) => (string?)e.Parent?.Attribute("Type") + "." + e.Name.LocalName;
            return new { CardId=card.Id, card.DebugName, Name=project.Name(card.Id,"en-us"), number.Id, number.Role, Variable=number.Name, number.Value,
                number.LabelRole, number.HelpKey, number.Context, Location=element == null ? "Template" : Endpoint(element),
                Consumers=endpoints.Select(Endpoint).ToArray(), Tooltip=project.Text(card.Id,"en-us","tooltip"),
                Bindings=project.DescriptionBindings(card.Id,[number.Id]) };
        })).ToArray();
        File.WriteAllText(args[2], JsonSerializer.Serialize(rows, new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"{rows.Length} editable values across {rows.Select(r=>r.CardId).Distinct().Count()} cards");
        foreach(var g in rows.GroupBy(r=>r.Role).OrderByDescending(g=>g.Count()))
            Console.WriteLine($"{g.Key}: {g.Count()}, linked {g.Count(r=>r.Bindings.Any(b=>b.Linked))}, help {g.Count(r=>r.HelpKey!=null)}; {string.Join(" / ",g.Take(3).Select(r=>r.Name+":"+r.Value))}");
    }
    finally { if (Directory.Exists(cache)) Directory.Delete(cache, true); }
    return;
}

if (args.Length == 0 || args[0] != "--enums")
{
    using var archive = ZipFile.OpenRead(args[0]);
    using var xml = archive.GetEntry("Abilities.xml")!.Open();
    var abilities = XDocument.Load(xml).Root!;
    XElement Card(int id) => abilities.Elements().Single(e => (int?)e.Attribute("Id") == id);
    int passed = 0;
    void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
    var ram = Card(200049);
    var rank = AbilityNumbers.Inspect(ram, false, "{Damage}").Single(n => n.Id == 746);
    Check(rank.Editable && rank.LabelRole == "highest-power-levels" && rank.HelpKey == "HighestLevels", "Ram is rank selection, not a threshold");
    Check(rank.Value == 1 && rank.Role == "power-level", "Stored role and value unchanged");
    Check(rank.Context == "Order", "Order context traced through subability flow mappings");
    for (int mode = 0; mode < 4; mode++)
    {
        var clone = new XElement(ram);
        clone.Descendants().Single(e => (int?)e.Attribute("Id") == 745).SetAttributeValue("V", mode);
        var value = AbilityNumbers.Inspect(clone, false, "{Damage}").Single(n => n.Id == 746);
        Check(value.LabelRole == new[] { "highest-power-count", "highest-power-levels", "lowest-power-count", "lowest-power-levels" }[mode], "Power filter mode " + mode);
    }
    var ranger = Card(202828);
    var rows = AbilityNumbers.Inspect(ranger, false, "{boost}").Where(n => n.Id is 318 or 319).ToArray();
    Check(rows.Length == 2 && rows.All(n => !n.Editable && n.Reason == "SingleRowSelection"), "Ranger row limits blocked");
    Check(AbilityNumbers.Inspect(ranger, false, "{boost}").Where(n => n.Id is not (318 or 319)).All(n => n.Reason != "SingleRowSelection"), "Other Ranger settings are not locked by row policy");
    bool rejected = false;
    try { AbilityNumbers.Prepare(new XElement("Template"), ranger, new Dictionary<int, int> { [319] = 2 }, false, "{boost}"); }
    catch (InvalidOperationException e) when (e.Message == "SingleRowSelection") { rejected = true; }
    Check(rejected, "Unsafe row edit rejected by backend");
    var geralt = Card(112103);
    var threshold = AbilityNumbers.Inspect(geralt, false, "{power}").Single(n => n.Editable && n.Role == "power-limit");
    Check(threshold.MeaningFacts.Contains(new AbilityMeaningFact("Comparison", ">=")), "Geralt comparison direction");
    foreach (var (mode, symbol) in new[] { (0,"="), (1,"!="), (2,"<"), (3,">"), (4,"<="), (5,">=") })
    {
        var clone = new XElement(geralt);
        clone.Descendants().Single(e => (int?)e.Attribute("Id") == 106).SetAttributeValue("V", mode);
        var value = AbilityNumbers.Inspect(clone, false, "{power}").Single(n => n.Id == threshold.Id);
        Check(value.MeaningFacts.Contains(new AbilityMeaningFact("Comparison", symbol)), "Comparison " + symbol);
    }
    var dynamicComparison = new XElement(geralt);
    dynamicComparison.Descendants().Single(e => (int?)e.Attribute("Id") == 106).SetAttributeValue("ExternalIn", "True");
    Check(!AbilityNumbers.Inspect(dynamicComparison, false, "{power}").Single(n => n.Id == threshold.Id).MeaningFacts.Any(f => f.Kind == "Comparison"), "Dynamic comparison not guessed");
    Check(AbilityNumbers.Inspect(geralt, false, "{power}").Where(n => n.Editable && n.Role is "min-targets" or "max-targets")
        .All(n => n.MeaningFacts.Contains(new AbilityMeaningFact("Action", "Destroy"))), "Geralt selection linked to destruction");
    Console.WriteLine($"PASS: {passed} number meaning checks");
    return;
}

using var stream = File.OpenRead(args[1]);
using var pe = new PEReader(stream);
var reader = pe.GetMetadataReader();
foreach (var handle in reader.TypeDefinitions)
{
    var type = reader.GetTypeDefinition(handle);
    if (!new[] { "EPowerFilter", "ECompareOp", "EArmorOperation", "ETapChargesOperation", "ELocation", "EPlayerId", "ECardType", "ESpawnType", "ESpawnLocation", "ECardPositionType" }.Contains(reader.GetString(type.Name))) continue;
    Console.WriteLine(reader.GetString(type.Namespace) + "." + reader.GetString(type.Name));
    foreach (var fieldHandle in type.GetFields())
    {
        var field = reader.GetFieldDefinition(fieldHandle);
        var constant = field.GetDefaultValue();
        if (constant.IsNil) continue;
        var blob = reader.GetBlobReader(reader.GetConstant(constant).Value);
        Console.WriteLine(reader.GetString(field.Name) + "=" + blob.ReadConstant(reader.GetConstant(constant).TypeCode));
    }
}
