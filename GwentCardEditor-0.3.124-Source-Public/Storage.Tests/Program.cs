using GwentCardEditor;

string root = Path.Combine(Path.GetTempPath(), "StorageTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
try
{
    string exe = Path.Combine(root, "publish-0.3.54");
    Directory.CreateDirectory(exe);
    Check(PackageStorage.FindProgramRoot(exe) == exe, "Standalone root");
    File.WriteAllText(Path.Combine(root, "CardEditor.cmd"), "test");
    Check(PackageStorage.FindProgramRoot(exe) == root, "Launcher root");
    Check(PackageStorage.FindProgramRoot(Path.Combine(root, "publish-0.3.55")) == root, "Version-independent root");
    string client = Path.Combine(root, "GWENT");
    string assets = Path.Combine(client, "Gwent_Data", "StreamingAssets");
    Directory.CreateDirectory(assets);
    string archive = Path.Combine(assets, "data_definitions");
    File.WriteAllText(archive, "test");
    string folder = PackageStorage.FolderFor(root, client);
    Check(folder == PackageStorage.FolderFor(root, assets), "Assets canonicalization");
    Check(folder == PackageStorage.FolderFor(root, archive), "Archive canonicalization");
    Check(folder != PackageStorage.FolderFor(root, client + "_Offline"), "Client isolation");
    Check(PackageStorage.Safe("Test: \"A\"/B?*") == "Test___A__B", "Filename sanitization");
    Check(PackageStorage.Safe("Карты 中文") == "Карты_中文", "Unicode labels");
    Directory.CreateDirectory(folder);
    DateTime time = new(2026, 10, 25, 15, 0, 0);
    string first = PackageStorage.NewPath(folder, "Art", "203125", time: time);
    Check(Path.GetFileName(first) == "20261025-150000__Art-203125", "Readable name");
    Directory.CreateDirectory(first);
    string second = PackageStorage.NewPath(folder, "Art", "203125", time: time);
    Check(second == first + "-02", "Directory collision");
    File.WriteAllText(second, "preserve");
    Check(PackageStorage.NewPath(folder, "Art", "203125", time: time) == first + "-03", "File collision");
    string installed = @"F:\GWH\Proj\AbilityEditor_0.29.5_MyProject_0.28.74_ThronebreakerExpansion\CardEditor";
    string longFolder = PackageStorage.FolderFor(installed, @"G:\Games\GOG Galaxy\Games\Gwent");
    string bounded = PackageStorage.NewPath(longFolder, "АнимРамки", new string('x', 100));
    Check(bounded.Length + 1 + 104 <= 258, "Payload path budget");
    bool rejected = false;
    try { PackageStorage.FolderFor(installed + new string('x', 150), client); }
    catch (InvalidOperationException e) when (e.Message == "BackupPathLong") { rejected = true; }
    Check(rejected, "Long root rejection");
    PackageStorage.Describe(first, client, "Art", "Card 203125");
    string info = File.ReadAllText(Path.Combine(first, "PackageInfo.txt"));
    Check(info.Contains(client) && info.Contains("Card 203125"), "Package summary");
    PackageStorage.Describe(first, client, "Other");
    Check(File.ReadAllText(Path.Combine(first, "PackageInfo.txt")) == info, "Summary preservation");
    Console.WriteLine($"PASS: {passed} storage checks");
}
finally { Directory.Delete(root, true); }

namespace GwentCardEditor { internal static class UiText { public static string Get(string key) => key; } }
