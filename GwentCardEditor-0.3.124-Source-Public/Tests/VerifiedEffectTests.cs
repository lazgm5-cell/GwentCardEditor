using GwentCardEditor;
using System.Xml.Linq;

internal static class VerifiedEffectTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed.Add(name); }
        foreach (var (card, field, reason) in new[] { (203282, 94, "ImplicitChargeCondition"),
            (203117, 216, "ChargeActivationSignal"), (122209, 466, "ArmorConversionReset") })
        {
            Check(source.Numbers(card).Single(n => n.Id == field) is { Editable: false } n && n.Reason == reason,
                "Service field hidden " + card + "/" + field);
            bool denied = false;
            try { source.Preview(card, source.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [field] = 2 } }); }
            catch (InvalidOperationException e) when (e.Message == reason) { denied = true; }
            Check(denied, "Service field protected in backend " + card + "/" + field);
        }
        Check(source.Numbers(203117).Any(n => n.Id == 97 && n.Editable), "Priestess real Order boost retained");
        Check(source.Numbers(122209).Any(n => n.Id == 374 && n.Editable), "Trololo real armor gain retained");
        Check(source.VerifiedEffects(202417).Select(s => (s.Id, s.Value)).SequenceEqual([(-7, 4), (-8, 3)]), "Philippa native first hit and random series recognized");
        Check(source.VerifiedEffects(202503).Single() is { Id: -9, Value: 4 }, "Elephant linked boost recognized");
        Check(!source.Numbers(202417).Any(n => n.Id is 54 or 69 or 70 or 71 && n.Editable)
            && !source.Numbers(202503).Any(n => n.Id is 74 or 75 && n.Editable), "Runtime and presentation aliases not separately unlocked");
        foreach (var (card, field, value, reason) in new[] { (202417, -8, 1, "AbilityRange"), (202417, -7, -1, "AbilityRange"),
            (202503, -9, 5000, "AbilityRange"), (122209, -7, 1, "ComputedEffectUnsupported") })
        {
            bool denied = false;
            try { source.Preview(card, source.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [field] = value } }); }
            catch (InvalidOperationException e) when (e.Message == reason) { denied = true; }
            Check(denied, "Verified effect range or compatibility enforced " + card + "/" + field);
        }
        int[] ids = [203282, 203117, 122209, 202417, 202503];
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var baseline = new XDocument(new XElement("Abilities", abilities.Root!.Elements().Where(e => (string?)e.Attribute("Type") == "SubAbility" || ids.Contains((int?)e.Attribute("Id") ?? -1))));
        string folder = Path.Combine(scratch, "verified-effects"); Directory.CreateDirectory(folder);
        new XDocument(new XElement("Templates", templates.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1)))).Save(Path.Combine(folder, "Templates.xml"));
        foreach (string locale in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, locale + ".csv"), File.ReadLines(Path.Combine(source.DirectoryPath, locale + ".csv")).Where(l => ids.Any(id => l.StartsWith(id + "_", StringComparison.Ordinal))));
        EditorProject Load(XDocument doc) { doc.Save(Path.Combine(folder, "Abilities.xml")); return new EditorProject(folder); }
        XElement Graph(XDocument doc, int id) => doc.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id);
        foreach (int seed in new[] { 2, 6, 9999 })
        {
            var project = Load(baseline);
            project.Apply(202417, project.CurrentEdit(202417) with { Numbers = new Dictionary<int, int> { [-7] = 5, [-8] = seed } });
            project.Apply(202503, project.CurrentEdit(202503) with { Numbers = new Dictionary<int, int> { [-9] = 5, [39] = 6 } });
            string output = Path.Combine(scratch, "verified-effects-export-" + seed); project.Export(output);
            var reopened = new EditorProject(output);
            Check(reopened.VerifiedEffects(202417).Single(s => s.Id == -7).Value == 5 && reopened.VerifiedEffects(202417).Single(s => s.Id == -8).Value == seed,
                "Philippa synchronized sequence survives export " + seed);
            Check(reopened.VerifiedEffects(202503).Single().Value == 5, "Elephant boost survives combined damage edit " + seed);
            var changed = XDocument.Load(Path.Combine(output, "Abilities.xml"));
            int Value(int card, int field) => (int)Graph(changed, card).Descendants().Single(e => (int?)e.Attribute("Id") == field).Attribute("V")!;
            Check(Value(202417, 69) == seed && Value(202417, 70) == seed - 1 && Value(202417, 71) == seed - 2, "Philippa displayed sequence matches actual arithmetic " + seed);
            Check(Value(202503, 74) == 5 && Value(202503, 75) == 10, "Elephant two-unit and Crew value doubled " + seed);
            foreach (var (card, field, original) in new[] { (202417, 54, 4), (202417, 69, 3), (202417, 70, 2), (202417, 71, 1), (202503, 74, 4), (202503, 75, 8), (202503, 39, 4) })
                Graph(changed, card).Descendants().Single(e => (int?)e.Attribute("Id") == field).SetAttributeValue("V", original);
            Check(XNode.DeepEquals(changed, baseline), "Only seven intended numeric values change, not flow, loops, damage operations or subabilities " + seed);
            Check(source.AvailableLanguages.All(locale => File.ReadAllBytes(Path.Combine(folder, locale + ".csv"))
                .SequenceEqual(File.ReadAllBytes(Path.Combine(output, locale + ".csv")))), "Computed effect export preserves localization bytes " + seed);
            project.Undo(); project.Undo(); Check(!project.HasChanges, "Computed effect edits undo atomically " + seed);
        }
        var changedStep = new XDocument(baseline);
        Graph(changedStep, 202417).Element("Nodes")!.Elements().Single(n => (string?)n.Attribute("Type") == "IntMinusNode").Element("B")!.SetAttributeValue("V", 2);
        Check(Load(changedStep).VerifiedEffects(202417).Count == 0, "Changed random damage decrement blocks sequence editor");
        var mismatchedBoost = new XDocument(baseline);
        Graph(mismatchedBoost, 202503).Descendants().Single(e => (int?)e.Attribute("Id") == 75).SetAttributeValue("V", 9);
        Check(Load(mismatchedBoost).VerifiedEffects(202503).Count == 0, "Mismatched two-unit and Crew amount not silently normalized");
        var changedSub = new XDocument(baseline);
        changedSub.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == 3).SetAttributeValue("NId", 9999);
        Check(Load(changedSub).VerifiedEffects(202417).Count == 0, "Changed shared damage implementation blocks scalar editor");
        var positiveArmor = new XDocument(baseline);
        Graph(positiveArmor, 122209).Descendants().Single(e => (int?)e.Attribute("Id") == 466).SetAttributeValue("V", 2);
        Check(Load(positiveArmor).Numbers(122209).Single(n => n.Id == 466).Editable, "Custom nonzero armor setting not mislabeled as clearing armor");
        var positiveBoost = new XDocument(baseline);
        Graph(positiveBoost, 203117).Descendants().Single(e => (int?)e.Attribute("Id") == 216).SetAttributeValue("V", 1);
        Check(Load(positiveBoost).Numbers(203117).Single(n => n.Id == 216).Editable, "Custom nonzero priestess boost remains editable");
        Check(!source.HasChanges, "Verified effect tests do not edit original game data");
        return passed;
    }
}
