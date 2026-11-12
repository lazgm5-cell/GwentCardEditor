using System.Text.Json;
using System.Xml.Linq;
using GwentCardEditor;

internal static class BalanceTests
{
    public static IReadOnlyList<string> Run(string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        void Reject(Action action, string name)
        {
            try { action(); } catch (Exception e) when (e is InvalidOperationException or JsonException or KeyNotFoundException or FormatException) { passed.Add(name); return; }
            throw new Exception("Expected rejection: " + name);
        }
        var cards = Enumerable.Range(1, 1001).Select(id => new BalanceCard(id, 4, 4, 6)).ToArray();
        var snapshot = BalanceSnapshot.Create(cards.Reverse(), DateTimeOffset.UtcNow);
        Check(snapshot.Cards.First().Id == 1 && snapshot.ContentHash.Length == 64, "balance snapshots have canonical ordering and checksum");
        string file = Path.Combine(scratch, "balance.json");
        snapshot.Save(file);
        Check(BalanceSnapshot.Load(file).ContentHash == snapshot.ContentHash, "balance snapshot portable offline round-trip");
        Reject(() => (snapshot with { ContentHash = "broken" }).Validate(), "balance checksum damage rejected");
        Reject(() => (snapshot with { Source = "https://example.org" }).Validate(), "unknown balance origin rejected");
        Reject(() => (snapshot with { Schema = 2 }).Validate(), "unsupported balance schema rejected");
        Reject(() => BalanceSnapshot.Create(cards.Take(999), DateTimeOffset.UtcNow), "partial official result rejected");
        Reject(() => BalanceSnapshot.Create(cards.Append(cards[0]), DateTimeOffset.UtcNow), "duplicate balance IDs rejected");
        Reject(() => BalanceSnapshot.Create(cards.Select(c => c.Id == 1 ? c with { Power = -1 } : c), DateTimeOffset.UtcNow), "negative balance stats rejected");
        Reject(() => (snapshot with { CapturedUtc = DateTimeOffset.UtcNow.AddDays(2) }).Validate(), "future snapshot date rejected");
        var rows = cards.Select(c => new { _id = c.Id.ToString(), _source = new { id = c.Id, type = c.Type, power = c.Power, provisions_cost = c.Provision } });
        Check(BalanceSnapshot.ParseOfficial(JsonSerializer.Serialize(rows), snapshot.CapturedUtc).ContentHash == snapshot.ContentHash, "official website search schema parsed");
        Reject(() => BalanceSnapshot.ParseOfficial("<html>error</html>", DateTimeOffset.UtcNow), "website HTML failure rejected");
        Reject(() => BalanceSnapshot.ParseOfficial("[]", DateTimeOffset.UtcNow), "empty website response rejected");
        Reject(() => BalanceReference.Load(Path.Combine(scratch, "source")), "modified or unknown reference base rejected");
        return passed;
    }

