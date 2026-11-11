using System.Globalization;

namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    private string lastReviewedSignature = "";
    private readonly HashSet<string> generatedLocalizationChanges = new(StringComparer.Ordinal);

    private void TrackGeneratedLocalizationChanges(int id, CardEdit draft, IReadOnlyList<Change> preview)
    {
        if (project == null) return;
        bool statusOrKeywordChanged = preview.Any(change => change.Field is "Tokens" or "SummoningSickness" or "Madness"
            or "AdditionalSymbiosis" or "AdditionalAssimilate" or "AdditionalHarmony" or "AdditionalIntimidate"
            or "AdditionalThrive" or "AdditionalVeteran" or "AdditionalFormation" or "AdditionalProfit"
            or "AdditionalResupply" or "AdditionalEcho" or "AdditionalFlanking"
            || change.Field.StartsWith("AdditionalValue/", StringComparison.Ordinal))
            || preview.Any(change =>
            {
                string[] parts = change.Field.Split('/');
                return parts.Length == 3 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int cardId)
                    && cardId == id && parts[1] == "Ability"
                    && int.TryParse(parts[2].TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out int variableId)
                    && project.IsNativeKeywordVariable(id, variableId);
            });
        foreach (Change change in preview)
        {
            string[] parts = change.Field.Split('/');
            if (parts.Length != 2 || parts[1] != "tooltip") continue;

            bool unchangedInput = draft.Texts.TryGetValue(parts[0], out var fields)
                && fields.TryGetValue("tooltip", out string? requested)
                && string.Equals(requested, project.Text(id, parts[0], "tooltip"), StringComparison.Ordinal);
            string projectChangeKey = $"{parts[0]}/{id}_tooltip";
            if (unchangedInput && statusOrKeywordChanged) generatedLocalizationChanges.Add(projectChangeKey);
            else generatedLocalizationChanges.Remove(projectChangeKey);
        }
    }

    private Dictionary<int, CardEdit> CloneStagedEdits() => new(stagedEdits);

    private void RestoreStagedEdits(IReadOnlyDictionary<int, CardEdit> source)
    {
        stagedEdits.Clear();
        foreach (var (id, edit) in source) stagedEdits[id] = edit;
    }

    private bool HasChangesForCard(int id) => project?.Changes().Any(change => ChangeCardId(change.Field) == id) == true;

    private static int? ChangeCardId(string field)
    {
        string[] parts = field.Split('/');
        if (parts.Length > 1 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int direct))
            return direct;
        if (parts.Length > 1)
        {
            int separator = parts[1].IndexOf('_');
            if (separator > 0 && int.TryParse(parts[1].AsSpan(0, separator), NumberStyles.None,
                CultureInfo.InvariantCulture, out int localized)) return localized;
        }
        return null;
    }

    private void UndoStagedChanges(int steps)
    {
        if (project == null) return;
        for (int i = 0; i < Math.Clamp(steps, 0, 100) && project.CanUndo; i++)
        {
            project.Undo();
            stagedRedo.Push(CloneStagedEdits());
            if (stagedUndo.TryPop(out var previous)) RestoreStagedEdits(previous);
            else stagedEdits.Clear();
        }
        lastReviewedSignature = "";
    }

    private void RedoStagedChanges(int steps)
    {
        if (project == null) return;
        for (int i = 0; i < Math.Clamp(steps, 0, 100) && project.CanRedo; i++)
        {
            project.Redo();
            stagedUndo.Push(CloneStagedEdits());
            if (stagedRedo.TryPop(out var next)) RestoreStagedEdits(next);
        }
        lastReviewedSignature = "";
    }

    private void RemoveCardChanges(int id)
    {
        if (project == null || !stagedEdits.ContainsKey(id)) return;
        var before = CloneStagedEdits();
        var remaining = stagedEdits.Where(pair => pair.Key != id).ToArray();
        project.ReplaceBatch(remaining.Select(pair => (pair.Key, pair.Value)).ToArray(),
            UiText.Get("PendingReviewRemoveHistory"));
        stagedUndo.Push(before);
        stagedRedo.Clear();
        RestoreStagedEdits(remaining.ToDictionary(pair => pair.Key, pair => pair.Value));
        lastReviewedSignature = "";
        RefreshList(selectedId);
    }

    private void ReviewPendingChanges()
    {
        if (project == null || !ResolveDraft() || project == null || !project.HasChanges) return;
        var dialog = new Form
        {
            Text = UiText.Get("PendingReviewTitle"),
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(1020, 690),
            MinimumSize = new Size(780, 500),
            MinimizeBox = false,
            MaximizeBox = true,
            ShowInTaskbar = false
        };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var instructions = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(950, 0),
            Text = UiText.Get("PendingReviewInstructions"),
            Margin = new Padding(2, 2, 2, 10)
        };
        var cards = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None
        };
        cards.Columns.Add(new DataGridViewTextBoxColumn { Name = "Card", HeaderText = UiText.Get("PendingReviewCard"), FillWeight = 35 });
        cards.Columns.Add(new DataGridViewTextBoxColumn { Name = "Summary", HeaderText = UiText.Get("PendingReviewChanges"), FillWeight = 65 });
        var details = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None
        };
        details.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        details.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = UiText.Get("Changes"), FillWeight = 34 });
        details.Columns.Add(new DataGridViewTextBoxColumn { Name = "Before", HeaderText = UiText.Get("Before"), FillWeight = 33 });
        details.Columns.Add(new DataGridViewTextBoxColumn { Name = "After", HeaderText = UiText.Get("After"), FillWeight = 33 });
        var summary = new Label { AutoSize = true, Margin = new Padding(2, 6, 8, 2) };
        var edit = new Button { AutoSize = true, Text = UiText.Get("PendingReviewEditCard"), Enabled = false };
        var remove = new Button { AutoSize = true, Text = UiText.Get("PendingReviewRemoveCard"), Enabled = false };
        var accept = new Button { AutoSize = true, Text = UiText.Get("PendingReviewApply") };
        var resetAll = new Button { AutoSize = true, Text = UiText.Get("PendingReviewResetAll") };
        var cancel = new Button { AutoSize = true, Text = UiText.Get("PendingReviewCancel"), DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange([cancel, accept, resetAll, remove, edit, summary]);
        root.Controls.Add(instructions, 0, 0);
        root.Controls.Add(cards, 0, 1);
        root.Controls.Add(details, 0, 2);
        root.Controls.Add(actions, 0, 3);
        dialog.Controls.Add(root);
        dialog.CancelButton = cancel;
        dialog.AcceptButton = accept;

        Dictionary<int, Change[]> grouped = new();
        void Rebuild()
        {
            int? selected = cards.CurrentRow?.Tag is int current ? current : null;
            var allChanges = project!.Changes().Where(change => !generatedLocalizationChanges.Contains(change.Field)).ToArray();
            grouped = allChanges.Where(change => ChangeCardId(change.Field).HasValue)
                .GroupBy(change => ChangeCardId(change.Field)!.Value)
                .ToDictionary(group => group.Key, group => VisibleNumberChanges(group.ToArray()).ToArray());
            cards.Rows.Clear();
            foreach (var (id, changes) in grouped.OrderBy(pair => project.Name(pair.Key, UiText.Language), CultureComparer()))
            {
                int row = cards.Rows.Add($"{project.Name(id, UiText.Language)} ({id})", UiText.Get("PendingReviewFieldCount")
                    .Replace("{count}", changes.Length.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
                cards.Rows[row].Tag = id;
            }
            int target = selected.HasValue ? cards.Rows.Cast<DataGridViewRow>().ToList().FindIndex(row => (int)row.Tag! == selected.Value) : -1;
            if (target < 0 && cards.Rows.Count > 0) target = 0;
            if (target >= 0) { cards.CurrentCell = cards.Rows[target].Cells[0]; cards.Rows[target].Selected = true; }
            summary.Text = UiText.Get("PendingReviewSummary")
                .Replace("{cards}", grouped.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{changes}", grouped.Values.Sum(changes => changes.Length).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            accept.Enabled = project.HasChanges;
            resetAll.Enabled = project.HasChanges;
            ShowDetails();
        }

        void ShowDetails()
        {
            details.Rows.Clear();
            if (cards.CurrentRow?.Tag is not int id || !grouped.TryGetValue(id, out var changes))
            {
                edit.Enabled = remove.Enabled = false;
                return;
            }
            edit.Enabled = true;
            remove.Enabled = stagedEdits.ContainsKey(id);
            remove.Text = remove.Enabled ? UiText.Get("PendingReviewRemoveCard") : UiText.Get("PendingReviewLinkedChanges");
            foreach (Change change in changes)
                details.Rows.Add(ChangeName(change.Field), ChangeValue(change.Field, change.Before), ChangeValue(change.Field, change.After));
        }

        cards.SelectionChanged += (_, _) => ShowDetails();
        edit.Click += (_, _) =>
        {
            if (cards.CurrentRow?.Tag is not int id) return;
            dialog.Tag = id;
            dialog.DialogResult = DialogResult.Retry;
            dialog.Close();
        };
        remove.Click += (_, _) =>
        {
            if (cards.CurrentRow?.Tag is not int id || project == null) return;
            string prompt = UiText.Get("PendingReviewRemoveConfirm").Replace("{card}", project.Name(id, UiText.Language), StringComparison.Ordinal);
            if (MessageBox.Show(dialog, prompt, dialog.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            RemoveCardChanges(id);
            Rebuild();
        };
        resetAll.Click += (_, _) =>
        {
            if (project == null || !project.HasChanges) return;
            if (MessageBox.Show(dialog, UiText.Get("ResetConfirm"), dialog.Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            stagedUndo.Push(CloneStagedEdits());
            project.Reset();
            stagedRedo.Clear();
            stagedEdits.Clear();
            lastReviewedSignature = "";
            RefreshList(selectedId);
            Rebuild();
        };
        accept.Click += (_, _) =>
        {
            lastReviewedSignature = Signature();
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };
        Rebuild();
        Theme.Apply(dialog, settings, settings.CreateFont());
        var result = dialog.ShowDialog(this);
        if (result == DialogResult.Retry && dialog.Tag is int editId)
            NavigateToCard(editId);
        else if (result == DialogResult.OK)
            UpdateActions();
        dialog.Dispose();
    }
}
