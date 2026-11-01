using System.Globalization;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private AbilityDocument? originalAbilityDocument;
    private Dictionary<int, XElement>? abilityIndex;
    private Dictionary<int, XElement> abilityOverrides = new();
    private void EnsureAbilities()
    {
        if (abilityIndex != null) return;
        string path = Path.Combine(DirectoryPath, "Abilities.xml");
        if (!File.Exists(path)) { abilityIndex = new(); return; }
        originalAbilityDocument = AbilityDocument.Load(path);
        abilityIndex = originalAbilityDocument.Document.Root?.Elements("Ability")
            .Where(x => (string?)x.Attribute("Type") == "CardAbility")
            .ToDictionary(x => (int)x.Attribute("Id")!) ?? new();
    }
    private XElement? OriginalAbility(int id) { EnsureAbilities(); return abilityIndex!.GetValueOrDefault(id); }
    private XElement? CurrentAbility(int id) => abilityOverrides.GetValueOrDefault(id) ?? OriginalAbility(id);
    private bool SharedAbility(int id)
    {
        var ids = Template(id).Element("AbilityIds")?.Elements().Select(x => (int?)x).ToArray() ?? [];
        if (ids.Length != 1 || ids[0] != id) return true;
        return originalIndex.Values.Count(t => t.Element("AbilityIds")?.Elements().Any(e => (int?)e == id) == true) != 1;
    }
    public IReadOnlyList<AbilityNumber> Numbers(int id)
    {
        if(GigaChoice(id))return GigaNumbers(id);
        if(id==202515)return [];
        if (NatureChoice(id)) return NatureNumbers(id);
        XElement? graph = CurrentAbility(id);
        if (graph == null) return Array.Empty<AbilityNumber>();
        if(OwnedFlanking(graph))
        {
            graph=FlankingBaseline(graph,out _);
            if(graph==null)return [];
        }
        string tooltip = string.Join("\n", AvailableLanguages.Select(l => Text(id, l, "tooltip")));
        var values = AbilityNumbers.Inspect(graph, SharedAbility(id), tooltip).ToList();
        var addedIds=TriggerModuleIds(id);
        values=values.Select(n=>addedIds.Contains(n.Id)?n with {Editable=false,Reason="AdditionalAbilityUnsupported"}:n).ToList();
        values=NativeKeywordNumbers(id,graph,values);
        values=CardClarityNumbers(id,graph,values);
        if (id == 142108 && SchirruPresentation()) values = values.Select(n => n.Id == 385
            ? n with { Editable = false, Reason = "DestructionPresentation" } : n).ToList();
        if (BarnabasContract(id)) values = values.Select(n => n.Id == 893
            ? n with { LabelRole = "barnabas-boost", HelpKey = "BarnabasBoost" }
            : n.Role is "min-targets" or "max-targets" ? n with { Editable = false, Reason = "SingleSelectionOnly" } : n).ToList();
        if (id == 202674) values = values.Select(n => n.Id is 1133 or 1150 or 1151
            ? n with { Editable = false, Reason = "ChoiceEffectOwned" } : n).ToList();
        if (id == 200228) values = values.Select(n => n.Id is 455 or 689
            ? n with { Editable = false, Reason = "RandomDamageQuantum" } : n).ToList();
        if (values.Any(n => n.Editable && n.Role == "copy-count"))
        {
            var copyAbilities = originalAbilityDocument?.Document.Root?.Elements("Ability")
                .Where(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == 30).ToArray() ?? [];
            var bondedAbilities=originalAbilityDocument?.Document.Root?.Elements("Ability").Where(e=>(string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==10).ToArray()??[];
            values = CopyAbilityMeaning.Apply(graph, copyAbilities.Length == 1 ? copyAbilities[0] : null, values,bondedAbilities.Length==1?bondedAbilities[0]:null);
        }
        if (Card(id).Type == 1) return LeaderNumbers(id, values);
        XElement template = Template(id);
        if ((string?)template.Element("TapActive") == "True")
        {
            var setting = InitialCharges(id);
            if (setting.RefillValues != null)
                values = values.Select(v => setting.RefillValues.Contains(v.Id) ? v with { Editable = false, Reason = "InitialValueLocked" } : v).ToList();
            values.Add(new(-1, "initial-charges", "InitalTapCharges", setting.Value, 1, 9, setting.Editable, setting.Reason, "Order", 1, setting.Editable));
            var refill = FixedChargeRefill(graph, setting);
            if (refill != null) values.Add(new(-5, "charge-refill", "ChargeRefill", refill.Value, 1, 9, true, "", "Reaction", 1, true));
        }
        if (InitialPatience(id) is { } patience)
            values.Add(new(-6, "initial-patience", "InitialPatience", patience.Value, 0, 99, true, "", "Ability", 1, true));
        values.AddRange(VerifiedEffects(id).Select(s => new AbilityNumber(s.Id, s.Role, s.Role, s.Value,
            s.Minimum, s.Maximum, true, "", s.Context, 1, true)));
        var timing = Timing(id);
        values.AddRange(timing.Select(s => new AbilityNumber(s.Id, s.Role, s.Role, s.Value, 1, 99, true, "", s.Id == -3 ? "Order" : s.Id == -4 ? "Deploy" : "Ability", s.Id == -4 ? 2 : 1, true)));
        if (Int(template, "InitialTimer") >= 0 && timing.All(s => s.Id != -2))
            values.Add(new(-2, "initial-timer", "InitialTimer", Int(template, "InitialTimer"), -1, 999, false, "InitialValueLocked", "Ability", 1, true));
        return Card(id).Editable ? values : values.Select(v => v with { Editable = false, Reason = Card(id).BlockReason }).ToArray();
    }
    private XElement? PrepareNumbers(int id, IReadOnlyDictionary<int, int>? edits)
    {
        if (edits == null || edits.Count == 0) return SyncBanArdDescription(id, null);
        if(GigaChoice(id))return PrepareGiga(id,edits).GetValueOrDefault(id);
        if(id==202515)throw new InvalidOperationException("ComputedEffectUnsupported");
        if(edits.Keys.Any(TriggerModuleIds(id).Contains))throw new InvalidOperationException("AdditionalAbilityUnsupported");
        ValidateNativeKeywordNumbers(id,edits);
        if (NatureChoice(id)) return PrepareNature(id, edits).GetValueOrDefault(id);
        if (id == 142108 && SchirruPresentation() && edits.ContainsKey(385)) throw new InvalidOperationException("DestructionPresentation");
        if (BarnabasContract(id) && edits.Keys.Any(k => k is 903 or 904 or 975 or 976 or 1037 or 1038)) throw new InvalidOperationException("SingleSelectionOnly");
        if (id == 202674 && edits.Keys.Any(k => k is 1133 or 1150 or 1151)) throw new InvalidOperationException("ChoiceEffectOwned");
        if (id == 200228 && edits.Keys.Any(k => k is 455 or 689)) throw new InvalidOperationException("RandomDamageQuantum");
        if (!Card(id).Editable) throw new InvalidOperationException(Card(id).BlockReason);
        XElement graph = CurrentAbility(id) ?? throw new InvalidOperationException("NoAbilityNumbers");
        FlankingBinding? flanking=null;
        if(OwnedFlanking(graph))graph=FlankingBaseline(graph,out flanking)??throw new InvalidOperationException("AdditionalAbilityUnsupported");
        if(edits.Keys.Any(CardClarityLocks(id,graph).Contains))throw new InvalidOperationException("InitialValueLocked");
        if (Card(id).Type == 1) return PrepareLeaderNumbers(id, graph, edits);
        var chargeEdit = InitialChargeEdit(id, edits);
        var refillEdit = ChargeRefillEdit(id, edits);
        var timingEdits = TimingEdits(id, edits);
        var patienceEdit = InitialPatienceEdit(id, edits);
        var effectEdits = VerifiedEffectEdits(id, edits);
        var nativeEdits=NativeKeywordEdits(id,edits);
        var graphEdits = edits.Where(e => !nativeEdits.ContainsKey(e.Key) && e.Key is not (-1 or -2 or -3 or -4 or -5 or -6 or -7 or -8 or -9 or -11 or -12)).ToDictionary(e => e.Key, e => e.Value);
        var lockedRefill = InitialCharges(id).RefillValues;
        if (lockedRefill != null && graphEdits.Keys.Any(lockedRefill.Contains)) throw new InvalidOperationException("InitialValueLocked");
        var next = graphEdits.Count == 0 ? new XElement(graph) : AbilityNumbers.Prepare(Template(id), graph, graphEdits, SharedAbility(id),
            string.Join("\n", AvailableLanguages.Select(l => Text(id, l, "tooltip"))));
        foreach(var (variable,value) in nativeEdits)
            next.Descendants().Single(e=>(int?)e.Attribute("Id")==variable).SetAttributeValue("V",value.ToString(CultureInfo.InvariantCulture));
        if (chargeEdit.HasValue)
            next.Descendants().Single(e => (int?)e.Attribute("Id") == chargeEdit.Value.VariableId)
                .SetAttributeValue("V", chargeEdit.Value.Value.ToString(CultureInfo.InvariantCulture));
        if (refillEdit.HasValue)
        {
            var (setting, value) = refillEdit.Value;
            foreach (int variable in new int?[] { setting.TextVariableId, setting.LiteralVariableId }.OfType<int>())
                next.Descendants().Single(e => (int?)e.Attribute("Id") == variable).SetAttributeValue("V", value.ToString(CultureInfo.InvariantCulture));
        }
        if (patienceEdit.HasValue)
            next.Descendants().Single(e => (int?)e.Attribute("Id") == patienceEdit.Value.Setting.VariableId)
                .SetAttributeValue("V", patienceEdit.Value.Value.ToString(CultureInfo.InvariantCulture));
        foreach (var (variable, value) in effectEdits)
            next.Descendants().Single(e => (int?)e.Attribute("Id") == variable).SetAttributeValue("V", value.ToString(CultureInfo.InvariantCulture));
        foreach (var (setting, value) in timingEdits)
            foreach (int variable in setting.GraphValues.Append(setting.TextVariableId))
                next.Descendants().Single(e => (int?)e.Attribute("Id") == variable).SetAttributeValue("V", value.ToString(CultureInfo.InvariantCulture));
        next = SyncBanArdDescription(id, next)!;
        if(flanking!=null)next=WrapFlanking(next,flanking);
        if (GraphValidator.Validate(next).ErrorCount > 0) throw new InvalidOperationException("GraphHasErrors");
        return next;
    }
    private static IReadOnlyList<Change> NumberChanges(int id, XElement before, XElement after)
    {
        var a = before.Descendants().Where(x => (string?)x.Attribute("Type") == "IntVar" && (int?)x.Attribute("Id") != null)
            .ToDictionary(x => (int)x.Attribute("Id")!);
        return after.Descendants().Where(x => (string?)x.Attribute("Type") == "IntVar" && (int?)x.Attribute("Id") != null)
            .Where(x=>!((int?)x.Attribute("Id")==22 && GigaIsolationChanged(id,before,after)))
            .Where(x=>!x.Ancestors().Any(n=>TriggerAbilitySpecs.Any(spec=>TriggerComment(n,spec))))
            .Where(x => a.ContainsKey((int)x.Attribute("Id")!) && (string?)a[(int)x.Attribute("Id")!].Attribute("V") != (string?)x.Attribute("V"))
            .Select(x => new Change($"{id}/Ability/#{(int)x.Attribute("Id")!}",
                (string?)a[(int)x.Attribute("Id")!].Attribute("V") ?? "", (string?)x.Attribute("V") ?? "")).ToArray();
    }
    private void ExportAbilityNumbers(string staging)
    {
        if (abilityOverrides.Count == 0) return;
        EnsureAbilities();
        var document = new XDocument(originalAbilityDocument!.Document);
        foreach (var graph in document.Root!.Elements("Ability").Where(x => (string?)x.Attribute("Type") == "CardAbility").ToArray())
            if ((int?)graph.Attribute("Id") is int id && abilityOverrides.TryGetValue(id, out var replacement))
                graph.ReplaceWith(new XElement(replacement));
        string path = Path.Combine(staging, "Abilities.xml");
        ExportGordCounter(document);
        document.Save(path, SaveOptions.DisableFormatting);
        _ = AbilityDocument.Load(path);
    }
}
