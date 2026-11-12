using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using GwentCardEditor;

internal static class ExportContinuationTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        void Reject(Action action, string name)
        {
            try { action(); throw new Exception("Expected source protection: " + name); }
            catch (InvalidOperationException e) when (e.Message == "SourceChanged") { passed.Add(name); }
        }
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string client = Path.Combine(scratch, "continuation-client"); Directory.CreateDirectory(client);
        string archive = Path.Combine(client, "data_definitions"); File.Copy(source.Client!.ArchivePath, archive);
        string originalHash = Hash(archive);
        var project = ClientSource.Open(client, Path.Combine(scratch, "continuation-cache"));
        const int id = 112101;
        var initial = project.Card(id);
        var reference = BalanceReference.Load(project.DirectoryPath);
        var balanceCards = reference.Cards.Values.Where(c => c.Type is 2 or 4 or 8 && c.Power >= 0 && c.Provision >= 0)
            .OrderBy(c => c.Id).Take(1900).Select(c => c.Id == id ? c with { Power = c.Power + 2, Provision = c.Provision - 1 } : c).ToArray();
        var snapshot = BalanceSnapshot.Create(balanceCards, DateTimeOffset.UtcNow);
        var projection = new BalanceProjection(reference, snapshot);
        project.ExportBalanceCouncil = true; project.ExportBalanceSnapshot = snapshot;
        project.Apply(id, project.CurrentEdit(id) with { Power = projection.ToStored(initial, "Power", projection.Display(initial).Power + 1) });
        string Export(string name)
        {
            string path = Path.Combine(scratch, "continuation-" + name); project.Export(path); return path;
        }
        string first = Export("first"), firstArchive = Path.Combine(first, "data_definitions");
        Check(Hash(archive) == originalHash, "first export does not install itself");
        File.Copy(firstArchive, archive, true);
        string firstHash = Hash(archive);
        Reject(project.Client!.Verify, "normal client verification still rejects an applied package");
        project.Apply(id, project.CurrentEdit(id) with { Provision = projection.ToStored(project.Card(id), "Provision", projection.Display(initial).Provision + 2),
            Texts = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["en-us"] = new Dictionary<string, string> { ["name"] = "Continuation test" } } });
        string pending = JsonSerializer.Serialize(project.Changes());
        string second = Export("second");
        Check(Hash(archive) == firstHash && project.HasChanges && JsonSerializer.Serialize(project.Changes()) == pending, "continuation export preserves live archive and pending session edits");
        var reopened = ClientSource.Open(second, Path.Combine(scratch, "continuation-reopen"));
        Check(reopened.Card(id).Power == initial.Power + 1 && reopened.Card(id).Provision == initial.Provision + 2, "continuation retains old and new raw stat edits without applying council twice");
        Check(projection.Display(reopened.Card(id)).Power == projection.Display(initial).Power + 1
            && projection.Display(reopened.Card(id)).Provision == projection.Display(initial).Provision + 2, "council display remains in the original coordinate system");
        Check(reopened.Name(id, "en-us") == "Continuation test", "continuation retains localized edits");
        Check(Hash(Path.Combine(second, "_editor", "backup", "data_definitions")) == originalHash, "continuation keeps exact original baseline backup");
        Check(BalanceSnapshot.Load(Path.Combine(second, "_editor", "balance-snapshot.json")).ContentHash == snapshot.ContentHash, "continuation retains the chosen council snapshot");
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(second, "_editor", "card-editor-changes.json"))))
        {
            var root = manifest.RootElement;
            Check(root.GetProperty("SourceArchiveHash").GetString() == originalHash && root.GetProperty("LiveArchiveHashAtExport").GetString() == firstHash,
                "manifest distinguishes baseline from currently applied archive");
            Check(root.GetProperty("AppliedPackageRecognized").GetBoolean() && root.GetProperty("AppliedPackage").GetString() == first, "manifest records byte-exact recognized session output");
        }
        var xml = XDocument.Load(Path.Combine(reopened.DirectoryPath, "Templates.xml"));
        var card = xml.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id);
        card.Element("Power")!.Value = initial.Power.ToString(); card.Element("Provision")!.Value = initial.Provision.ToString();
        Check(XNode.DeepEquals(xml, XDocument.Load(Path.Combine(project.DirectoryPath, "Templates.xml"))), "continuation changes only requested template values");
        File.Copy(Path.Combine(second, "data_definitions"), archive, true);
        string secondHash = Hash(archive);
        project.Apply(id, project.CurrentEdit(id) with { Armor = initial.Armor + 1 });
        string third = Export("third");
        Check(Hash(archive) == secondHash && ClientSource.Open(third, Path.Combine(scratch, "continuation-third-cache")).Card(id).Armor == initial.Armor + 1,
            "second installed session output can also be continued");
        string fourth = Export("without-installing-third");
        Check(Hash(archive) == secondHash && File.Exists(Path.Combine(fourth, "data_definitions")), "export can repeat while an older known output stays installed");
        File.Copy(firstArchive, archive, true);
        Check(File.Exists(Path.Combine(Export("older-output"), "data_definitions")), "restoring an older output from this session is recognized");
        var otherSession = ClientSource.Open(client, Path.Combine(scratch, "other-continuation-cache"));
        otherSession.Apply(id, otherSession.CurrentEdit(id) with { Armor = otherSession.Card(id).Armor + 1 });
        File.Copy(Path.Combine(second, "data_definitions"), archive, true);
        Reject(() => otherSession.Export(Path.Combine(scratch, "other-session-rejected")), "outputs from a different session do not grant continuation permission");
        string foreign = Path.Combine(scratch, "foreign-archive"); File.Copy(archive, foreign);
        using (var zip = ZipFile.Open(foreign, ZipArchiveMode.Update)) { using var stream = zip.CreateEntry("foreign-change.bin").Open(); stream.WriteByte(123); }
        File.Copy(foreign, archive, true);
        pending = JsonSerializer.Serialize(project.Changes());
        string rejected = Path.Combine(scratch, "foreign-continuation-rejected");
        Reject(() => project.Export(rejected), "unrecognized external archive change remains blocked");
        Check(!Directory.Exists(rejected) && JsonSerializer.Serialize(project.Changes()) == pending, "blocked export neither loses edits nor leaves a partial package");
        File.Copy(firstArchive, archive, true);
        File.AppendAllText(Path.Combine(first, "_editor", "backup", "data_definitions"), "tampered");
        Reject(() => project.Export(Path.Combine(scratch, "tampered-baseline-rejected")), "tampered original baseline remains blocked");
        File.Copy(source.Client.ArchivePath, archive, true);
        Check(File.Exists(Path.Combine(Export("restored-original"), "data_definitions")), "restoring the exact original source allows export again");
        File.Copy(Path.Combine(second, "data_definitions"), archive, true);
        File.AppendAllText(Path.Combine(project.DirectoryPath, "Templates.xml"), " ");
        Reject(() => project.Export(Path.Combine(scratch, "tampered-cache-rejected")), "immutable unpacked source checks are not bypassed by continuation");
        Check(Hash(source.Client.ArchivePath) == originalHash, "continuation tests never change the real client or baseline");
        source.Client.Verify();
        return passed;
    }
}
