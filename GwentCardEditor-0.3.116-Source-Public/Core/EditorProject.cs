using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed record CardEntry(int Id, string DebugName, int Faction, int SecondaryFaction,
    int Rarity, int Tier, int Type, int Power, int Armor, int Provision, int Availability,
    bool Editable, string BlockReason, bool IsTutorial, bool IsToken);

public sealed record CardEdit(int Power, int Armor, int Provision, int Tier, int Rarity,
    int Faction, IReadOnlySet<string> Categories,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Texts,
    string? PrimaryCategory = null, IReadOnlyDictionary<int, int>? Numbers = null, bool? Zeal = null,
    IReadOnlyDictionary<string, bool>? Statuses = null, bool? Symbiosis = null, bool? Assimilate = null, bool? Harmony = null, bool? Intimidate = null, bool? Thrive = null, bool? Veteran = null, bool? Formation = null,
    IReadOnlyDictionary<string,int>? AdditionalValues = null, bool? Profit = null, bool? Resupply = null, bool? Echo = null, bool? Flanking = null, bool? Insanity = null,
    int? SecondaryFaction = null);

public sealed record Change(string Field, string Before, string After);

public sealed partial class EditorProject
{
    public static readonly int[] Factions = [1, 2, 8, 16, 32, 4, 64];
    public static readonly int[] Rarities = [1, 2, 4, 8];
    public static readonly string[] Languages = ["ru-ru", "en-us", "zh-cn", "pl-pl", "de-de", "pt-br", "fr-fr", "ja-jp", "it-it", "es-es", "es-mx", "zh-tw", "ko-kr"];
    public static readonly string[] TextFields = ["name", "tooltip", "fluff"];
    private readonly XDocument original;
    private readonly Dictionary<int, XElement> originalIndex;
    private XDocument working;
    private Dictionary<int, XElement> index = new();
    private readonly Dictionary<string, string> sourceHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LocaleFile> locales = new();
    private Dictionary<string, Dictionary<string, string>> textEdits = new();
    private sealed record Snapshot(XDocument Xml, Dictionary<string, Dictionary<string, string>> Text, Dictionary<int, XElement> Abilities);
    private sealed record HistoryItem(Snapshot State, string Label);
    private readonly Stack<HistoryItem> history = new();
    private readonly Stack<HistoryItem> redo = new();
    private readonly List<(string Package, string OutputHash)> successfulClientExports = new();
    public string DirectoryPath { get; }
    public ClientSource? Client { get; }
    public string SourcePath => Client?.ArchivePath ?? DirectoryPath;
    public bool CanUndo => history.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public IReadOnlyList<string> HistoryLabels => history.Reverse().Select(x => x.Label).ToArray();
    public IReadOnlyList<string> RedoLabels => redo.Select(x => x.Label).ToArray();
    public bool ExportBalanceCouncil { get; set; }
    public BalanceSnapshot? ExportBalanceSnapshot { get; set; }
    public IReadOnlyList<string> AvailableLanguages => locales.Keys.ToArray();
    public bool HasChanges => !XNode.DeepEquals(original, working) || textEdits.Values.Any(x => x.Count > 0) || abilityOverrides.Count > 0;

    public EditorProject(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        string metadata = Path.Combine(DirectoryPath, ClientSource.MetadataFile);
        if (File.Exists(metadata)) Client = JsonSerializer.Deserialize<ClientSource>(File.ReadAllText(metadata))
            ?? throw new InvalidDataException("Invalid client source metadata.");
        string templatePath = Path.Combine(DirectoryPath, "Templates.xml");
        original = XDocument.Load(templatePath, LoadOptions.PreserveWhitespace);
        if (original.Root?.Name != "Templates") throw new InvalidDataException("Invalid Templates.xml root.");
        var ids = original.Root.Elements("Template").Where(IsCard).Select(x => (int?)x.Attribute("Id")).ToArray();
        if (ids.Any(x => !x.HasValue || x <= 0) || ids.Distinct().Count() != ids.Length)
            throw new InvalidDataException("Invalid or duplicate card IDs.");
        originalIndex = original.Root.Elements("Template").Where(IsCard).ToDictionary(x => (int)x.Attribute("Id")!);
        working = new XDocument(original);
        Reindex();
        foreach (string path in SourceFiles()) sourceHashes[Path.GetFileName(path)] = Hash(path);
        foreach (string language in Languages)
        {
            string file = Path.Combine(DirectoryPath, language + ".csv");
            if (File.Exists(file)) locales[language] = new LocaleFile(file);
        }
    }

