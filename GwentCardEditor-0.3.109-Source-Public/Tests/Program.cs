using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.IO.Compression;
using GwentCardEditor;

string scratch = Path.Combine(Path.GetTempPath(), "GwentCardEditorTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
var passed = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    passed.Add(name);
}
void Reject(Action action, string reason)
{
    try { action(); }
    catch (InvalidOperationException e) when (e.Message == reason) { passed.Add("reject " + reason); return; }
    throw new Exception("Expected rejection: " + reason);
}
string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
try
{
    if (args.Contains("--export-continuation-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(ExportContinuationTests.Run(input, scratch));
        File.WriteAllText(args[1], JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {passed.Count} export continuation checks.");
        return;
    }
    if (args.Contains("--readiness-audit-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(ReadinessAuditTests.Run(input, scratch));
        File.WriteAllText(args[1], JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {passed.Count} readiness audit checks.");
        return;
    }
    if (args.Contains("--selected-draw-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(SelectedDrawTextTests.Run(input, scratch));
        File.WriteAllText(args[1], JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {passed.Count} selected draw checks.");
        return;
    }
    if (args.Contains("--discard-text-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(DiscardCountTextTests.Run(input, scratch));
        File.WriteAllText(args[1], JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {passed.Count} discard text checks.");
        return;
    }
    if (args.Contains("--move-text-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(MoveCountTextTests.Run(input, scratch));
        File.WriteAllText(args[1], JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {passed.Count} move text checks.");
        return;
    }
    if (args.Contains("--movement-text-audit", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        CardMovementTextAudit.Write(input, args[1]);
        return;
    }
    if (args.Contains("--draw-text-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(DrawCountTextTests.Run(input, scratch));
        File.WriteAllText(args[1], JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {passed.Count} draw text checks.");
        return;
    }
    if(args.Contains("--ban-ard-description-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(BanArdDescriptionTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--target-text-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(TargetCountTextTests.Run(input,scratch));passed.AddRange(TargetCountPowerTextTests.Run(input,scratch));passed.AddRange(TargetStatusTextTests.Run(input,scratch));passed.AddRange(LeaderTests.Real(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--insanity-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(InsanityTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--catalog-tokens-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(CatalogTokenTests.Run(input));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--flanking-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(FlankingTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--echo-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(EchoTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--resupply-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(ResupplyTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--syndicate-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(SyndicateEditingTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--review-batch-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(ReviewBatchTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--leader-copy-catalog-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(LeaderCopyCatalogTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--card-clarity-revision-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(CardClarityRevisionTests.Run(input,scratch));passed.AddRange(GigascorpionTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--native-keywords-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(NativeKeywordTests.Run(input,scratch));
        passed.AddRange(NativeKeywordSourceTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--additional-values-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(AdditionalValueTests.Run(input,scratch));
        passed.AddRange(AssimilateTests.Run(input,scratch));passed.AddRange(HarmonyTests.Run(input,scratch));
        passed.AddRange(IntimidateTests.Run(input,scratch));passed.AddRange(ThriveTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--formation-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(FormationTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--veteran-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(VeteranTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--thrive-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(ThriveTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--intimidate-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(IntimidateTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--harmony-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(HarmonyTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--assimilate-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));passed.AddRange(AssimilateTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if(args.Contains("--additional-only",StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));
        passed.AddRange(AdditionalAbilityTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if (args.Contains("--gord-only", StringComparer.Ordinal))
    {
        var input=ClientSource.Open(args[0],Path.Combine(scratch,"client-cache"));
        passed.AddRange(GordTests.Run(input,scratch));
        string report=JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Tests=passed},new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(args[1],report);Console.WriteLine(report);return;
    }
    if (args.Contains("--choice-effects-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(ChoiceEffectTests.Run(input, scratch));
        string report = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(args[1], report); Console.WriteLine(report); return;
    }
    if (args.Contains("--verified-effects-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(VerifiedEffectTests.Run(input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused); return;
    }
    if (args.Contains("--charge-clarity-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(ChargeClarityTests.Run(input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused); return;
    }
    if (args.Contains("--patience-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(InitialPatienceTests.Run(input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused); return;
    }
    if (args.Contains("--clarity-batch-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(AbilityClarityBatchTests.Run(input, scratch));
        passed.AddRange(AlternativeSelectionClarityTests.Run(input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused); return;
    }
    if (args.Contains("--clarity-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(AbilityClarityTests.Run(input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused); return;
    }
    if (args.Contains("--catalog-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(CatalogVisibilityTests.Run(input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused);
        return;
    }
    if (args.Contains("--copy-meanings-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(CopyMeaningTests.Run(input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused);
        return;
    }
    passed.AddRange(ArtCropTests.Run());
    if (args.Contains("--art-only", StringComparer.Ordinal))
    {
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused);
        return;
    }
    if (args.Contains("--power-only", StringComparer.Ordinal))
    {
        passed.AddRange(PowerSemanticsTests.Run());
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(PowerSemanticsTests.Real(input, scratch));
        passed.AddRange(LeaderChargeContractTests.Real(input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused);
        return;
    }
    if (args.Contains("--alternative-only", StringComparer.Ordinal))
    {
        passed.AddRange(AlternativeEffectTests.Run());
        passed.AddRange(AdditiveEffectTests.Run());
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(AlternativeEffectTests.Real(input, scratch));
        passed.AddRange(AdditiveEffectTests.Real(input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused);
        return;
    }
    if (args.Contains("--additive-only", StringComparer.Ordinal))
    {
        passed.AddRange(AdditiveEffectTests.Run());
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(AdditiveEffectTests.Real(input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused);
        return;
    }
    if (args.Contains("--leader-contracts-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(LeaderChargeContractTests.Real(input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused);
        return;
    }
    string fixture = Path.Combine(scratch, "source");
    Directory.CreateDirectory(fixture);
    XElement Card(int id, int type = 4, int availability = 1, int secondary = 0) => new("Template",
        new XAttribute("Type", "CardTemplate"), new XAttribute("Id", id), new XAttribute("DebugName", "Card " + id),
        new XAttribute("Availability", availability), new XAttribute("AudioId", 1700),
        new XElement("Power", type == 4 ? 4 : 0), new XElement("Armor", 2), new XElement("Provision", 6),
        new XElement("Tier", 2), new XElement("Rarity", 1), new XElement("Type", type),
        new XElement("FactionId", 1), new XElement("SecondaryFactionId", secondary),
        new XElement("Categories", new XElement("e0", new XAttribute("V", "16384")),
            new XElement("e1", new XAttribute("V", "0")), new XElement("e2", new XAttribute("V", "4"))),
        new XElement("PrimaryCategory", new XElement("e0", new XAttribute("V", "0")),
            new XElement("e1", new XAttribute("V", "16384")), new XElement("e2", new XAttribute("V", "0"))),
        new XElement("Tokens", 8192), new XElement("AbilityIds", new XElement("e0", id)));
    XElement token = Card(16, 4, 0);
    token.SetAttributeValue("DebugName", "Spawned token");
    token.Element("Categories")!.Element("e1")!.SetAttributeValue("V", "512");
    XElement tutorial = Card(17);
    tutorial.SetAttributeValue("DebugName", "Tutorial Geralt");
    XElement ftux = Card(18);
    ftux.SetAttributeValue("DebugName", "FTUX Leader");
    var dataOnly = Card(13, 4, 0);
    dataOnly.SetAttributeValue("DebugName", "Data only card fixture");
    new XDocument(new XElement("Templates", Card(11), Card(12, 2), dataOnly, Card(14, 1), Card(15, 4, 1, 2), token, tutorial, ftux))
        .Save(Path.Combine(fixture, "Templates.xml"));
    File.WriteAllText(Path.Combine(fixture, "Abilities.xml"), "<Abilities><unchanged /></Abilities>");
    string csv = "11_name;Mage\r\n11_tooltip;<keyword=deploy>Deploy</keyword>: damage {Damage}.\\nNext\r\n11_fluff;Story;with separator\r\nuntouched;Keep  this \r\n";
    foreach (string language in EditorProject.Languages)
        File.WriteAllText(Path.Combine(fixture, language + ".csv"), csv, new UTF8Encoding(language == "ru-ru"));
    var initialHashes = Directory.GetFiles(fixture).ToDictionary(p => Path.GetFileName(p)!, Hash);
    var project = new EditorProject(fixture);
    Check(project.Cards.Count == 8 && project.AvailableLanguages.Count == EditorProject.Languages.Length, "load fixture and locales");
    if (args.Contains("--descriptions-only", StringComparer.Ordinal))
    {
        passed.AddRange(DescriptionBindingTests.Run(fixture, scratch));
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(DescriptionBindingTests.Real(input));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused);
        return;
    }
    if (args.Contains("--statuses-only", StringComparer.Ordinal))
    {
        var input = ClientSource.Open(args[0], Path.Combine(scratch, "client-cache"));
        passed.AddRange(StatusBatchTests.Run(fixture, input, scratch));
        string focused = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], focused);
        Console.WriteLine(focused);
        return;
    }
    passed.AddRange(NumberTests.Run(fixture, scratch));
    passed.AddRange(DescriptionBindingTests.Run(fixture, scratch));
    passed.AddRange(InitialChargeTests.Run(scratch));
    passed.AddRange(NumberGroupTests.Run(scratch));
    passed.AddRange(BalanceTests.Run(scratch));
    passed.AddRange(ZealTests.Run(scratch));
    passed.AddRange(InitialStatusTests.Run(scratch));
    passed.AddRange(StatusReadTests.Run(scratch));
    passed.AddRange(StatusWriteTests.Run(scratch));
    passed.AddRange(StatusVariableTests.Run(scratch));
    passed.AddRange(StatusMoneyTests.Run());
    passed.AddRange(ProfitTests.Run());
    passed.AddRange(AdditiveEffectTests.Run());
    passed.AddRange(AlternativeEffectTests.Run());
    passed.AddRange(PowerSemanticsTests.Run());
    passed.AddRange(PaymentTests.Run());
    passed.AddRange(ThresholdTests.Run());
    passed.AddRange(SubAbilityTests.Run());
    passed.AddRange(AuditTests.Run(scratch));
    passed.AddRange(SortNameTests.Run(fixture, scratch));
    passed.AddRange(RelatedCardTests.Run(fixture, scratch));
    Check(project.Card(16).IsToken && project.Card(16).Editable, "explicit token category recognized and editable");
    Check(!project.Card(13).IsToken, "availability zero alone does not mean token");
    foreach (CardGroup cardGroup in Enum.GetValues<CardGroup>())
        Check(CardCatalog.Query(project, 1, "en-us", group: cardGroup, showHidden: true).All(c => !c.IsTutorial), "tutorial excluded in group " + cardGroup);
    Check(CardCatalog.Query(project, 1, "en-us").All(c => !c.IsToken), "tokens excluded from main cards");
    Check(CardCatalog.Query(project, 1, "en-us", group: CardGroup.Tokens).Select(c => c.Id).SequenceEqual(new[] { 16 }), "token category includes unavailable symbols");
    foreach (CardSort cardSort in Enum.GetValues<CardSort>())
    {
        var up = CardCatalog.Query(project, 1, "en-us", group: CardGroup.All, showHidden: true, sort: cardSort);
        var down = CardCatalog.Query(project, 1, "en-us", group: CardGroup.All, showHidden: true, sort: cardSort, descending: true);
        Func<CardEntry, string> sortKey = cardSort switch
        {
            CardSort.Name => c => project.Name(c.Id, "en-us"), CardSort.Rarity => c => c.Rarity.ToString("D10"),
            CardSort.Power => c => c.Power.ToString("D10"), CardSort.Provision => c => c.Provision.ToString("D10"), _ => c => c.Id.ToString("D10")
        };
        var comparer = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("en-us"), true);
        Check(up.Select(sortKey).SequenceEqual(up.Select(sortKey).Order(comparer)), "ascending " + cardSort);
        Check(down.Select(sortKey).SequenceEqual(down.Select(sortKey).OrderDescending(comparer)), "descending " + cardSort);
    }
    Check(!project.Card(13).Editable && !project.Card(14).Editable, "hidden and malformed leader blocked");
    CardEdit edit = project.CurrentEdit(11);
    Reject(() => project.Apply(11, edit with { Power = 0 }), "Range:Power");
    Reject(() => project.Apply(11, edit with { Provision = 100 }), "Range:Provision");
    Reject(() => project.Apply(11, edit with { Tier = 16 }), "UnsupportedFields");
    Reject(() => project.Apply(11, edit with { Faction = 3 }), "UnsupportedFields");
    Reject(() => project.Apply(12, project.CurrentEdit(12) with { Power = 7 }), "Range:Power");
    Reject(() => project.Apply(13, project.CurrentEdit(13) with { Power = 7 }), "HiddenOrGenerated");
    var dualEdit = project.CurrentEdit(15);
    Reject(() => project.Apply(15, dualEdit with { Faction = 2 }), "SecondaryFactionInvalid");
    Reject(() => project.Apply(15, dualEdit with { SecondaryFaction = 99 }), "UnsupportedFields");
    Check(project.Apply(15, dualEdit with { SecondaryFaction = 4 }), "change secondary faction independently");
    Check(project.Card(15).Faction == 1 && project.Card(15).SecondaryFaction == 4, "read changed secondary faction");
    project.Undo();
    Check(project.Apply(15, dualEdit with { Faction = 8, SecondaryFaction = 4 }), "change both factions together");
    Check(project.Card(15).Faction == 8 && project.Card(15).SecondaryFaction == 4, "read both changed factions");
    project.Undo();
    Check(project.Apply(11, edit with { SecondaryFaction = 2 }), "add a secondary faction");
    Check(project.Card(11).SecondaryFaction == 2, "read newly added secondary faction");
    Check(project.Changes().Any(c => c.Field == "11/SecondaryFactionId"), "secondary faction appears in change report");
    string secondaryOutput = Path.Combine(scratch, "secondary-faction-output");
    project.Export(secondaryOutput);
    Check(new EditorProject(secondaryOutput).Card(11).SecondaryFaction == 2, "secondary faction export round-trip");
    project.Undo();
    Check(project.Apply(15, dualEdit with { SecondaryFaction = 0 }), "remove secondary faction");
    Check(project.Card(15).SecondaryFaction == 0, "read removed secondary faction");
    project.Undo();
    Reject(() => project.Apply(11, edit with { Categories = new HashSet<string> { "bogus" } }), "UnknownCategory");
    CardEdit TextEdit(string language, string field, string value) => project.CurrentEdit(11) with
    {
        Texts = new Dictionary<string, IReadOnlyDictionary<string, string>>
        { [language] = new Dictionary<string, string> { [field] = value } }
    };
    Reject(() => project.Apply(11, TextEdit("ru-ru", "name", "bad\nline")), "PhysicalLine");
    Reject(() => project.Apply(11, TextEdit("ru-ru", "name", "")), "EmptyName");
    Reject(() => project.Apply(11, TextEdit("ru-ru", "tooltip", "Lost tokens")), "TextTokens");
    Reject(() => project.Apply(11, TextEdit("ru-ru", "name", "Broken <tag")), "TextTokens");
    Reject(() => project.Apply(11, TextEdit("ru-ru", "missing", "text")), "MissingText");
    Check(!project.HasChanges && !project.CanUndo, "failed edits are atomic");
    Check(project.Apply(11, edit with { Power = 5, Armor = 4, Provision = 8, Tier = 8, Rarity = 4,
        Faction = 8, Categories = new HashSet<string> { "mage", "warrior" } }), "apply basic fields");
    Check(project.Card(11).Power == 5 && project.Card(11).Faction == 8 && project.Categories(11).Contains("warrior"), "read changed fields");
    project.Undo();
    Check(!project.HasChanges, "undo basic fields");
    Check(!project.Apply(11, edit) && !project.HasChanges, "no-op edit");
    Check(project.PrimaryCategory(11) == "human", "decode primary category");
    project.Apply(11, edit with { PrimaryCategory = "elf" });
    Check(project.PrimaryCategory(11) == "elf", "edit primary category");
    project.Undo();
    project.Apply(11, edit with { Categories = new HashSet<string> { "warrior" } });
    string categoryOutput = Path.Combine(scratch, "category-output");
    project.Export(categoryOutput);
    XElement categoryTemplate = XDocument.Load(Path.Combine(categoryOutput, "Templates.xml")).Root!.Elements().First();
    Check((string?)categoryTemplate.Element("Categories")?.Element("e2")?.Attribute("V") == "4", "unknown category bits preserved");
    Check(XNode.DeepEquals(categoryTemplate.Element("PrimaryCategory"), XDocument.Load(Path.Combine(fixture, "Templates.xml")).Root!.Elements().First().Element("PrimaryCategory")), "additional category edits preserve primary");
    project.Undo();
    project.Apply(11, edit with { Power = 5 });
    project.Apply(11, TextEdit("ru-ru", "name", "Русский маг"));
    project.Apply(11, TextEdit("zh-cn", "name", "法师"));
    project.Apply(11, TextEdit("en-us", "fluff", "A new story;still valid"));
    string output = Path.Combine(scratch, "output");
    project.Export(output);
    var reloaded = new EditorProject(output);
    Check(reloaded.Card(11).Power == 5, "later text edits preserve numeric edits");
    Check(reloaded.Name(11, "ru-ru") == "Русский маг" && reloaded.Name(11, "zh-cn") == "法师", "localized text round-trip");
    Check(reloaded.Text(11, "en-us", "fluff") == "A new story;still valid", "semicolon value round-trip");
    string changedCsv = File.ReadAllText(Path.Combine(output, "ru-ru.csv"));
    Check(changedCsv.Contains("untouched;Keep  this \r\n"), "unrelated locale bytes preserved");
    Check(File.ReadAllBytes(Path.Combine(output, "ru-ru.csv")).AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "UTF-8 BOM preserved");
    foreach (var (name, hash) in initialHashes)
    {
        Check(Hash(Path.Combine(fixture, name!)) == hash, "source untouched " + name);
        Check(Hash(Path.Combine(output, "backup", name!)) == hash, "backup exact " + name);
    }
    Check(Hash(Path.Combine(output, "Abilities.xml")) == initialHashes["Abilities.xml"], "abilities unchanged");
    Check(XDocument.Load(Path.Combine(output, "Templates.xml")).Root!.Elements().First().Element("Tokens")!.Value == "8192", "status bits preserved");
    Reject(() => project.Export(output), "ExportExists");
    Reject(() => project.Export(Path.Combine(fixture, "new")), "ExportInsideSource");
    project.Reset();
    Check(!project.HasChanges && project.CanUndo, "reset project");
    project.Undo();
    Check(project.HasChanges, "undo reset");
    File.AppendAllText(Path.Combine(fixture, "Abilities.xml"), " ");
    Reject(() => project.Export(Path.Combine(scratch, "stale")), "SourceChanged");
    Check(!Directory.Exists(Path.Combine(scratch, "stale")), "stale export leaves no output");

    string client = Path.Combine(scratch, "fake-client");
    string streamAssets = Path.Combine(client, "Gwent_Data", "StreamingAssets");
    Directory.CreateDirectory(streamAssets);
    string fakeArchive = Path.Combine(streamAssets, "data_definitions");
    using (var zip = ZipFile.Open(fakeArchive, ZipArchiveMode.Create))
    {
        foreach (string file in Directory.GetFiles(fixture))
            zip.CreateEntryFromFile(file, Path.GetExtension(file) == ".csv" ? "Localization/" + Path.GetFileName(file) : Path.GetFileName(file));
        var entry = zip.CreateEntry("Unrelated/preserved.bin");
        using var stream = entry.Open();
        stream.Write(new byte[] { 1, 2, 3, 4, 5 });
    }
    string archiveHash = Hash(fakeArchive);
    string cache = Path.Combine(scratch, "cache");
    var imported = ClientSource.Open(client, cache);
    Check(imported.Client != null && imported.AvailableLanguages.Count == EditorProject.Languages.Length, "open client archive and locales");
    Check(ClientSource.Open(client, cache).DirectoryPath == imported.DirectoryPath, "reuse verified immutable cache");
    var archiveEdit = imported.CurrentEdit(11);
    imported.Apply(11, archiveEdit with { Power = 7 });
    imported.Apply(11, imported.CurrentEdit(11) with { Texts = new Dictionary<string, IReadOnlyDictionary<string, string>>
        { ["en-us"] = new Dictionary<string, string> { ["name"] = "Changed Mage" } } });
    string packedOutput = Path.Combine(scratch, "packed");
    imported.Export(packedOutput);
    Check(Directory.GetFiles(packedOutput).Select(Path.GetFileName).SequenceEqual(new[] { "data_definitions" }), "client export has only installable archive at top level");
    Check(Directory.GetDirectories(packedOutput).Select(Path.GetFileName).SequenceEqual(new[] { "_editor" }), "client export has only one support folder");
    Check(!Directory.EnumerateFiles(packedOutput, "*.xml", SearchOption.AllDirectories).Any()
        && !Directory.EnumerateFiles(packedOutput, "*.csv", SearchOption.AllDirectories).Any(), "client export contains no confusing unpacked files");
    Check(Hash(Path.Combine(packedOutput, "_editor", "backup", "data_definitions")) == archiveHash, "compact export backs up exact original archive");
    using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(packedOutput, "_editor", "card-editor-changes.json"))))
    {
        Check(manifest.RootElement.GetProperty("OutputHashes").GetProperty("data_definitions").GetString() == Hash(Path.Combine(packedOutput, "data_definitions")), "compact manifest verifies installable archive hash");
        Check(manifest.RootElement.GetProperty("SourceArchiveHash").GetString() == archiveHash, "compact manifest records backup source hash");
    }
    Check(Hash(fakeArchive) == archiveHash, "original client archive untouched");
    var packed = ClientSource.Open(Path.Combine(packedOutput, "data_definitions"), cache);
    Check(packed.Card(11).Power == 7 && packed.Name(11, "en-us") == "Changed Mage", "exported archive round-trip");
    using (var zip = ZipFile.OpenRead(Path.Combine(packedOutput, "data_definitions")))
    {
        using var stream = zip.GetEntry("Unrelated/preserved.bin")!.Open();
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        Check(memory.ToArray().SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }), "unrelated binary entry preserved");
    }
    File.AppendAllText(Path.Combine(imported.DirectoryPath, "Templates.xml"), " ");
    Check(ClientSource.Open(client, cache).DirectoryPath != imported.DirectoryPath, "tampered cache is not reused");
    File.AppendAllText(fakeArchive, " ");
    Reject(() => packed.Export(Path.Combine(scratch, "not-dirty")), "NoChanges");
    Reject(() => imported.Export(Path.Combine(scratch, "stale-client")), "SourceChanged");

    if (args.Length > 0)
    {
        string actual = Path.GetFullPath(args[0]);
        string? actualArchive = File.Exists(Path.Combine(actual, "Templates.xml")) ? null : ClientSource.ResolveArchive(actual);
        string? actualArchiveHash = actualArchive == null ? null : Hash(actualArchive);
        var real = ClientSource.Open(actual, Path.Combine(scratch, "real-cache"));
        var actualHashes = Directory.GetFiles(real.DirectoryPath).Where(p => Path.GetExtension(p) is ".xml" or ".csv").ToDictionary(p => Path.GetFileName(p)!, Hash);
        Check(real.Cards.Count > 1000, "real project loaded");
        int noops = 0;
        foreach (CardEntry card in real.Cards.Where(x => x.Editable))
        {
            if (real.Preview(card.Id, real.CurrentEdit(card.Id)).Count != 0) throw new Exception("False changes on " + card.Id);
            noops++;
        }
        Check(noops > 1000, "all supported real cards round-trip without false changes: " + noops);
        CardEdit current = real.CurrentEdit(112101);
        real.Apply(112101, current with { Power = current.Power + 1 });
        real.Export(Path.Combine(scratch, "real-export"));
        var verified = ClientSource.Open(Path.Combine(scratch, "real-export"), Path.Combine(scratch, "real-export-cache"));
        Check(verified.Card(112101).Power == current.Power + 1, "real project export round-trip");
        Check(real.Changes().Count == 1, "real edit only changes selected field");
        Check(Hash(Path.Combine(verified.DirectoryPath, "Abilities.xml")) == actualHashes["Abilities.xml"], "real ability graph bytes unchanged");
        foreach (var (name, hash) in actualHashes) Check(Hash(Path.Combine(real.DirectoryPath, name!)) == hash, "real source unchanged " + name);
        if (actualArchive != null)
        {
            Check(Hash(actualArchive) == actualArchiveHash, "original GOG client archive unchanged");
            var finalClient = ClientSource.Open(Path.Combine(scratch, "real-export", "data_definitions"), Path.Combine(scratch, "final-cache"));
            Check(finalClient.Card(112101).Power == current.Power + 1, "original client packed export verified");
        }
        real.Undo();
        Check(!real.HasChanges, "real undo exact");
        var realDamage = real.Numbers(122301).Single(n => n.Id == 501);
        Check(realDamage.Editable && realDamage.Role == "damage", "real damage parameter is verified");
        real.Apply(122301, real.CurrentEdit(122301) with { Numbers = new Dictionary<int, int> { [501] = realDamage.Value + 1 } });
        string realNumbers = Path.Combine(scratch, "real-numbers-export");
        real.Export(realNumbers);
        var numberResult = ClientSource.Open(realNumbers, Path.Combine(scratch, "real-number-cache"));
        Check(Hash(Path.Combine(numberResult.DirectoryPath, "Templates.xml")) == actualHashes["Templates.xml"], "real ability-only export keeps original template bytes");
        foreach (string locale in EditorProject.Languages)
            if (actualHashes.ContainsKey(locale + ".csv"))
                Check(Hash(Path.Combine(numberResult.DirectoryPath, locale + ".csv")) == actualHashes[locale + ".csv"], "real numeric edit keeps locale " + locale);
        Check(numberResult.Numbers(122301).Single(n => n.Id == 501).Value == realDamage.Value + 1, "real numeric packed export round-trip");
        Check(real.Changes().Count == 1, "real numeric edit changes only one graph value");
        real.Undo();
        Check(!real.HasChanges, "real numeric undo exact");
        int cardsWithNumbers = 0, editableNumbers = 0, blockedNumbers = 0;
        passed.AddRange(StatusMoneyTests.Real(real, scratch));
        passed.AddRange(ProfitTests.Examples(real));
        passed.AddRange(StatusMoneyTests.Real(real, scratch, true));
        passed.AddRange(PaymentTests.Examples(real));
        passed.AddRange(StatusMoneyTests.Real(real, scratch, payment: true));
        passed.AddRange(ThresholdTests.Examples(real));
        passed.AddRange(StatusMoneyTests.Real(real, scratch, threshold: true));
        passed.AddRange(SubAbilityTests.Examples(real));
        passed.AddRange(StatusMoneyTests.Real(real, scratch, subAbility: true));
        passed.AddRange(ZealTests.Real(real, scratch));
        passed.AddRange(NumberGroupTests.Real(real, scratch));
        passed.AddRange(BalanceTests.Real(real, scratch));
        passed.AddRange(InitialChargeTests.Real(real, scratch));
        passed.AddRange(ChargeRefillTests.Real(real, scratch));
        passed.AddRange(InitialStatusTests.Real(real, scratch));
        passed.AddRange(StatusEconomyTests.Real(real, scratch));
        passed.AddRange(StatusTributeTests.Real(real, scratch));
        passed.AddRange(StatusDefinitionTests.Real(real, scratch));
        passed.AddRange(StatusMathTests.Real(real, scratch));
        passed.AddRange(TimingTests.Real(real, scratch));
        passed.AddRange(LeaderTests.Real(real, scratch));
        passed.AddRange(LeaderChargeTests.Real(real, scratch));
        passed.AddRange(LeaderChargeContractTests.Real(real, scratch));
        passed.AddRange(AdditiveEffectTests.Real(real, scratch));
        passed.AddRange(AlternativeEffectTests.Real(real, scratch));
        passed.AddRange(PowerSemanticsTests.Real(real, scratch));
        passed.AddRange(CopyMeaningTests.Run(real, scratch));
        passed.AddRange(CatalogVisibilityTests.Run(real, scratch));
        passed.AddRange(AbilityClarityTests.Run(real, scratch));
        passed.AddRange(AbilityClarityBatchTests.Run(real, scratch));
        passed.AddRange(AlternativeSelectionClarityTests.Run(real, scratch));
        passed.AddRange(InitialPatienceTests.Run(real, scratch));
        passed.AddRange(ChargeClarityTests.Run(real, scratch));
        passed.AddRange(VerifiedEffectTests.Run(real, scratch));
        passed.AddRange(ChoiceEffectTests.Run(real, scratch));
        passed.AddRange(GordTests.Run(real, scratch));
        passed.AddRange(AdditionalAbilityTests.Run(real, scratch));
        passed.AddRange(AssimilateTests.Run(real, scratch));
        passed.AddRange(HarmonyTests.Run(real, scratch));
        passed.AddRange(IntimidateTests.Run(real, scratch));
        passed.AddRange(ThriveTests.Run(real, scratch));
        passed.AddRange(VeteranTests.Run(real, scratch));
        passed.AddRange(FormationTests.Run(real, scratch));
        passed.AddRange(AdditionalValueTests.Run(real, scratch));
        passed.AddRange(NativeKeywordTests.Run(real, scratch));
        passed.AddRange(NativeKeywordSourceTests.Run(real, scratch));
        passed.AddRange(CardClarityRevisionTests.Run(real, scratch));
        passed.AddRange(LeaderCopyCatalogTests.Run(real, scratch));
        passed.AddRange(ReviewBatchTests.Run(real, scratch));
        passed.AddRange(SyndicateEditingTests.Run(real, scratch));
        passed.AddRange(ResupplyTests.Run(real, scratch));
        passed.AddRange(EchoTests.Run(real, scratch));
        passed.AddRange(FlankingTests.Run(real, scratch));
        passed.AddRange(CatalogTokenTests.Run(real));
        passed.AddRange(InsanityTests.Run(real,scratch));
        passed.AddRange(GigascorpionTests.Run(real, scratch));
        passed.AddRange(DescriptionBindingTests.Real(real));
        passed.AddRange(TargetCountTextTests.Run(real, scratch));
        passed.AddRange(TargetCountPowerTextTests.Run(real, scratch));
        passed.AddRange(TargetStatusTextTests.Run(real, scratch));
        passed.AddRange(DrawCountTextTests.Run(real, scratch));
        passed.AddRange(MoveCountTextTests.Run(real, scratch));
        passed.AddRange(DiscardCountTextTests.Run(real, scratch));
        passed.AddRange(SelectedDrawTextTests.Run(real, scratch));
        passed.AddRange(ReadinessAuditTests.Run(real, scratch));
        passed.AddRange(ExportContinuationTests.Run(real, scratch));
        passed.AddRange(TokenTests.Real(real, scratch));
        passed.AddRange(RelatedCardTests.Real(real));
        passed.AddRange(CharacteristicsIntegrationTests.Real(real, scratch));
        passed.AddRange(StatusBatchTests.Run(fixture, real, scratch));
        if (args.Length > 2) passed.AddRange(BalanceTests.Live(real, scratch, args[2]));
        var reasons = new Dictionary<string, int>();
        foreach (CardEntry card in real.Cards.Where(c => c.Editable))
        {
            var values = real.Numbers(card.Id);
            if (values.Any(n => n.Editable)) cardsWithNumbers++;
            editableNumbers += values.Count(n => n.Editable);
            blockedNumbers += values.Count(n => !n.Editable);
            foreach (var number in values.Where(n => !n.Editable)) reasons[number.Reason] = reasons.GetValueOrDefault(number.Reason) + 1;
        }
        Check(cardsWithNumbers > 100 && editableNumbers > 100, "numeric catalog coverage " + cardsWithNumbers + " cards, " + editableNumbers + " editable, " + blockedNumbers + " blocked");
        if (args.Length > 1) File.WriteAllText(Path.ChangeExtension(args[1], ".coverage.json"), JsonSerializer.Serialize(new { cardsWithNumbers, editableNumbers, blockedNumbers, reasons }, new JsonSerializerOptions { WriteIndented = true }));
    }
    string json = JsonSerializer.Serialize(new { Passed = true, Count = passed.Count, Tests = passed }, new JsonSerializerOptions { WriteIndented = true });
    if (args.Length > 1) File.WriteAllText(args[1], json);
    Console.WriteLine(json);
}
finally
{
    // The only recursively removed directory is the test's newly created, absolute temp directory.
    string resolved = Path.GetFullPath(scratch);
    if (resolved.StartsWith(Path.Combine(Path.GetTempPath(), "GwentCardEditorTests-"), StringComparison.OrdinalIgnoreCase))
        Directory.Delete(resolved, true);
}
