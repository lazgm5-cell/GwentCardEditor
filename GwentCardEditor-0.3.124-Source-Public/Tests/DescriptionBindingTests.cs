using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GwentCardEditor;

internal static class DescriptionBindingTests
{
    public static IReadOnlyList<string> Run(string fixture, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        foreach (var (text, expected) in new[]
        {
            ("{Damage}", "Direct"), ("{ Damage }", "Direct"), ("{Damage+B.P.BB_Raid}", "Formula"),
            ("{B.P.BB_Raid + Damage}", "Formula"), ("{(Damage-1)*2/3}", "Formula"),
            ("{--Damage}", "Formula"), ("{Damage2}", "Unlinked"), ("{NotDamage}", "Unlinked"),
            ("{B.P.Damage}", "Unlinked"), ("Deal 3 damage", "Unlinked"), ("{damage}", "CaseMismatch"),
            ("{damage + 1}", "CaseMismatch"), ("{Damage?1:2}", "UnknownExpression"),
            ("{Damage +}", "UnknownExpression"), ("{Damage 1}", "UnknownExpression"),
            ("{Damage + (2}", "UnknownExpression"), ("{Damage(2)}", "UnknownExpression"),
            ("{Damage} {Damage?}", "UnknownExpression"), ("{Damage} {damage}", "CaseMismatch"),
            ("{Damage + " + new string('(', 40) + "1" + new string(')', 40) + "}", "UnknownExpression")
        }) Check(DescriptionExpression.Binding(text, ["Damage"]) == expected, "description expression " + text + " => " + expected);
        Check(DescriptionExpression.Binding("{MaxTargets}", ["MinTargets", "MaxTargets"]) == "Direct", "group aliases accept a linked member");
        string folder = Path.Combine(scratch, "description-bindings"); Directory.CreateDirectory(folder);
        var templates = XDocument.Load(Path.Combine(fixture, "Templates.xml"));
        new XDocument(new XElement("Templates", new XElement(templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11)))).Save(Path.Combine(folder, "Templates.xml"));
        File.WriteAllText(Path.Combine(folder, "Abilities.xml"), """
        <Abilities><Ability Type="CardAbility" Id="11" NId="1000"><TemporaryVariables><e0 Type="IntVar" Id="200" Name="Damage" V="3"/></TemporaryVariables><PersistentVariables/>
        <Nodes><e0 Type="PlayedTrigger" Id="1"><FlowOut Type="FlowConnector" Id="2"/></e0>
        <e1 Type="GetVarNode" Id="3" VarId="200"><Out Type="IntVar" Id="4" V="0"/></e1>
        <e2 Type="ChangePowerNode" Id="5"><FlowIn Type="FlowConnector" Id="6"/><Power Type="IntVar" Id="7" V="0"/>
        <Operation Type="EnumVar`1" GArg="ECardPowerOp" Id="8" V="1"/><TargetedPower Type="EnumVar`1" GArg="EPowerType" Id="9" V="0"/></e2></Nodes>
        <Connections><e0 Type="Connection" Id="300" SrcId="2" DstId="6"/><e1 Type="Connection" Id="301" SrcId="4" DstId="7"/></Connections></Ability></Abilities>
        """);
        foreach (var (locale, text) in new[] { ("en-us", "Deal {Damage} damage."), ("ru-ru", "Damage: 3."), ("zh-cn", "{Damage+B.P.Bonus}") })
            File.WriteAllText(Path.Combine(folder, locale + ".csv"), "11_name;Card\n11_tooltip;" + text + "\n", new UTF8Encoding(false));
        var project = new EditorProject(folder);
        var bindings = project.DescriptionBindings(11, [200]).ToDictionary(b => b.Language);
        Check(bindings["en-us"].Kind == "Direct" && bindings["ru-ru"].Kind == "Unlinked" && bindings["zh-cn"].Kind == "Formula", "description linkage isolated by locale");
        Check(!project.HasChanges && project.PendingDescriptionWarnings().Count == 0, "diagnostics do not modify project or warn on clean state");
        CardEdit Edit(int value) => project.CurrentEdit(11) with { Numbers = new Dictionary<int, int> { [200] = value } };
        Check(project.DescriptionWarnings(11, Edit(3)).Count == 0, "no description warning for numeric no-op");
        var warnings = project.DescriptionWarnings(11, Edit(4));
        Check(warnings is [{ Language: "ru-ru", Kind: "Unlinked", TextEdited: false }], "apply warns only for affected unlinked language");
        project.Apply(11, Edit(4));
        Check(project.PendingDescriptionWarnings() is [{ Language: "ru-ru", Kind: "Unlinked" }], "export warns for current numeric diff");
        string output = Path.Combine(scratch, "description-export"); project.Export(output);
        using (var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "card-editor-changes.json"))))
        {
            Check(report.RootElement.GetProperty("Version").GetString() == "0.3.109", "export manifest reports current version");
            Check(report.RootElement.GetProperty("DescriptionWarnings").GetArrayLength() == 1, "description warnings included in export manifest");
        }
        foreach (string locale in project.AvailableLanguages)
            Check(File.ReadAllBytes(Path.Combine(folder, locale + ".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, locale + ".csv"))), "description diagnostics preserve CSV " + locale);
        project.Undo(); Check(!project.HasChanges && project.PendingDescriptionWarnings().Count == 0, "description warning clears after undo");
        var manual = Edit(4);
        manual = manual with { Texts = manual.Texts.ToDictionary(p => p.Key, p => p.Key == "ru-ru"
            ? (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["name"] = "Card", ["tooltip"] = "Damage: 4." } : p.Value) };
        Check(project.DescriptionWarnings(11, manual) is [{ TextEdited: true }], "manual literal edit is recorded without assuming semantic correctness");
        project.Apply(11, manual);
        Check(project.PendingDescriptionWarnings() is [{ TextEdited: true }], "pending diagnostics retain manual text edit flag");
        project.Reset(); Check(project.PendingDescriptionWarnings().Count == 0, "description warnings clear after reset");
        return passed;
    }

    public static IReadOnlyList<string> Real(EditorProject source)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var attack = source.Numbers(152202).Single(n => n.Editable && n.Role == "base-damage");
        Check(source.DescriptionBindings(152202, [attack.Id]).Any(b => b.Language == "en-us" && b.Kind == "Formula"), "original raid formula recognized");
        var charges = source.DescriptionBindings(122105, [-1]);
        Check(charges.Where(b => source.HasText(122105, b.Language, "tooltip")).All(b => b.Kind == "Direct"), "synthetic leader charges resolve real presentation alias");
        Check(charges.Any(b => b.Language == "zh-tw" && b.Kind == "MissingText"), "missing native localized entry explicitly reported");
        var refill = source.Cards.First(c => source.ChargeRefill(c.Id) != null);
        Check(source.DescriptionBindings(refill.Id, [-5]).Where(b => source.HasText(refill.Id, b.Language, "tooltip")).All(b => b.Kind == "Direct"), "synthetic refill resolves presentation alias");
        var timing = source.Cards.SelectMany(c => source.Timing(c.Id).Select(t => (c.Id, Timing: t))).First();
        Check(source.DescriptionBindings(timing.Id, [timing.Timing.Id]).Any(b => b.Linked), "synthetic timing resolves presentation alias");
        var bomb = source.Numbers(202122).Single(n => n.Editable && n.Role == "alternate-damage");
        Check(source.DescriptionBindings(202122, [bomb.Id]).Any(b => b.Language == "en-us" && b.Kind == "Unlinked"), "original literal bomb description not treated as linked");
        Check(!source.HasChanges, "description audit leaves original client clean");
        return passed;
    }
}
