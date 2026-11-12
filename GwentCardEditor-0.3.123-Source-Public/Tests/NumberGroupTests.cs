using System.Xml.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GwentCardEditor;

internal static class NumberGroupTests
{
    public static IReadOnlyList<string> Run(string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        string folder = Path.Combine(scratch, "number-groups");
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(Path.Combine(scratch, "numbers-source")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var project = new EditorProject(folder);
        var group = project.NumberGroups(11).Single(g => g.Members.Count == 2);
        Check(group.Display.Role == "targets" && group.Members.Select(v => v.Id).ToHashSet().SetEquals([113, 114]), "equal bounds on the same selection become one target count");
        Check(project.NumberGroups(11).SelectMany(g => g.Members).Select(m => m.Id).Order()
            .SequenceEqual(project.Numbers(11).Where(n => n.Editable).Select(n => n.Id).Order()), "grouping neither drops nor duplicates editable fields");
        var edit = project.CurrentEdit(11) with { Numbers = group.Members.ToDictionary(m => m.Id, _ => 2) };
        project.Apply(11, edit);
        Check(project.NumberGroups(11).Single(g => g.Members.Count == 2).Display.Value == 2, "one target value updates both limits");
        project.Undo();
        Check(!project.HasChanges, "target count atomic undo");
        string path = Path.Combine(folder, "Abilities.xml");
        var graph = XDocument.Load(path);
        var upper = graph.Descendants().Single(e => (int?)e.Attribute("Id") == 114);
        upper.SetAttributeValue("V", 2);
        graph.Save(path);
        Check(new EditorProject(folder).NumberGroups(11).All(g => g.Members.Count == 1), "real target ranges are not silently converted to exact counts");
        upper.SetAttributeValue("V", 1);
        var maxNode = new XElement("e9", new XAttribute("Type", "RequestCardTargetsNode"), new XAttribute("Id", 900), new XElement(upper));
        upper.Remove();
        graph.Descendants("Nodes").Single().Add(maxNode);
        graph.Save(path);
        Check(new EditorProject(folder).NumberGroups(11).All(g => g.Members.Count == 1), "matching numbers from different selection nodes are not grouped");
        return passed;
    }

    public static IReadOnlyList<string> Real(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var edits = project.Cards.Where(c => c.Editable).Select(c => new { c.Id,
            Values = project.NumberGroups(c.Id).Where(g => g.Members.Count == 2)
                .SelectMany(g => g.Members.Select(m => new { m.Id, Old = m.Value, New = m.Value < g.Display.Maximum ? m.Value + 1 : m.Value - 1 }))
                .ToArray() }).Where(e => e.Values.Length > 0).ToArray();
        Check(edits.Length > 10, "real target count groups on cards: " + edits.Length);
        foreach (var edit in edits) project.Apply(edit.Id, project.CurrentEdit(edit.Id) with { Numbers = edit.Values.ToDictionary(v => v.Id, v => v.New) });
        string output = Path.Combine(scratch, "real-target-count-export");
        project.Export(output);
        var result = ClientSource.Open(output, Path.Combine(scratch, "real-target-count-cache"));
        Check(edits.All(e => e.Values.All(v => result.Numbers(e.Id).Single(n => n.Id == v.Id).Value == v.New)), "all grouped target values survive packed export");
        var xml = XDocument.Load(Path.Combine(result.DirectoryPath, "Abilities.xml"));
        foreach (var edit in edits)
        {
            var ability = xml.Root!.Elements().Single(e => (int?)e.Attribute("Id") == edit.Id && (string?)e.Attribute("Type") == "CardAbility");
            foreach (var value in edit.Values)
                ability.Descendants().Single(e => (int?)e.Attribute("Id") == value.Id).SetAttributeValue("V", value.Old);
        }
        Check(XNode.DeepEquals(xml, XDocument.Load(Path.Combine(project.DirectoryPath, "Abilities.xml"))), "target grouping export changes only the paired numeric attributes");
        foreach (string name in EditorProject.Languages.Select(l => l + ".csv").Append("Templates.xml"))
            if (File.Exists(Path.Combine(project.DirectoryPath, name)))
            {
                byte[] before = File.ReadAllBytes(Path.Combine(project.DirectoryPath, name));
                byte[] after = File.ReadAllBytes(Path.Combine(result.DirectoryPath, name));
                if (name == "Templates.xml") Check(before.SequenceEqual(after), "target groups preserve template bytes");
                else
                {
                    string language = Path.GetFileNameWithoutExtension(name);
                    var keys = edits.Select(e => e.Id + "_tooltip").ToHashSet();
                    string Mask(byte[] bytes) => Regex.Replace(Encoding.UTF8.GetString(bytes), @"^([^;\r\n]+);([^\r\n]*)",
                        m => keys.Contains(m.Groups[1].Value) ? m.Groups[1].Value + ";" : m.Value, RegexOptions.Multiline);
                    Check(Mask(before) == Mask(after), "target groups preserve all unrelated locale bytes " + name);
                    Check(edits.All(e => result.Text(e.Id, language, "tooltip") == project.Text(e.Id, language, "tooltip")), "target descriptions survive packed export " + name);
                }
            }
        foreach (var edit in edits) project.Undo();
        Check(!project.HasChanges, "all target count changes undo exactly");
        return passed;
    }
}
