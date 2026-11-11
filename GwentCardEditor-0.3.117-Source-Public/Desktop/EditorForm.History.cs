namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    private void ShowHistory()
    {
        if (project == null || !ResolveDraft() || project == null) return;
        using var dialog = new Form { Text = UiText.Language == "ru-ru" ? "История изменений" : "Edit history", StartPosition = FormStartPosition.CenterParent, Size = new Size(760, 500), MinimumSize = new Size(600, 360) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var undoList = new ListBox { Dock = DockStyle.Fill };
        var redoList = new ListBox { Dock = DockStyle.Fill };
        foreach (string item in project.HistoryLabels.Reverse()) undoList.Items.Add(item);
        foreach (string item in project.RedoLabels) redoList.Items.Add(item);
        var undoPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        undoPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); undoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        undoPanel.Controls.Add(new Label { AutoSize = true, Text = UiText.Language == "ru-ru" ? "Отменить" : "Undo" }, 0, 0); undoPanel.Controls.Add(undoList, 0, 1);
        var redoPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        redoPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); redoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        redoPanel.Controls.Add(new Label { AutoSize = true, Text = UiText.Language == "ru-ru" ? "Повторить" : "Redo" }, 0, 0); redoPanel.Controls.Add(redoList, 0, 1);
        layout.Controls.Add(undoPanel, 0, 0); layout.Controls.Add(redoPanel, 1, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { AutoSize = true, Text = UiText.Language == "ru-ru" ? "Закрыть" : "Close", DialogResult = DialogResult.Cancel };
        var undoButton = new Button { AutoSize = true, Text = UiText.Language == "ru-ru" ? "Отменить выбранное и новее" : "Undo selected and newer" };
        var redoButton = new Button { AutoSize = true, Text = UiText.Language == "ru-ru" ? "Повторить выбранное и новее" : "Redo selected and newer" };
        undoButton.Enabled = undoList.Items.Count > 0; redoButton.Enabled = redoList.Items.Count > 0;
        undoButton.Click += (_, _) => { if (undoList.SelectedIndex >= 0) { UndoStagedChanges(undoList.SelectedIndex + 1); dialog.DialogResult = DialogResult.OK; dialog.Close(); } };
        redoButton.Click += (_, _) => { if (redoList.SelectedIndex >= 0) { RedoStagedChanges(redoList.SelectedIndex + 1); dialog.DialogResult = DialogResult.OK; dialog.Close(); } };
        actions.Controls.AddRange([close, redoButton, undoButton]); layout.Controls.Add(actions, 0, 1); layout.SetColumnSpan(actions, 2);
        dialog.Controls.Add(layout); dialog.CancelButton = close;
        Theme.Apply(dialog, settings, settings.CreateFont());
        if (dialog.ShowDialog(this) == DialogResult.OK) RefreshList(selectedId);
    }
}
