namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    private readonly CheckBox balanceCouncil = new EditorCheckBox { AutoSize = true, Checked = true };
    private readonly Button updateBalance = new() { AutoSize = true };
    private readonly Button saveBalance = new() { AutoSize = true, Enabled = false };
    private readonly Label balanceState = new() { AutoSize = true, MaximumSize = new Size(360, 0), Margin = new Padding(8, 7, 3, 3) };
    private BalanceSnapshot? balanceSnapshot;
    private BalanceReference? balanceReference;
    private BalanceProjection? balanceProjection;
    private bool balanceDownloading;
    private string? balanceError;
    private readonly CancellationTokenSource balanceCancellation = new();
    private string BalanceSourceKey => project!.SourcePath.ToUpperInvariant();
    private Control BuildBalanceBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(8, 0, 8, 3) };
        var import = Bind(new Button { AutoSize = true }, "OpenBalanceSnapshot");
        var reference = Bind(new Button { AutoSize = true }, "SelectBalanceReference");
        bar.Controls.AddRange([Bind(balanceCouncil, "BalanceCouncil"), Bind(updateBalance, "UpdateBalance"), import,
            Bind(saveBalance, "SaveBalanceSnapshot"), reference, balanceState]);
        saveBalance.Click += (_, _) => Guard(() =>
        {
            if (balanceSnapshot == null) return;
            using var dialog = new SaveFileDialog { Filter = "Balance snapshot (*.json)|*.json", DefaultExt = "json",
                FileName = $"gwent-balance-{balanceSnapshot.CapturedUtc:yyyy-MM-dd}.json", OverwritePrompt = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) balanceSnapshot.Save(dialog.FileName);
        });
        updateBalance.Click += async (_, _) => await DownloadBalance();
        import.Click += (_, _) => Guard(() =>
        {
            if (!CanChangeBalance()) return;
            using var dialog = new OpenFileDialog { Filter = "Balance snapshot (*.json)|*.json", InitialDirectory = BalanceFolder };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var snapshot = BalanceSnapshot.Load(dialog.FileName);
            AcceptSnapshot(snapshot);
        });
        reference.Click += (_, _) => Guard(() =>
        {
            if (project == null || !CanChangeBalance()) return;
            using var dialog = new FolderBrowserDialog { Description = UiText.Get("SelectBalanceReference"), UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var original = ClientSource.Open(dialog.SelectedPath, Path.Combine(EditorSettings.Home, "Projects"));
            balanceReference = BalanceReference.Load(original.DirectoryPath);
            settings.BalanceReferenceDirectory = original.DirectoryPath;
            RebuildBalanceProjection(); SaveSettings(); RefreshList(selectedId);
        });
        balanceCouncil.CheckedChanged += (_, _) => Guard(() =>
        {
            if (project == null) return;
            bool requested = balanceCouncil.Checked;
            // Resolve an existing draft in its old coordinate system.
            rendering = true; balanceCouncil.Checked = !requested; rendering = false;
            if (!CanChangeBalance()) return;
            rendering = true; balanceCouncil.Checked = requested; rendering = false;
            settings.BalanceCouncilBySource[BalanceSourceKey] = requested;
            SaveSettings(); RefreshList(selectedId);
            if (requested && balanceSnapshot == null && !testing) BeginInvoke(async () => await DownloadBalance());
        });
        FormClosed += (_, _) => balanceCancellation.Cancel();
        return bar;
    }
    private static string BalanceFolder => Path.Combine(EditorSettings.Home, "Balance", "Snapshots");
    private bool CanChangeBalance() => project != null && ResolveDraft() && (testing
        || !project.Changes().Any(c => c.Field.EndsWith("/Power") || c.Field.EndsWith("/Provision")) || Ask(UiText.Get("BalanceSwitchConfirm")));
    private void LoadBalanceSettings()
    {
        balanceSnapshot = null; balanceProjection = null; balanceReference = null; balanceError = null;
        rendering = true;
        balanceCouncil.Checked = !testing && settings.CouncilFor(project!.SourcePath);
        rendering = false;
        var candidates = new[] { project!.DirectoryPath, settings.BalanceReferenceDirectory }
            .Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>()
            .Concat(Directory.Exists(Path.Combine(EditorSettings.Home, "Projects"))
                ? Directory.EnumerateDirectories(Path.Combine(EditorSettings.Home, "Projects")) : []);
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { balanceReference = BalanceReference.Load(candidate); settings.BalanceReferenceDirectory = candidate; break; }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException) { }
        }
        if (!testing && settings.BalanceSnapshotFile != null)
        {
            try { balanceSnapshot = BalanceSnapshot.Load(settings.BalanceSnapshotFile); }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException)
            { balanceError = "InvalidBalanceSnapshot"; }
        }
        RebuildBalanceProjection();
        if (balanceCouncil.Checked && balanceSnapshot == null && balanceError == null && !testing)
            BeginInvoke(async () => await DownloadBalance());
    }
    private void RebuildBalanceProjection()
    {
        balanceProjection = null;
        if (balanceReference != null && balanceSnapshot != null)
        {
            try { balanceProjection = new(balanceReference, balanceSnapshot); }
            catch (InvalidOperationException) { balanceError = "BalanceCoverageMismatch"; }
        }
    }
    private void AcceptSnapshot(BalanceSnapshot snapshot)
    {
        // Verify compatibility before replacing the last usable snapshot.
        if (balanceReference != null) _ = new BalanceProjection(balanceReference, snapshot);
        string path = EditorSettings.CacheSnapshot(snapshot);
        balanceSnapshot = snapshot; balanceError = null;
        settings.BalanceSnapshotFile = path;
        RebuildBalanceProjection(); SaveSettings(); RefreshList(selectedId);
    }
    private async Task DownloadBalance()
    {
        if (project == null || balanceDownloading || !CanChangeBalance()) return;
        balanceDownloading = true; updateBalance.Enabled = false;
        RefreshBalanceNote(selectedId.HasValue ? project.Card(selectedId.Value) : null);
        try
        {
            var snapshot = await BalanceSnapshot.DownloadAsync(balanceCancellation.Token);
            if (!IsDisposed && !Disposing && CanChangeBalance()) AcceptSnapshot(snapshot);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException
            or System.Text.Json.JsonException or KeyNotFoundException or FormatException)
        {
            if (!IsDisposed && !Disposing) balanceError = "BalanceDownloadFailed";
        }
        finally
        {
            balanceDownloading = false;
            if (!IsDisposed && !Disposing) { updateBalance.Enabled = true; RefreshList(selectedId); }
        }
    }
    private static bool UsesLocalBalance(CardEntry? card) => card != null && (card.Type is 1 or 16 || card.IsToken || card.Availability <= 0);
    private CardEntry BalanceCardView(CardEntry card) => !balanceCouncil.Checked || UsesLocalBalance(card) ? card
        : balanceProjection?.Display(card) ?? card with { Power = -1, Provision = -1 };
    private void ConfigureBalanceInput(CardEntry local, string key, NumericUpDown input)
    {
        if (!balanceCouncil.Checked || UsesLocalBalance(local)) return;
        if (balanceProjection?.TryDelta(local, out var delta) != true)
        { input.Value = 0; input.Enabled = false; return; }
        int offset = key == "Power" ? delta.Power : delta.Provision;
        int raw = key == "Power" ? local.Power : local.Provision;
        int shown = raw + offset;
        if (shown < 0) { input.Value = 0; input.Enabled = false; return; }
        input.Minimum = 0;
        input.Maximum = Math.Max(shown, (key == "Power" ? 999 : 99) + offset);
        input.Value = shown;
        input.Minimum = Math.Min(shown, Math.Max(0, (key == "Power" && local.Type == 4 ? 1 : 0) + offset));
    }
    private int StoredBalanceInput(string key)
    {
        var local = project!.Card(selectedId!.Value);
        var input = (NumericUpDown)fields[key];
        int raw = key == "Power" ? local.Power : local.Provision;
        if (!input.Enabled) return raw;
        return balanceCouncil.Checked && !UsesLocalBalance(local) ? balanceProjection!.ToStored(local, key, (int)input.Value) : (int)input.Value;
    }
    private void RefreshBalanceNote(CardEntry? local)
    {
        saveBalance.Enabled = balanceSnapshot != null;
        string state = !balanceCouncil.Checked ? "BalanceLocal" : balanceDownloading ? "BalanceDownloading"
            : balanceSnapshot == null ? balanceError ?? "BalanceSnapshotMissing"
            : balanceReference == null ? "BalanceReferenceMismatch" : balanceProjection == null ? "BalanceCoverageMismatch" : "BalanceReady";
        balanceState.Text = UiText.Get(local?.IsToken == true ? "TokenLocalBalance" : local?.Type == 1 ? "LeaderLocalBalance" : UsesLocalBalance(local) ? "BalanceLocal" : state);
        if (!UsesLocalBalance(local) && balanceCouncil.Checked && balanceSnapshot != null)
            balanceState.Text += $" · {balanceSnapshot.CapturedUtc:yyyy-MM-dd} · {balanceSnapshot.ContentHash[..8]}";
        if (!UsesLocalBalance(local) && balanceError == "BalanceDownloadFailed" && balanceSnapshot != null)
            balanceState.Text += " · " + UiText.Get("BalanceCachedAfterFailure");
        if (!UsesLocalBalance(local) && balanceSnapshot != null && DateTimeOffset.UtcNow - balanceSnapshot.CapturedUtc > TimeSpan.FromDays(35) && balanceCouncil.Checked)
            balanceState.Text += " · " + UiText.Get("BalanceOldSnapshot");
        balanceNote.Text = local?.IsToken == true ? UiText.Get("TokenLocalNote") : local?.Type == 1 ? UiText.Get("LeaderLocalNote")
            : UsesLocalBalance(local) || !balanceCouncil.Checked ? UiText.Get("LocalBalanceNote") : UiText.Get("CouncilExportNote");
        if (balanceCouncil.Checked && local != null && !UsesLocalBalance(local))
        {
            if (balanceProjection?.TryDelta(local, out var delta) == true)
            {
                balanceNote.Text += $"\n{UiText.Get("LocalValues")}: {local.Power} / {local.Provision}. {UiText.Get("BalanceAdjustment")}: {delta.Power:+0;-0;0} / {delta.Provision:+0;-0;0}.";
                if (local.Power + delta.Power < 0 || local.Provision + delta.Provision < 0) balanceNote.Text += "\n" + UiText.Get("BalanceRange");
            }
            else balanceNote.Text += "\n" + UiText.Get(state == "BalanceReady" ? "BalanceCardMissing" : state);
        }
    }
    private void SmokeBalance(string output, string? balanceFile)
    {
        if (project == null || balanceReference == null) throw new InvalidOperationException("Balance smoke needs the verified original base.");
        if (!new EditorSettings().CouncilFor(project.SourcePath)) throw new InvalidOperationException("Council must default to enabled for new sources.");
        var previousSnapshot = balanceSnapshot;
        var raw = project.Card(112101);
        var cards = balanceReference.Cards.Values.Where(c => c.Type is 2 or 4 or 8 && c.Power >= 0 && c.Provision >= 0)
            .OrderBy(c => c.Id).Take(1900).Select(c => c.Id == raw.Id ? c with { Power = c.Power + 2, Provision = c.Provision - 1 } : c);
        balanceSnapshot = BalanceSnapshot.Create(cards, DateTimeOffset.UtcNow);
        balanceError = null; RebuildBalanceProjection();
        search.Text = ""; searchDelay.Stop(); factions.SelectedIndex = 0;
        search.Text = raw.Id.ToString(); searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 0;
        balanceCouncil.Checked = true;
        if (((NumericUpDown)fields["Power"]).Value != raw.Power + 2 || ((NumericUpDown)fields["Provision"]).Value != raw.Provision - 1
            || Convert.ToInt32(grid.Rows[0].Cells["Power"].Value) != raw.Power + 2 || project.Preview(raw.Id, Draft()).Count != 0)
            throw new InvalidOperationException("Council display or lossless draft failed.");
        if (ChangeValue("Power", raw.Power.ToString()) != (raw.Power + 2).ToString())
            throw new InvalidOperationException("Council confirmation must show final power.");
        ((NumericUpDown)fields["Power"]).Value = raw.Power + 3;
        CommitDraft(false);
        if (project.Card(raw.Id).Power != raw.Power + 1 || ((NumericUpDown)fields["Power"]).Value != raw.Power + 3)
            throw new InvalidOperationException("Council inverse conversion failed.");
        project.Undo(); RefreshList(raw.Id);
        if (project.HasChanges || draftDirty) throw new InvalidOperationException("Council undo failed.");
        foreach (int index in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = index; Application.DoEvents();
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, "council-" + UiText.Language + ".png"));
        }
        language.SelectedIndex = 0;
        balanceCouncil.Checked = false;
        if (((NumericUpDown)fields["Power"]).Value != raw.Power || settings.CouncilFor(project.SourcePath))
            throw new InvalidOperationException("Local mode must restore local values and remember the source choice.");
        if (!settings.CouncilFor(project.SourcePath + "-another-client"))
            throw new InvalidOperationException("Council setting must be isolated per source.");
        balanceSnapshot = null; RebuildBalanceProjection(); balanceCouncil.Checked = true;
        if (fields["Power"].Enabled || fields["Provision"].Enabled || !fields["Armor"].Enabled)
            throw new InvalidOperationException("Missing snapshot must block only balance stats.");
        ((NumericUpDown)fields["Armor"]).Value = raw.Armor + 1;
        CommitDraft(false);
        if (project.Card(raw.Id).Power != raw.Power || project.Card(raw.Id).Provision != raw.Provision)
            throw new InvalidOperationException("Unavailable balance data must not change hidden local values.");
        project.Undo(); RefreshList(raw.Id); balanceCouncil.Checked = false;
        if (balanceFile != null)
        {
            balanceSnapshot = BalanceSnapshot.Load(balanceFile); RebuildBalanceProjection(); balanceCouncil.Checked = true;
            var official = balanceSnapshot.Cards.Single(c => c.Id == raw.Id);
            if (((NumericUpDown)fields["Power"]).Value != official.Power || ((NumericUpDown)fields["Provision"]).Value != official.Provision)
                throw new InvalidOperationException("Downloaded official snapshot display failed.");
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, "council-official-ru.png"));
            balanceCouncil.Checked = false;
        }
        var originalReference = balanceReference;
        balanceReference = null; RebuildBalanceProjection(); balanceCouncil.Checked = true;
        if (fields["Power"].Enabled || fields["Provision"].Enabled) throw new InvalidOperationException("Unknown balance reference must lock stat editing.");
        balanceCouncil.Checked = false; balanceReference = originalReference;
        balanceSnapshot = previousSnapshot; RebuildBalanceProjection(); RefreshList(raw.Id);
        string saved = Path.Combine(output, "balance-settings.json");
        settings.Save(saved);
        var roundTrip = EditorSettings.Load(saved);
        using var appearance = new AppearanceDialog(roundTrip);
        if (roundTrip.CouncilFor(project.SourcePath) || appearance.Result.CouncilFor(project.SourcePath))
            throw new InvalidOperationException("Council preference lost during save or appearance editing.");
    }
}