    private IEnumerable<string> SourceFiles() => Directory.EnumerateFiles(DirectoryPath)
        .Where(p => Path.GetExtension(p).Equals(".xml", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(p).Equals(".csv", StringComparison.OrdinalIgnoreCase));
    private static bool IsCard(XElement x) => (string?)x.Attribute("Type") == "CardTemplate";
    private static int Int(XElement e, string name) => int.TryParse(e.Element(name)?.Value,
        NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : -1;
    private void Reindex() => index = working.Root!.Elements("Template").Where(IsCard).ToDictionary(x => (int)x.Attribute("Id")!);
    private XElement Template(int id, XDocument? doc = null) => doc == null ? index[id]
        : ReferenceEquals(doc, original) ? originalIndex[id] : doc.Root!.Elements("Template").Single(x => IsCard(x) && (int?)x.Attribute("Id") == id);

    private static CardEntry Read(XElement e)
    {
        int type = Int(e, "Type"), tier = Int(e, "Tier"), faction = Int(e, "FactionId"), secondaryFaction = Int(e, "SecondaryFactionId");
        int availability = (int?)e.Attribute("Availability") ?? -1;
        string debugName = (string?)e.Attribute("DebugName") ?? "";
        bool tutorial = debugName.Contains("tutorial", StringComparison.OrdinalIgnoreCase) || debugName.Contains("ftux", StringComparison.OrdinalIgnoreCase);
        bool token = (Mask(e, 1) & 512UL) != 0;
        string reason = tutorial ? "TutorialHidden" : CardCatalog.IsHidden((int)e.Attribute("Id")!, debugName) ? "HiddenOrGenerated" : type is not (1 or 2 or 4 or 8 or 16) ? "UnsupportedType"
            : (type == 1 ? tier != 1 || token || Int(e, "Power") != 0 || Int(e, "Armor") != 0
                || Int(e, "SecondaryFactionId") != 0 || e.Element("Provision")?.HasAttributes == true
                || e.Element("Provision")?.HasElements == true : tier is not (2 or 8))
            || !Factions.Contains(faction) || secondaryFaction != 0 && (!Factions.Contains(secondaryFaction) || secondaryFaction == faction)
            || !Rarities.Contains(Int(e, "Rarity"))
            || Int(e, "Power") < 0 || Int(e, "Armor") < 0 || Int(e, "Provision") < 0 || !SupportedStructure(e)
                ? "UnsupportedFields" : "";
        return new((int)e.Attribute("Id")!, (string?)e.Attribute("DebugName") ?? "", faction,
            secondaryFaction, Int(e, "Rarity"), tier, type, Int(e, "Power"),
            Int(e, "Armor"), Int(e, "Provision"), availability, reason.Length == 0, reason, tutorial, token);
    }
    private static bool SupportedStructure(XElement template)
    {
        foreach (string field in new[] { "Power", "Armor", "Provision", "Tier", "Rarity", "FactionId", "SecondaryFactionId", "Type" })
            if (template.Elements(field).Count() != 1 || Int(template, field) < 0) return false;
        foreach (string field in new[] { "Categories", "PrimaryCategory" })
        {
            if (template.Elements(field).Count() != 1) return false;
            for (int slot = 0; slot < 3; slot++)
            {
                var elements = template.Element(field)!.Elements("e" + slot).ToArray();
                if (elements.Length != 1 || !ulong.TryParse((string?)elements[0].Attribute("V"), out _)) return false;
            }
        }
        return true;
    }

    public IReadOnlyList<CardEntry> Cards => index.Values.Select(Read).ToArray();
    public CardEntry Card(int id) => Read(Template(id));
    public string Name(int id, string language) => Text(id, language, "name") is string value && value.Length > 0
        ? value : Card(id).DebugName;
    public bool HasText(int id, string language, string field) => locales.TryGetValue(language, out var locale)
        && locale.Values.ContainsKey($"{id}_{field}");
    public string Text(int id, string language, string field)
    {
        string key = $"{id}_{field}";
        return textEdits.TryGetValue(language, out var edits) && edits.TryGetValue(key, out string? value)
            ? value : locales.TryGetValue(language, out var locale) ? locale.Values.GetValueOrDefault(key, "") : "";
    }

    public IReadOnlySet<string> Categories(int id)
    {
        XElement template = Template(id);
        return CategoryRegistry.All.Where(c => (Mask(template, c.Slot) & c.Mask) == c.Mask)
            .Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    private static ulong Mask(XElement e, int slot) => ulong.TryParse(
        (string?)e.Element("Categories")?.Element("e" + slot)?.Attribute("V"), out ulong v) ? v : 0;

    public string? PrimaryCategory(int id)
    {
        XElement? primary = Template(id).Element("PrimaryCategory");
        if (primary == null) return null;
        ulong[] masks = Enumerable.Range(0, 3).Select(i => ulong.TryParse((string?)primary.Element("e" + i)?.Attribute("V"), out ulong v) ? v : ulong.MaxValue).ToArray();
        if (masks.All(x => x == 0)) return "";
        return CategoryRegistry.All.FirstOrDefault(c => Enumerable.Range(0, 3).All(i => masks[i] == (i == c.Slot ? c.Mask : 0)))?.Name;
    }

    public CardEdit CurrentEdit(int id) => new(Card(id).Power, Card(id).Armor, Card(id).Provision,
        Card(id).Tier, Card(id).Rarity, Card(id).Faction, Categories(id),
        AvailableLanguages.ToDictionary(l => l, l => (IReadOnlyDictionary<string, string>)TextFields
            .Where(f => HasText(id, l, f)).ToDictionary(f => f, f => Text(id, l, f))), PrimaryCategory(id), Zeal: InitialZeal(id),
        SecondaryFaction: Card(id).SecondaryFaction);

    public IReadOnlyList<Change> Preview(int id, CardEdit edit)
    {
        edit = NormalizeAdditionalAbilities(id, edit);
        edit = NormalizeSelectedDrawText(id, NormalizeMoveCountText(id, NormalizeDrawCountText(id, NormalizeTargetCountText(id, edit))));
        edit = NormalizeInitialStatusText(id, edit);
        XElement before = Template(id);
        XElement after = Prepare(id, edit);
        var changes = new List<Change>();
        foreach (string field in new[] { "Power", "Armor", "Provision", "Tier", "Rarity", "FactionId", "SecondaryFactionId", "Categories", "PrimaryCategory", "SummoningSickness", "InitalTapCharges", "InitialTimer", "Tokens", "Madness" })
        {
            string a = before.Element(field)?.ToString(SaveOptions.DisableFormatting) ?? "";
            string b = after.Element(field)?.ToString(SaveOptions.DisableFormatting) ?? "";
            if (a != b) changes.Add(new(field,
                field == "Categories" ? string.Join(", ", Categories(id)) : field == "PrimaryCategory" ? PrimaryCategory(id) ?? "?" : before.Element(field)!.Value,
                field == "Categories" ? string.Join(", ", edit.Categories.Order()) : field == "PrimaryCategory" ? edit.PrimaryCategory ?? "?" : after.Element(field)!.Value));
        }
        foreach (var (language, fields) in edit.Texts)
            foreach (var (field, value) in fields)
                if (Text(id, language, field) != value) changes.Add(new($"{language}/{field}", Text(id, language, field), value));
        XElement? graph = PrepareHarmony(id,PrepareAssimilate(id,PrepareAdditionalAbilities(id, PrepareNumbers(id, edit.Numbers), edit),edit),edit);
        graph=PrepareIntimidate(id,graph,edit);
        graph=PrepareThrive(id,graph,edit);
        graph=PrepareVeteran(id,graph,edit);
        graph=PrepareFormation(id,graph,edit);
        graph=PrepareProfit(id,graph,edit);
        graph=PrepareResupply(id,graph,edit);
        graph=PrepareEcho(id,graph,edit);
        graph=PrepareFlanking(id,graph,edit);
        graph=PrepareInsanity(id,graph,edit);
        if (graph != null) changes.AddRange(NumberChanges(id, CurrentAbility(id)!, graph));
        if (graph != null) changes.AddRange(TriggerValueChanges(CurrentAbility(id),graph));
        if (edit.Symbiosis.HasValue && edit.Symbiosis.Value != AdditionalSymbiosis(id).Value)
            changes.Add(new("AdditionalSymbiosis",AdditionalSymbiosis(id).Value.ToString(),edit.Symbiosis.Value.ToString()));
        if(edit.Assimilate.HasValue && edit.Assimilate.Value!=AdditionalAssimilate(id).Value)
            changes.Add(new("AdditionalAssimilate",AdditionalAssimilate(id).Value.ToString(),edit.Assimilate.Value.ToString()));
        if(edit.Harmony.HasValue && edit.Harmony.Value!=AdditionalHarmony(id).Value)
            changes.Add(new("AdditionalHarmony",AdditionalHarmony(id).Value.ToString(),edit.Harmony.Value.ToString()));
        if(edit.Intimidate.HasValue && edit.Intimidate.Value!=AdditionalIntimidate(id).Value)
            changes.Add(new("AdditionalIntimidate",AdditionalIntimidate(id).Value.ToString(),edit.Intimidate.Value.ToString()));
        if(edit.Thrive.HasValue && edit.Thrive.Value!=AdditionalThrive(id).Value)
            changes.Add(new("AdditionalThrive",AdditionalThrive(id).Value.ToString(),edit.Thrive.Value.ToString()));
        if(edit.Veteran.HasValue && edit.Veteran.Value!=AdditionalVeteran(id).Value)
            changes.Add(new("AdditionalVeteran",AdditionalVeteran(id).Value.ToString(),edit.Veteran.Value.ToString()));
        if(edit.Formation.HasValue && edit.Formation.Value!=AdditionalFormation(id).Value)
            changes.Add(new("AdditionalFormation",AdditionalFormation(id).Value.ToString(),edit.Formation.Value.ToString()));
        if(edit.Profit.HasValue && edit.Profit.Value!=AdditionalProfit(id).Value)
            changes.Add(new("AdditionalProfit",AdditionalProfit(id).Value.ToString(),edit.Profit.Value.ToString()));
        if(edit.Resupply.HasValue && edit.Resupply.Value!=AdditionalResupply(id).Value)
            changes.Add(new("AdditionalResupply",AdditionalResupply(id).Value.ToString(),edit.Resupply.Value.ToString()));
        if(edit.Echo.HasValue && edit.Echo.Value!=AdditionalEcho(id).Value)
            changes.Add(new("AdditionalEcho",AdditionalEcho(id).Value.ToString(),edit.Echo.Value.ToString()));
        if(edit.Flanking.HasValue && edit.Flanking.Value!=AdditionalFlanking(id).Value)
            changes.Add(new("AdditionalFlanking",AdditionalFlanking(id).Value.ToString(),edit.Flanking.Value.ToString()));
        foreach (var (owner, linked) in PrepareChoiceEffects(id, edit.Numbers).Where(p => p.Key != id))
        {
            changes.AddRange(NumberChanges(owner, CurrentAbility(owner)!, linked));
            if(GigaIsolationChanged(owner,CurrentAbility(owner),linked))changes.Add(new("GigaChoiceIsolation",(GigaGraphMode(CurrentAbility(owner))==1).ToString(),(GigaGraphMode(linked)==1).ToString()));
        }
        return changes;
    }

    private XElement Prepare(int id, CardEdit edit)
    {
        CardEntry card = Card(id);
        if (!card.Editable) throw new InvalidOperationException(card.BlockReason);
        ValidateNumber(edit.Power, card.Power, 1, 999, "Power", card.Type == 4);
        ValidateNumber(edit.Armor, card.Armor, 0, 999, "Armor", card.Type == 4);
        ValidateNumber(edit.Provision, card.Provision, 0, 99, "Provision", card.Type != 16);
        int secondaryFaction = edit.SecondaryFaction ?? card.SecondaryFaction;
        if (card.Type == 1 && (edit.Tier != card.Tier || edit.Rarity != card.Rarity || edit.Faction != card.Faction
            || secondaryFaction != card.SecondaryFaction
            || !Categories(id).SetEquals(edit.Categories) || edit.PrimaryCategory != PrimaryCategory(id)))
            throw new InvalidOperationException("LeaderFieldsLocked");
        if ((card.Type == 1 ? edit.Tier != 1 : edit.Tier is not (2 or 8)) || !Rarities.Contains(edit.Rarity) || !Factions.Contains(edit.Faction)
            || secondaryFaction != 0 && !Factions.Contains(secondaryFaction))
            throw new InvalidOperationException("UnsupportedFields");
        if (secondaryFaction == edit.Faction)
            throw new InvalidOperationException("SecondaryFactionInvalid");
        XElement copy = new(Template(id));
        if(edit.Insanity.HasValue && edit.Insanity.Value!=AdditionalInsanity(id).Value)
        {
            if(!AdditionalInsanity(id).Editable)throw new InvalidOperationException("AdditionalAbilityUnsupported");
            copy.Element("Madness")!.Value=edit.Insanity.Value.ToString();
        }
        PrepareInitialStatuses(id, copy, edit.Statuses);
        var initialChargeEdit = InitialChargeEdit(id, edit.Numbers);
        if (initialChargeEdit.HasValue) copy.Element("InitalTapCharges")!.Value = initialChargeEdit.Value.Value.ToString(CultureInfo.InvariantCulture);
        foreach (var (setting, value) in TimingEdits(id, edit.Numbers).Where(e => e.Setting.Id == -2))
            copy.Element("InitialTimer")!.Value = value.ToString(CultureInfo.InvariantCulture);
        if (edit.Zeal.HasValue && edit.Zeal != InitialZeal(id))
        {
            var setting = Zeal(id);
            if (!setting.Editable) throw new InvalidOperationException(setting.Reason);
            copy.Element("SummoningSickness")!.Value = (!edit.Zeal.Value).ToString();
        }
        foreach (var (key, value) in new[] { ("Power", edit.Power), ("Armor", edit.Armor), ("Provision", edit.Provision),
                     ("Tier", edit.Tier), ("Rarity", edit.Rarity), ("FactionId", edit.Faction), ("SecondaryFactionId", secondaryFaction) })
            copy.Element(key)!.Value = value.ToString(CultureInfo.InvariantCulture);
        var knownNames = CategoryRegistry.All.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (edit.Categories.Any(x => !knownNames.Contains(x))) throw new InvalidOperationException("UnknownCategory");
        foreach (var c in CategoryRegistry.All)
        {
            bool had = Categories(id).Contains(c.Name), has = edit.Categories.Contains(c.Name);
            if (had == has) continue;
            XElement slot = copy.Element("Categories")?.Element("e" + c.Slot)
                ?? throw new InvalidOperationException("UnsupportedFields");
            ulong old = Mask(copy, c.Slot);
            slot.SetAttributeValue("V", has ? old | c.Mask : old & ~c.Mask);
        }
        if (edit.PrimaryCategory != PrimaryCategory(id))
        {
            if (PrimaryCategory(id) == null || edit.PrimaryCategory == null)
                throw new InvalidOperationException("UnknownCategory");
            var category = edit.PrimaryCategory.Length == 0 ? null : CategoryRegistry.Resolve(edit.PrimaryCategory);
            for (int slot = 0; slot < 3; slot++)
                (copy.Element("PrimaryCategory")?.Element("e" + slot)
                    ?? throw new InvalidOperationException("UnsupportedFields"))
                    .SetAttributeValue("V", category?.Slot == slot ? category.Mask : 0UL);
        }
        foreach (var (language, fields) in edit.Texts)
        {
            if (!locales.TryGetValue(language, out var locale)) throw new InvalidOperationException("MissingLocale");
            foreach (var (field, value) in fields)
            {
                string key = $"{id}_{field}";
                if (!TextFields.Contains(field) || !locale.Values.TryGetValue(key, out string? baseline))
                    throw new InvalidOperationException("MissingText");
                string permitted = field == "tooltip" ? AdditionalTextBaseline(id,language,baseline,edit) : baseline;
                if (value == permitted) continue;
                if (value.Contains('\r') || value.Contains('\n') || value.Contains('\0'))
                    throw new InvalidOperationException("PhysicalLine");
                if (field == "name" && string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("EmptyName");
                // Keep the game's formatting and ability references intact in the basic editor.
                if (!Tokens(permitted).SequenceEqual(Tokens(value))
                    || "<>{}".Any(c => permitted.Count(x => x == c) != value.Count(x => x == c)))
                    throw new InvalidOperationException("TextTokens");
            }
        }
        return copy;
    }
    private static IEnumerable<string> Tokens(string value) => Regex.Matches(value, @"<[^>]*>|\{[^}]*\}")
        .Select(m => m.Value);
    private static void ValidateNumber(int value, int old, int min, int max, string name, bool editable)
    {
        if (value == old) return;
        if (!editable || value < min || value > max) throw new InvalidOperationException("Range:" + name);
    }
    private Dictionary<string, Dictionary<string, string>> CloneText() => textEdits.ToDictionary(x => x.Key,
        x => new Dictionary<string, string>(x.Value));
    private Snapshot Capture() => new(new XDocument(working), CloneText(), new Dictionary<int, XElement>(abilityOverrides));
    private void Restore(Snapshot state) { working = state.Xml; textEdits = state.Text; abilityOverrides = state.Abilities; Reindex(); }
    private void Record(string label) { history.Push(new(Capture(), label)); redo.Clear(); }

    public bool Apply(int id, CardEdit edit)
    {
        CardEdit requested = edit;
        edit = NormalizeAdditionalAbilities(id, edit);
        edit = NormalizeSelectedDrawText(id, NormalizeMoveCountText(id, NormalizeDrawCountText(id, NormalizeTargetCountText(id, edit))));
        edit = NormalizeInitialStatusText(id, edit);
        XElement next = Prepare(id, edit);
        XElement? graph = PrepareHarmony(id,PrepareAssimilate(id,PrepareAdditionalAbilities(id, PrepareNumbers(id, edit.Numbers), edit),edit),edit);
        graph=PrepareIntimidate(id,graph,edit);
        graph=PrepareThrive(id,graph,edit);
        graph=PrepareVeteran(id,graph,edit);
        graph=PrepareFormation(id,graph,edit);
        graph=PrepareProfit(id,graph,edit);
        graph=PrepareResupply(id,graph,edit);
        graph=PrepareEcho(id,graph,edit);
        graph=PrepareFlanking(id,graph,edit);
        graph=PrepareInsanity(id,graph,edit);
        if (Preview(id, requested).Count == 0) return false;
        var linkedGraphs = PrepareChoiceEffects(id, edit.Numbers);
        Record($"Edit: {Name(id, "en-us")} ({id})");
        Template(id).ReplaceWith(next);
        index[id] = next;
        if (graph != null)
        {
            if (XNode.DeepEquals(graph, OriginalAbility(id))) abilityOverrides.Remove(id);
            else abilityOverrides[id] = graph;
        }
        foreach (var (owner, linked) in linkedGraphs.Where(p => p.Key != id))
        {
            if (XNode.DeepEquals(linked, OriginalAbility(owner))) abilityOverrides.Remove(owner);
            else abilityOverrides[owner] = linked;
        }
        foreach (var (language, fields) in edit.Texts)
        {
            if (!textEdits.ContainsKey(language)) textEdits[language] = new();
            foreach (var (field, value) in fields)
            {
                string key = $"{id}_{field}";
                if (locales[language].Values[key] == value) textEdits[language].Remove(key);
                else textEdits[language][key] = value;
            }
        }
        return true;
    }
    public int ApplyBatch(IReadOnlyList<(int Id, CardEdit Edit)> edits)
    {
        if (edits.Select(x => x.Id).Distinct().Count() != edits.Count)
            throw new InvalidOperationException("DuplicateCardInBatch");

        // Validate every requested edit against the same pre-batch state before mutating anything.
        var changed = edits.Where(x => Preview(x.Id, x.Edit).Count > 0).ToArray();
        if (changed.Length == 0) return 0;

        var before = Capture();
        int historyDepth = history.Count;
        int applied = 0;
        try
        {
            foreach (var (id, edit) in changed)
                if (Apply(id, edit)) applied++;
        }
        catch
        {
            while (history.Count > historyDepth) history.Pop();
            Restore(before);
            throw;
        }

        // Collapse per-card history entries into one user-visible undo step.
        while (history.Count > historyDepth) history.Pop();
        if (applied > 0) { history.Push(new(before, $"Bulk edit ({applied} cards)")); redo.Clear(); }
        return applied;
    }

    public int ReplaceBatch(IReadOnlyList<(int Id, CardEdit Edit)> edits, string historyLabel)
    {
        if (edits.Select(x => x.Id).Distinct().Count() != edits.Count)
            throw new InvalidOperationException("DuplicateCardInBatch");

        Snapshot before = Capture();
        int historyDepth = history.Count;
        HistoryItem[] redoBefore = redo.ToArray();
        Restore(new(new XDocument(original), new(), new()));
        int applied = 0;
        try
        {
            foreach (var (id, edit) in edits)
                if (Apply(id, edit)) applied++;
        }
        catch
        {
            while (history.Count > historyDepth) history.Pop();
            Restore(before);
            redo.Clear();
            for (int i = redoBefore.Length - 1; i >= 0; i--) redo.Push(redoBefore[i]);
            throw;
        }

        while (history.Count > historyDepth) history.Pop();
        Snapshot after = Capture();
        if (!SnapshotEquals(before, after))
        {
            history.Push(new(before, historyLabel));
            redo.Clear();
        }
        else
        {
            redo.Clear();
            for (int i = redoBefore.Length - 1; i >= 0; i--) redo.Push(redoBefore[i]);
        }
        return applied;
    }

    private static bool SnapshotEquals(Snapshot left, Snapshot right)
    {
        if (!XNode.DeepEquals(left.Xml, right.Xml)
            || left.Text.Count != right.Text.Count
            || left.Abilities.Count != right.Abilities.Count
            || left.Text.Any(language => !right.Text.TryGetValue(language.Key, out var fields)
                || language.Value.Count != fields.Count
                || language.Value.Any(field => fields.GetValueOrDefault(field.Key) != field.Value))
            || left.Abilities.Any(ability => !right.Abilities.TryGetValue(ability.Key, out var graph)
                || !XNode.DeepEquals(ability.Value, graph))) return false;
        return true;
    }

    public void Undo(int steps = 1)
    {
        for (int i = 0; i < Math.Clamp(steps, 0, 100) && history.TryPop(out var last); i++)
        { redo.Push(new(Capture(), last.Label)); Restore(last.State); }
    }
    public void Redo(int steps = 1)
    {
        for (int i = 0; i < Math.Clamp(steps, 0, 100) && redo.TryPop(out var next); i++)
        { history.Push(new(Capture(), next.Label)); Restore(next.State); }
    }
    public void Reset()
    {
        if (!HasChanges) return;
        Record("Reset all changes");
        working = new XDocument(original);
        Reindex();
        textEdits.Clear();
        abilityOverrides.Clear();
    }

    public IReadOnlyList<Change> Changes()
    {
        var result = new List<Change>();
        foreach (CardEntry card in Cards)
        {
            XElement old = Template(card.Id, original), now = Template(card.Id);
            foreach (string field in new[] { "Power", "Armor", "Provision", "Tier", "Rarity", "FactionId", "SecondaryFactionId", "Categories", "PrimaryCategory", "SummoningSickness", "InitalTapCharges", "InitialTimer", "Tokens", "Madness" })
                if (!XNode.DeepEquals(old.Element(field), now.Element(field)))
                    result.Add(new($"{card.Id}/{field}", old.Element(field)!.ToString(), now.Element(field)!.ToString()));
        }
        foreach (var (language, edits) in textEdits)
            foreach (var (key, value) in edits) result.Add(new($"{language}/{key}", locales[language].Values[key], value));
        foreach (var (id, graph) in abilityOverrides) result.AddRange(NumberChanges(id, OriginalAbility(id)!, graph));
        foreach(var (id,graph) in abilityOverrides)
            if(GigaIsolationChanged(id,OriginalAbility(id),graph))result.Add(new(id+"/GigaChoiceIsolation",(GigaGraphMode(OriginalAbility(id))==1).ToString(),(GigaGraphMode(graph)==1).ToString()));
        foreach (var (id, graph) in abilityOverrides) result.AddRange(TriggerValueChanges(OriginalAbility(id),graph).Select(c=>c with {Field=id+"/"+c.Field}));
        foreach (var (id, graph) in abilityOverrides)
            if (OwnedSymbiosis(graph) != OwnedSymbiosis(OriginalAbility(id))) result.Add(new($"{id}/AdditionalSymbiosis",OwnedSymbiosis(OriginalAbility(id)).ToString(),OwnedSymbiosis(graph).ToString()));
        foreach(var (id,graph) in abilityOverrides)
            if(OwnedAssimilate(graph)!=OwnedAssimilate(OriginalAbility(id)))result.Add(new($"{id}/AdditionalAssimilate",OwnedAssimilate(OriginalAbility(id)).ToString(),OwnedAssimilate(graph).ToString()));
        foreach(var (id,graph) in abilityOverrides)
            if(OwnedHarmony(graph)!=OwnedHarmony(OriginalAbility(id)))result.Add(new($"{id}/AdditionalHarmony",OwnedHarmony(OriginalAbility(id)).ToString(),OwnedHarmony(graph).ToString()));
        foreach(var (id,graph) in abilityOverrides)
            if(OwnedIntimidate(graph)!=OwnedIntimidate(OriginalAbility(id)))result.Add(new($"{id}/AdditionalIntimidate",OwnedIntimidate(OriginalAbility(id)).ToString(),OwnedIntimidate(graph).ToString()));
        foreach(var (id,graph) in abilityOverrides)
            if(OwnedThrive(graph)!=OwnedThrive(OriginalAbility(id)))result.Add(new($"{id}/AdditionalThrive",OwnedThrive(OriginalAbility(id)).ToString(),OwnedThrive(graph).ToString()));
        foreach(var (id,graph) in abilityOverrides)
            if(OwnedVeteran(graph)!=OwnedVeteran(OriginalAbility(id)))result.Add(new($"{id}/AdditionalVeteran",OwnedVeteran(OriginalAbility(id)).ToString(),OwnedVeteran(graph).ToString()));
        foreach(var (id,graph) in abilityOverrides)
            if(OwnedFormation(graph)!=OwnedFormation(OriginalAbility(id)))result.Add(new($"{id}/AdditionalFormation",OwnedFormation(OriginalAbility(id)).ToString(),OwnedFormation(graph).ToString()));
        foreach(var (id,graph) in abilityOverrides)
            if(OwnedTrigger(graph,ProfitSpec)!=OwnedTrigger(OriginalAbility(id),ProfitSpec))result.Add(new($"{id}/AdditionalProfit",OwnedTrigger(OriginalAbility(id),ProfitSpec).ToString(),OwnedTrigger(graph,ProfitSpec).ToString()));
        foreach(var (id,graph) in abilityOverrides)
            if(OwnedTrigger(graph,ResupplySpec)!=OwnedTrigger(OriginalAbility(id),ResupplySpec))result.Add(new($"{id}/AdditionalResupply",OwnedTrigger(OriginalAbility(id),ResupplySpec).ToString(),OwnedTrigger(graph,ResupplySpec).ToString()));
        foreach(var (id,graph) in abilityOverrides)
            if(OwnedTrigger(graph,EchoSpec)!=OwnedTrigger(OriginalAbility(id),EchoSpec))result.Add(new($"{id}/AdditionalEcho",OwnedTrigger(OriginalAbility(id),EchoSpec).ToString(),OwnedTrigger(graph,EchoSpec).ToString()));
        foreach(var (id,graph) in abilityOverrides)
            if(OwnedFlanking(graph)!=OwnedFlanking(OriginalAbility(id)))result.Add(new($"{id}/AdditionalFlanking",OwnedFlanking(OriginalAbility(id)).ToString(),OwnedFlanking(graph).ToString()));
        return result;
    }

    private (string Baseline, string LiveHash, string? AppliedPackage)? ClientExportSource()
    {
        if (Client == null) return null;
        string liveHash = Client.CurrentArchiveHash();
        if (liveHash == Client.ArchiveHash) return (Client.ArchivePath, liveHash, null);
        // Trust only a byte-exact output produced by this project instance. The
        // retained original backup remains the base for all cumulative exports.
        foreach (var export in successfulClientExports.AsEnumerable().Reverse().Where(e => e.OutputHash == liveHash))
        {
            string baseline = Path.Combine(export.Package, "_editor", "backup", "data_definitions");
            if (File.Exists(baseline) && Hash(baseline) == Client.ArchiveHash) return (baseline, liveHash, export.Package);
        }
        throw new InvalidOperationException("SourceChanged");
    }

    public void Export(string target)
    {
        if (!HasChanges) throw new InvalidOperationException("NoChanges");
        var exportSource = ClientExportSource();
        string destination = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new InvalidOperationException("ExportExists");
        if (destination.Equals(DirectoryPath, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ExportInsideSource");
        string[] currentFiles = SourceFiles().Select(Path.GetFileName).Order().ToArray()!;
        if (!currentFiles.SequenceEqual(sourceHashes.Keys.Order())) throw new InvalidOperationException("SourceChanged");
        foreach (var (name, hash) in sourceHashes)
            if (Hash(Path.Combine(DirectoryPath, name)) != hash) throw new InvalidOperationException("SourceChanged");
        string parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".card-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string work = Client == null ? staging : Path.Combine(staging, ".working");
            string support = Client == null ? staging : Path.Combine(staging, "_editor");
            string backup = Path.Combine(support, "backup");
            Directory.CreateDirectory(work);
            Directory.CreateDirectory(backup);
            if (Client != null)
            {
                string archiveBackup = Path.Combine(backup, "data_definitions");
                Client.VerifyExportSource(exportSource!.Value.Baseline, exportSource.Value.LiveHash);
                File.Copy(exportSource.Value.Baseline, archiveBackup);
                if (Hash(archiveBackup) != Client.ArchiveHash) throw new InvalidOperationException("SourceChanged");
            }
            foreach (var (name, hash) in sourceHashes)
            {
                string source = Path.Combine(DirectoryPath, name);
                string copy = Path.Combine(work, name);
                File.Copy(source, copy);
                if (Hash(copy) != hash) throw new InvalidOperationException("SourceChanged");
                if (Client == null) File.Copy(copy, Path.Combine(backup, name));
            }
            if (!XNode.DeepEquals(original, working)) working.Save(Path.Combine(work, "Templates.xml"), SaveOptions.DisableFormatting);
            ExportAbilityNumbers(work);
            _ = XDocument.Load(Path.Combine(work, "Templates.xml"));
            foreach (var (language, edits) in textEdits.Where(x => x.Value.Count > 0))
            {
                string file = Path.Combine(work, language + ".csv");
                locales[language].WriteEdits(file, edits);
                var check = new LocaleFile(file);
                foreach (var (key, value) in edits)
                    if (check.Values[key] != value) throw new InvalidDataException("Localization round-trip failed.");
            }
            if (Client != null)
            {
                var modified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!XNode.DeepEquals(original, working)) modified.Add("Templates.xml");
                if (abilityOverrides.Count > 0) modified.Add("Abilities.xml");
                foreach (var (language, edits) in textEdits.Where(x => x.Value.Count > 0)) modified.Add(language + ".csv");
                Client.ExportArchive(work, modified, staging, exportSource!.Value.Baseline, exportSource.Value.LiveHash);
                // Discard only this export's private unpacked workspace, never a user folder.
                if (Path.GetDirectoryName(Path.GetFullPath(work)) != Path.GetFullPath(staging)
                    || Path.GetFileName(work) != ".working") throw new InvalidOperationException("InvalidExportWorkspace");
                Directory.Delete(work, true);
            }
            if (ExportBalanceCouncil && ExportBalanceSnapshot != null)
                ExportBalanceSnapshot.Save(Path.Combine(support, "balance-snapshot.json"));
            var outputHashes = Directory.EnumerateFiles(staging).ToDictionary(p => Path.GetFileName(p)!, Hash);
            File.WriteAllText(Path.Combine(support, "card-editor-changes.json"), JsonSerializer.Serialize(new
            {
                Version = "0.3.114", Source = SourcePath, CreatedUtc = DateTime.UtcNow,
                Balance = new { Mode = ExportBalanceCouncil ? ExportBalanceSnapshot == null ? "CouncilUnavailable" : "Council" : "Local",
                    SnapshotHash = ExportBalanceCouncil ? ExportBalanceSnapshot?.ContentHash : null,
                    CapturedUtc = ExportBalanceCouncil ? ExportBalanceSnapshot?.CapturedUtc : null,
                    ReferenceTemplateHash = ExportBalanceCouncil && ExportBalanceSnapshot != null ? BalanceReference.TemplateHash : null },
                SourceHashes = sourceHashes, Changes = Changes(), DescriptionWarnings = PendingDescriptionWarnings(),
                SourceArchiveHash = Client?.ArchiveHash,
                LiveArchiveHashAtExport = exportSource?.LiveHash,
                AppliedPackageRecognized = exportSource?.AppliedPackage != null,
                AppliedPackage = exportSource?.AppliedPackage,
                BackupArchive = Client == null ? null : "_editor/backup/data_definitions",
                OutputHashes = outputHashes
            }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            if (Client != null) Client.VerifyExportSource(exportSource!.Value.Baseline, exportSource.Value.LiveHash);
            Directory.Move(staging, destination);
            if (Client != null) successfulClientExports.Add((destination, outputHashes["data_definitions"]));
        }
        finally
        {
            if (Directory.Exists(staging) && Path.GetDirectoryName(Path.GetFullPath(staging)) == Path.GetFullPath(parent)
                && Path.GetFileName(staging).StartsWith(".card-editor-", StringComparison.Ordinal)) Directory.Delete(staging, true);
        }
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class LocaleFile
    {
        private readonly string content;
        private readonly bool bom;
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public LocaleFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bom = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 });
            content = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            foreach (string line in content.Split('\n'))
            {
                int separator = line.IndexOf(';');
                if (separator < 1) continue;
                string key = line[..separator], value = line[(separator + 1)..].TrimEnd('\r');
                if (!Values.TryAdd(key, value)) throw new InvalidDataException("Duplicate localization key: " + key);
            }
        }
        public void WriteEdits(string path, IReadOnlyDictionary<string, string> edits)
        {
            string output = Regex.Replace(content, @"^([^;\r\n]+);([^\r\n]*)", match =>
                edits.TryGetValue(match.Groups[1].Value, out string? value)
                    ? match.Groups[1].Value + ";" + value : match.Value, RegexOptions.Multiline);
            File.WriteAllText(path, output, new UTF8Encoding(bom));
        }
    }
}