    public static IReadOnlyList<string> Real(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var reference = BalanceReference.Load(project.DirectoryPath);
        // Ensure coverage and the selected real card without relying on dictionary order.
        var cards = reference.Cards.Values.Where(c => c.Type is 2 or 4 or 8 && c.Power >= 0 && c.Provision >= 0).OrderBy(c => c.Id).Take(1900).ToArray();
        var ciri = cards.Single(c => c.Id == 112101);
        cards = cards.Select(c => c.Id == ciri.Id ? c with { Power = c.Power + 2, Provision = c.Provision - 1 } : c).ToArray();
        var snapshot = BalanceSnapshot.Create(cards, DateTimeOffset.UtcNow);
        var projection = new BalanceProjection(reference, snapshot);
        var local = project.Card(ciri.Id);
        var shown = projection.Display(local);
        var sorted = CardCatalog.Query(project, local.Faction, "en-us", sort: CardSort.Power, projection: projection.Display).Select(c => c.Power).ToArray();
        Check(sorted.SequenceEqual(sorted.Order()), "catalog sorts displayed balance values rather than local numbers");
        Check(shown.Power == local.Power + 2 && shown.Provision == local.Provision - 1, "accumulated balance shown over local stats");
        Check(projection.ToStored(local, "Power", shown.Power) == local.Power
            && projection.ToStored(local, "Provision", shown.Provision) == local.Provision, "balance no-op does not rewrite either local stat");
        Check(projection.Display(local with { Power = local.Power + 1 }).Power == shown.Power + 1, "existing local edits are retained on top of snapshot reference");
        Check(projection.ToStored(local with { Power = 1000 }, "Power", 1002) == 1000, "unchanged legacy value outside editing range is preserved");
        Check(projection.Display(local with { Id = 999999 }).Power == -1, "missing card is not silently shown with local stats");
        Check(!projection.TryDelta(local with { Type = 8 }, out _), "balance type mismatch blocked");
        Check(!projection.TryDelta(local with { IsToken = true }, out _), "token balance projection blocked");
        try { projection.ToStored(local, "Power", 1); throw new Exception("Unsafe negative XML value accepted"); }
        catch (InvalidOperationException e) when (e.Message == "BalanceRange") { passed.Add("unrepresentable final value blocked"); }
        int desiredPower = shown.Power + 1, desiredProvision = shown.Provision + 1;
        project.Apply(local.Id, project.CurrentEdit(local.Id) with {
            Power = projection.ToStored(local, "Power", desiredPower), Provision = projection.ToStored(local, "Provision", desiredProvision) });
        project.ExportBalanceCouncil = true; project.ExportBalanceSnapshot = snapshot;
        string output = Path.Combine(scratch, "real-balance-export");
        project.Export(output);
        var result = ClientSource.Open(output, Path.Combine(scratch, "real-balance-cache"));
        Check(result.Card(local.Id).Power == local.Power + 1 && result.Card(local.Id).Provision == local.Provision + 1, "balance export stores compensated XML values, not website values");
        Check(projection.Display(result.Card(local.Id)).Power == desiredPower && projection.Display(result.Card(local.Id)).Provision == desiredProvision, "reopened modified client does not apply the snapshot twice");
        string support = project.Client == null ? output : Path.Combine(output, "_editor");
        Check(BalanceSnapshot.Load(Path.Combine(support, "balance-snapshot.json")).ContentHash == snapshot.ContentHash, "export carries portable balance snapshot");
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(support, "card-editor-changes.json"))))
            Check(manifest.RootElement.GetProperty("Balance").GetProperty("Mode").GetString() == "Council", "export records balance coordinate system");
        var xml = XDocument.Load(Path.Combine(result.DirectoryPath, "Templates.xml"));
        var card = xml.Root!.Elements().Single(e => (int?)e.Attribute("Id") == local.Id);
        card.Element("Power")!.Value = local.Power.ToString(); card.Element("Provision")!.Value = local.Provision.ToString();
        Check(XNode.DeepEquals(xml, XDocument.Load(Path.Combine(project.DirectoryPath, "Templates.xml"))), "balance export changes only the requested two XML fields");
        foreach (string name in EditorProject.Languages.Select(l => l + ".csv").Append("Abilities.xml"))
            if (File.Exists(Path.Combine(project.DirectoryPath, name)))
                Check(File.ReadAllBytes(Path.Combine(project.DirectoryPath, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(result.DirectoryPath, name))), "balance export preserves " + name);
        project.Undo(); project.ExportBalanceCouncil = false; project.ExportBalanceSnapshot = null;
        Check(!project.HasChanges, "balance edit undo exact");
        return passed;
    }

    public static IReadOnlyList<string> Live(EditorProject project, string scratch, string path)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var snapshot = BalanceSnapshot.Load(path);
        var projection = new BalanceProjection(BalanceReference.Load(project.DirectoryPath), snapshot);
        var matched = project.Cards.Where(c => c.Editable && projection.TryDelta(c, out _)).ToArray();
        Check(matched.Length > 1000, "official snapshot compatible editable cards: " + matched.Length);
        Check(matched.All(c => projection.ToStored(c, "Provision", projection.Display(c).Provision) == c.Provision
            && (c.Type != 4 || projection.ToStored(c, "Power", projection.Display(c).Power) == c.Power)), "official snapshot no-op round-trip for every compatible card");
        var ciri = project.Card(112101);
        var officialCiri = snapshot.Cards.Single(c => c.Id == ciri.Id);
        Check(projection.Display(ciri).Power == officialCiri.Power && projection.Display(ciri).Provision == officialCiri.Provision,
            "official Ciri stats are displayed over verified original base");
        project.Apply(ciri.Id, project.CurrentEdit(ciri.Id) with { Power = projection.ToStored(ciri, "Power", officialCiri.Power + 1) });
        project.ExportBalanceCouncil = true; project.ExportBalanceSnapshot = snapshot;
        string output = Path.Combine(scratch, "live-balance-export");
        project.Export(output);
        var reopened = ClientSource.Open(output, Path.Combine(scratch, "live-balance-cache"));
        Check(reopened.Card(ciri.Id).Power == ciri.Power + 1 && projection.Display(reopened.Card(ciri.Id)).Power == officialCiri.Power + 1,
            "downloaded official snapshot compensated export round-trip");
        project.Undo(); project.ExportBalanceCouncil = false; project.ExportBalanceSnapshot = null;
        Check(!project.HasChanges, "downloaded official snapshot edit undo exact");
        return passed;
    }
}
