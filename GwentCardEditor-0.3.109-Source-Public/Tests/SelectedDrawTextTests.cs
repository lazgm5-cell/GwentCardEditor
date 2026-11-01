using System.Text.RegularExpressions;
using System.Xml.Linq;
using GwentCardEditor;

internal static class SelectedDrawTextTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var cases = source.Cards.Where(c => c.Editable).SelectMany(c => source.NumberGroups(c.Id)
            .Where(g => g.Display.LabelRole == "draw-selected-count" && g.Display.TextLinked
                && source.DescriptionBindings(c.Id, g.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked && b.Variables.Length == 0))
            .Select(g => (Card: c.Id, Group: g))).ToArray();
        Check(cases.Length >= 2, "generic selected draw coverage " + cases.Length);
        foreach (var (id, group) in cases)
        {
            Check(group.Members.Count == 3, "selection and actual draw are one setting " + id);
            var baseline = source.AvailableLanguages.Where(l => source.HasText(id, l, "tooltip")).ToDictionary(l => l, l => source.Text(id, l, "tooltip"));
            var others = source.Numbers(id).Where(n => group.Members.All(m => n.Id != m.Id)).ToDictionary(n => n.Id, n => n.Value);
            CardEdit Edit(int value) => source.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => value) };
            Check(source.Preview(id, Edit(2)).Count(c => c.Field.EndsWith("/tooltip", StringComparison.Ordinal)) == baseline.Count && !source.HasChanges, "selected draw preview updates every locale without mutation " + id);
            foreach (int value in new[] { 2, 3, 5, 11, 21, 22, 99, 1 })
            {
                source.Apply(id, Edit(value));
                foreach (var (language, text) in baseline)
                {
                    string changed = source.Text(id, language, "tooltip");
                    string[] Tags(string s) => Regex.Matches(s, @"<keyword=[^>]+>[^<]*</keyword>|\{[^{}]*\}").Select(m => m.Value).ToArray();
                    Check(Tags(text).SequenceEqual(Tags(changed)) && (value == 1 ? changed == text : changed != text), "selected draw exact text round trip " + id + "/" + language + "/" + value);
                }
                Check(source.Numbers(id).Where(n => others.ContainsKey(n.Id)).All(n => n.Value == others[n.Id]), "return to deck, damage and charges unchanged " + id + "/" + value);
            }
            Check(!source.HasChanges, "selected draw round trip clean " + id);
            source.Apply(id, Edit(2));
            string output = Path.Combine(scratch, "selected-draw-export-" + id); source.Export(output);
            var reopened = ClientSource.Open(output, Path.Combine(scratch, "selected-draw-reopen-" + id));
            Check(reopened.NumberGroups(id).Single(g => g.Display.Id == group.Display.Id).Display.TextLinked, "selected draw survives packed export " + id);
            var xml = XDocument.Load(Path.Combine(reopened.DirectoryPath, "Abilities.xml"));
            var graph = xml.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id && (string?)e.Attribute("Type") == "CardAbility");
            foreach (var n in group.Members) graph.Descendants().Single(e => (int?)e.Attribute("Id") == n.Id).SetAttributeValue("V", n.Value);
            Check(XNode.DeepEquals(xml, XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"))), "only three selected draw numbers change in graph " + id);
            reopened.Apply(id, reopened.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => 1) });
            Check(baseline.All(p => reopened.Text(id, p.Key, "tooltip") == p.Value), "reopened selected draw restores all locales " + id);
            source.Undo();
            try { source.Preview(id, source.CurrentEdit(id) with { Numbers = new Dictionary<int, int> { [group.Display.Id] = 2 } }); throw new Exception("Expected linked draw conflict"); }
            catch (InvalidOperationException e) when (e.Message == "TargetCountTextConflict") { passed.Add("partial selected draw edit rejected " + id); }
        }
        const int sample = 112205;
        var pair = cases.Single(c => c.Card == sample).Group;
        string fixture = Path.Combine(scratch, "selected-draw-guards"); Directory.CreateDirectory(fixture);
        var native = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == sample && (string?)e.Attribute("Type") == "CardAbility");
        var template = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == sample);
        new XDocument(new XElement("Templates", new XElement(template))).Save(Path.Combine(fixture, "Templates.xml"));
        foreach (string language in source.AvailableLanguages) File.Copy(Path.Combine(source.DirectoryPath, language + ".csv"), Path.Combine(fixture, language + ".csv"));
        void Save(XElement graph) => new XDocument(new XElement("Abilities", graph)).Save(Path.Combine(fixture, "Abilities.xml"));
        bool Linked() => new EditorProject(fixture).DescriptionBindings(sample, pair.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked);
        Save(new XElement(native)); Check(Linked(), "isolated selected Dragon draw recognized");
        foreach (string guard in new[] { "wrong-category", "negated-category", "unequal-draw", "wrong-choice", "other-source", "opponent", "random", "shared-selection" })
        {
            var altered = new XElement(native); var nodes = altered.Element("Nodes")!;
            var choice = nodes.Elements().Single(n => (string?)n.Attribute("Type") == "RequestCardChoiceNode" && (int?)n.Element("MinChoices")?.Attribute("Id") == pair.Members[1].Id);
            var draw = nodes.Elements().Single(n => (string?)n.Attribute("Type") == "DrawCardsNode");
            var filter = nodes.Elements().Single(n => (string?)n.Attribute("Type") == "FilterCardCategoryNode");
            switch (guard)
            {
                case "wrong-category": filter.Element("CardCategory")!.Element("V")!.Elements().First().SetAttributeValue("V", "1"); break;
                case "negated-category": filter.Element("Negate")!.SetAttributeValue("V", "True"); break;
                case "unequal-draw": draw.Element("Num")!.SetAttributeValue("V", 2); break;
                case "wrong-choice": choice.Element("ChoiceType")!.SetAttributeValue("V", 4); break;
                case "other-source": nodes.Elements().Single(n => (string?)n.Attribute("Type") == "GetCardsNode" && (int?)n.Element("LocationId")?.Attribute("V") == 16).Element("LocationId")!.SetAttributeValue("V", 8); break;
                case "opponent": foreach (var n in nodes.Elements().Where(n => (string?)n.Attribute("Type") == "GetOwnerPlayerIdNode")) n.SetAttributeValue("Type", "GetOwnerOpponentPlayerIdNode"); break;
                case "random": draw.Element("Random")!.SetAttributeValue("V", "True"); break;
                case "shared-selection": altered.Element("Connections")!.Add(new XElement("auditWire", new XAttribute("Type", "Connection"), new XAttribute("Id", 900000), new XAttribute("SrcId", (int)choice.Element("SelectedChoices")!.Attribute("Id")!), new XAttribute("DstId", 900001))); break;
            }
            Save(altered); Check(!Linked(), "unsafe selected draw mapping excluded " + guard);
        }
        foreach (int id in new[] { 202187, 202782 }) Check(source.NumberGroups(id).Any(g => g.Display.LabelRole == "draw-selected-count" && g.Display.TextLinked), "confirmed filtered selected draw linked " + id);
        foreach (int id in new[] { 202187, 202782 })
        {
            string filteredFixture = Path.Combine(scratch, "selected-draw-filter-guards-" + id); Directory.CreateDirectory(filteredFixture);
            var filteredNative = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == id && (string?)e.Attribute("Type") == "CardAbility");
            var filteredTemplate = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == id);
            new XDocument(new XElement("Templates", new XElement(filteredTemplate))).Save(Path.Combine(filteredFixture, "Templates.xml"));
            foreach (string language in source.AvailableLanguages) File.Copy(Path.Combine(source.DirectoryPath, language + ".csv"), Path.Combine(filteredFixture, language + ".csv"));
            var filteredGroup = cases.Single(c => c.Card == id).Group;
            void SaveFiltered(XElement graph) => new XDocument(new XElement("Abilities", graph)).Save(Path.Combine(filteredFixture, "Abilities.xml"));
            bool FilteredLinked() => new EditorProject(filteredFixture).DescriptionBindings(id, filteredGroup.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked);
            SaveFiltered(new XElement(filteredNative)); Check(FilteredLinked(), "isolated filtered snapshot draw recognized " + id);
            foreach (string guard in new[] { "wrong-value", "wrong-negation", "disconnected-filter-flow", "extra-filter" })
            {
                var altered = new XElement(filteredNative); var nodes = altered.Element("Nodes")!;
                var predicate = nodes.Elements().Single(n => (string?)n.Attribute("Type") == (id == 202187 ? "FilterCardFactionNode" : "FilterHasKeywordNode"));
                var setter = nodes.Elements().Single(n => (string?)n.Attribute("Type") == "SetVarNode" && (string?)n.Element("In")?.Attribute("Type") == "CardListVar");
                var sourceWire = altered.Element("Connections")!.Elements().Single(e => (int?)e.Attribute("DstId") == (int?)setter.Element("In")?.Attribute("Id"));
                var filter = nodes.Elements().Single(n => (int?)n.Element("FilteredCards")?.Attribute("Id") == (int?)sourceWire.Attribute("SrcId"));
                switch (guard)
                {
                    case "wrong-value": predicate.Element(id == 202187 ? "Faction" : "Keyword")!.SetAttributeValue("V", 2); break;
                    case "wrong-negation": predicate.Element("Negate")!.SetAttributeValue("V", id == 202187 ? "False" : "True"); break;
                    case "disconnected-filter-flow": altered.Element("Connections")!.Elements().Single(e => (int?)e.Attribute("DstId") == (int?)setter.Element("FlowIn")?.Attribute("Id")).SetAttributeValue("SrcId", (int)filter.Element("FlowFail")!.Attribute("Id")!); break;
                    case "extra-filter": var extra = new XElement(filter.Element("Filters")!.Elements().First()); extra.Name = "auditFilter"; extra.SetAttributeValue("Id", 900000); filter.Element("Filters")!.Add(extra); break;
                }
                SaveFiltered(altered); Check(!FilteredLinked(), "unsafe filtered snapshot draw excluded " + id + "/" + guard);
            }
        }
        const int cached = 202498;
        var cachedGroup = cases.Single(c => c.Card == cached).Group;
        string cachedFixture = Path.Combine(scratch, "selected-draw-cache-guards"); Directory.CreateDirectory(cachedFixture);
        var cachedNative = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == cached && (string?)e.Attribute("Type") == "CardAbility");
        var cachedTemplate = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == cached);
        new XDocument(new XElement("Templates", new XElement(cachedTemplate))).Save(Path.Combine(cachedFixture, "Templates.xml"));
        foreach (string language in source.AvailableLanguages) File.Copy(Path.Combine(source.DirectoryPath, language + ".csv"), Path.Combine(cachedFixture, language + ".csv"));
        void SaveCached(XElement graph) => new XDocument(new XElement("Abilities", graph)).Save(Path.Combine(cachedFixture, "Abilities.xml"));
        bool CachedLinked() => new EditorProject(cachedFixture).DescriptionBindings(cached, cachedGroup.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked);
        SaveCached(new XElement(cachedNative)); Check(CachedLinked(), "single assignment cached deck draw recognized");
        foreach (string guard in new[] { "second-writer", "wrong-variable-type", "missing-source", "reaction" })
        {
            var altered = new XElement(cachedNative); var nodes = altered.Element("Nodes")!;
            var setter = nodes.Elements().Single(n => (string?)n.Attribute("Type") == "SetVarNode");
            switch (guard)
            {
                case "second-writer": var extra = new XElement(setter); extra.Name = "auditSetter"; extra.SetAttributeValue("Id", 900000); nodes.Add(extra); break;
                case "wrong-variable-type": altered.Element("TemporaryVariables")!.Elements().Single(v => (int?)v.Attribute("Id") == (int?)setter.Attribute("VarId")).SetAttributeValue("Type", "IntVar"); break;
                case "missing-source": altered.Element("Connections")!.Elements().Where(e => (int?)e.Attribute("DstId") == (int?)setter.Element("In")?.Attribute("Id")).Remove(); break;
                case "reaction": foreach (var tapped in nodes.Elements().Where(n => (string?)n.Attribute("Type") == "TappedTrigger")) tapped.SetAttributeValue("Type", "AfterCardMovedTrigger"); break;
            }
            SaveCached(altered); Check(!CachedLinked(), "unsafe cached draw mapping excluded " + guard);
        }
        Check(!source.HasChanges, "selected draw tests leave original clean"); source.Client!.Verify();
        return passed;
    }
}
