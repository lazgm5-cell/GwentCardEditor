namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeAbilityClarity(string output)
    {
        Directory.CreateDirectory(output);
        foreach (int locale in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = locale; hidden.Checked = false;
            foreach (int id in new[] { 202402, 202399, 200078, 112205, 202398, 202194, 202487, 202782, 202054, 132102, 203119, 112214, 202997, 202609, 203054, 122108, 202917, 202918, 202920, 202992, 203052, 202990, 122214, 202651, 203091, 203197, 203282, 203117, 122209, 202417, 202503, 202107, 200228, 202674, 202700, 202701, 202702, 202383, 142108 })
            {
                group.SelectedIndex = (int)CardGroup.All;
                search.Text = ""; searchDelay.Stop();
                factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, project!.Card(id).Faction);
                search.Text = id.ToString(); searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 2;
                Application.DoEvents();
                string labels = string.Join("\n", numberTable.Controls.OfType<Label>().Select(l => l.Text));
                AssertNoDescriptionDiagnostics(numberTable);
                if (id == 142108 && numberInputs.Count != 0) throw new Exception("Schirru technical damage visible");
                if (id == 202383)
                {
                    if (numberInputs.Count != 1 || !numberInputs.ContainsKey(-12)
                        || !labels.Contains(UiText.Get("Ability.gord-boost-cap"))) throw new Exception("Gord cap missing");
                    decimal before=numberInputs[-12].Value;
                    rendering=true;numberInputs[-12].Value=15;rendering=false;
                    var changes=project.Preview(id,Draft());
                    if(changes.Count!=1 || VisibleNumberChanges(changes).Any(c=>ChangeName(c.Field).Contains('#'))) throw new Exception("Gord cap confirmation technical");
                    rendering=true;numberInputs[-12].Value=before;rendering=false;draftDirty=false;
                }
                if (id == 202107 && (numberInputs.Count != 1 || !labels.Contains(UiText.Get("Ability.barnabas-boost")))) throw new Exception("Barnabas boost unclear");
                if (id == 200228 && (numberInputs.Count != 1 || !numberInputs.ContainsKey(-11))) throw new Exception("Pitfall quantum exposed");
                if (id == 202674 && numberInputs.Count != 0) throw new Exception("Parent choice parameters exposed");
                if (id is 202700 or 202701 or 202702)
                {
                    if (!numberInputs.ContainsKey(1) || !numberInputs.ContainsKey(-10)
                        || !labels.Contains(UiText.Get("AbilityHelp.NatureSharedTargets"))
                        || (id == 202702) != numberInputs.ContainsKey(2)) throw new Exception("Choice settings missing");
                    foreach (int field in numberInputs.Keys.ToArray())
                    {
                        decimal before = numberInputs[field].Value;
                        rendering = true; numberInputs[field].Value = before + 1; rendering = false;
                        var changes = project.Preview(id, Draft());
                        if (changes.Count != 2 || VisibleNumberChanges(changes).Count != 1
                            || VisibleNumberChanges(changes).Any(c => ChangeName(c.Field).Contains('#'))) throw new Exception("Choice confirmation duplicated or technical");
                        rendering = true; numberInputs[field].Value = before; rendering = false; draftDirty = false;
                    }
                }
                if (id is 202402 or 202399)
                {
                    int field = id == 202402 ? 259 : 363;
                    if (numberInputs.ContainsKey(field) || labels.Contains(UiText.Get("Ability.armor-condition")))
                        throw new Exception("Internal armor condition still visible");
                }
                if (id == 200078 && numberInputs.ContainsKey(133)) throw new Exception("Survival guard still visible");
                if (id == 112205)
                {
                    if (!labels.Contains(UiText.Get("Ability.draw-selected-count")) || !labels.Contains(UiText.Get("Ability.move-to-deck-bottom-count")))
                        throw new Exception("Myrgtabrakke steps not distinguished");
                    if (!ReferenceEquals(numberInputs[628], numberInputs[648]) || !ReferenceEquals(numberInputs[628], numberInputs[649])
                        || ReferenceEquals(numberInputs[628], numberInputs[999])) throw new Exception("Draw synchronization incorrect");
                    var input = numberInputs[628]; decimal before = input.Value;
                    rendering = true; input.Value = before + 1; rendering = false;
                    var edits = Draft().Numbers;
                    if (edits == null || !new[] { 628, 648, 649 }.All(key => edits.ContainsKey(key)) || edits.ContainsKey(999) || edits.ContainsKey(1000))
                        throw new Exception("Draw draft affects wrong step");
                    rendering = true; input.Value = before; rendering = false; draftDirty = false;
                }
                if (id == 132102 && numberInputs.ContainsKey(524)) throw new Exception("Imlerith strongest count visible");
                if (id == 203119 && numberInputs.ContainsKey(1704)) throw new Exception("Toad internal threshold visible");
                if (id == 112214 && (!numberInputs.TryGetValue(11, out var threshold) || threshold.Value != 4))
                    throw new Exception("Cyprian explicit threshold missing");
                if (id == 202398 && numberInputs.ContainsKey(136)) throw new Exception("Olgierd bookkeeping visible");
                if (id == 202194 && numberInputs.ContainsKey(305)) throw new Exception("Syanna activation signal visible");
                if (id == 202487 && (!labels.Contains(UiText.Get("Ability.reveal-owner-count"))
                    || !labels.Contains(UiText.Get("Ability.reveal-opponent-count"))
                    || !labels.Contains(UiText.Get("AbilityHelp.RevealAfterSwap")))) throw new Exception("Merchant swap not explained");
                if (id == 202782 && (!ReferenceEquals(numberInputs[86], numberInputs[18])
                    || !ReferenceEquals(numberInputs[86], numberInputs[19])
                    || !labels.Contains(UiText.Get("Ability.shuffle-into-deck-count")))) throw new Exception("Mutation choices unclear");
                if (id == 202054 && (numberInputs.ContainsKey(24) || numberInputs.ContainsKey(25)
                    || !labels.Contains(UiText.Get("AbilityHelp.AdditionalDestruction")))) throw new Exception("Soup anchor not protected");
                if (id == 202997)
                {
                    if (new[] { 172, 173, 261, 262 }.Any(numberInputs.ContainsKey)
                        || !new[] { 385, 429, 430 }.All(key => ReferenceEquals(numberInputs[384], numberInputs[key]))
                        || !labels.Contains(UiText.Get("Ability.strength-transfer-targets"))
                        || !labels.Contains(UiText.Get("AbilityHelp.AlternativeStrengthTransfer"))) throw new Exception("Doll alternative steps unclear");
                    var input = numberInputs[384]; decimal before = input.Value;
                    rendering = true; input.Value = before + 1; rendering = false;
                    var edits = Draft().Numbers;
                    if (edits == null || !new[] { 384, 385, 429, 430 }.All(edits.ContainsKey)
                        || new[] { 172, 173, 261, 262 }.Any(edits.ContainsKey)) throw new Exception("Doll draft not synchronized");
                    rendering = true; input.Value = before; rendering = false; draftDirty = false;
                }
                if (id == 202609 && (numberInputs.ContainsKey(527) || !numberInputs.ContainsKey(148) || !numberInputs.ContainsKey(562)))
                    throw new Exception("Wraith technical charge check visible or effects missing");
                if (id == 203054 && (numberInputs.ContainsKey(55) || numberInputs.ContainsKey(56))) throw new Exception("Sabbath row count visible");
                if (id is 122108 or 202917 or 202918 or 202920 or 202992 or 203052)
                {
                    var patience = project.InitialPatience(id) ?? throw new Exception("Patience contract missing");
                    if (!numberInputs.TryGetValue(-6, out var input) || input.Value != patience.Value
                        || !labels.Contains(UiText.Get("Ability.initial-patience"))
                        || !labels.Contains(UiText.Get("MeaningHelp.InitialPatience"))) throw new Exception("Initial Patience setting missing");
                    decimal before = input.Value;
                    rendering = true; input.Value = before + 1; rendering = false;
                    var edits = Draft().Numbers;
                    if (edits == null || !edits.ContainsKey(-6) || edits.ContainsKey(patience.VariableId))
                        throw new Exception("Patience draft edits runtime variable directly");
                    rendering = true; input.Value = before; rendering = false; draftDirty = false;
                }
                if (id == 202990 && numberInputs.ContainsKey(-6)) throw new Exception("Unverified Leticia Patience unlocked");
                if (id == 122214)
                {
                    if (!numberInputs.TryGetValue(-1, out var charges) || charges.Value != 3
                        || !labels.Contains(AbilityLabel("initial-charges"))) throw new Exception("Sergeant initial charges missing");
                    rendering = true; charges.Value = 4; rendering = false;
                    var edits = Draft().Numbers;
                    if (edits == null || !edits.ContainsKey(-1) || edits.ContainsKey(295)) throw new Exception("Sergeant charge draft inconsistent");
                    rendering = true; charges.Value = 3; rendering = false; draftDirty = false;
                }
                if (id == 202651 && numberInputs.ContainsKey(527)) throw new Exception("Kerack unused-order threshold visible");
                if (id == 203091 && (numberInputs.ContainsKey(1486) || !numberInputs.ContainsKey(1342))) throw new Exception("Radovid charge ability unclear");
                if (id == 203197 && new[] { 898, 920, 943 }.Any(numberInputs.ContainsKey)) throw new Exception("Melitele self reveal count visible");
                if (id == 203282 && numberInputs.ContainsKey(94)) throw new Exception("Radovid Retribution charge threshold visible");
                if (id == 203117 && (numberInputs.ContainsKey(216) || !numberInputs.ContainsKey(97))) throw new Exception("Priestess service boost visible or real boost missing");
                if (id == 122209 && (numberInputs.ContainsKey(466) || !numberInputs.ContainsKey(374))) throw new Exception("Trololo armor reset visible or real armor gain missing");
                if (id is 202417 or 202503)
                {
                    int[] controls = id == 202417 ? [-7, -8] : [-9];
                    decimal[] before = controls.Select(key => numberInputs[key].Value).ToArray();
                    rendering = true;
                    numberInputs[controls[0]].Value = 5;
                    if (id == 202417) numberInputs[-8].Value = 6;
                    rendering = false;
                    var changes = project.Preview(id, Draft());
                    var visible = VisibleNumberChanges(changes);
                    if (visible.Count != controls.Length || visible.Any(c => ChangeName(c.Field).Contains('#')))
                        throw new Exception("Linked computed effect confirmation exposes technical aliases");
                    rendering = true;
                    for (int i = 0; i < controls.Length; i++) numberInputs[controls[i]].Value = before[i];
                    rendering = false; draftDirty = false;
                }
                using var capture = new Bitmap(Width, Height);
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"clarity-{id}-{UiText.Language}.png"));
            }
        }
        if (project!.HasChanges || draftDirty) throw new Exception("Clarity smoke changed source card data");
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Cards\":39,\"Languages\":3,\"ClientModified\":false}");
    }
}
