using System.Diagnostics;
using System.Globalization;
using System.Text;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

internal sealed record Choice(int Value, string Label)
{
    public override string ToString() => Label;
}
internal sealed record CategoryChoice(string Name, string Label)
{
    public override string ToString() => Label;
}

internal sealed partial class EditorForm : Form
{
    private static readonly string[] FactionKeys = ["Neutral", "Monsters", "NorthernRealms", "Scoiatael", "Skellige", "Nilfgaard", "Syndicate"];
    private readonly ComboBox language = new EditorComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155 };
    private readonly TextBox search = new() { Width = 270 };
    private readonly ComboBox sort = new EditorComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox group = new EditorComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly CheckBox hidden = new EditorCheckBox { AutoSize = true };
    private readonly CheckBox initialZeal = new EditorCheckBox { AutoSize = true };
    private readonly Label zealNote = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Dictionary<string, CheckBox> initialStatuses = new();
    private readonly Label balanceNote = new() { AutoSize = true, MaximumSize = new Size(400, 0), ForeColor = Color.DimGray };
    private readonly TabControl factions = new() { Dock = DockStyle.Fill };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, RowHeadersVisible = false, BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AutoGenerateColumns = false
    };
    private readonly SplitContainer split = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
    private readonly Label heading = new() { AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold), MaximumSize = new Size(550, 0) };
    private readonly Label reason = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(540, 0) };
    private readonly Label identity = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly TableLayoutPanel detail = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(14) };
    private readonly Dictionary<string, Control> fields = new();
    private readonly CheckedListBox categories = new() { CheckOnClick = true, Height = 160, Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TabControl textTabs = new() { Dock = DockStyle.Fill, Height = 370 };
    private readonly TabControl sections = new() { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel numberTable = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
    private readonly Dictionary<int, NumericUpDown> numberInputs = new();
    private IReadOnlyList<AbilityNumber> numberValues = Array.Empty<AbilityNumber>();
    private readonly Panel parameterScroll = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(247, 248, 250) };
    private readonly Dictionary<(string Language, string Field), TextBox> textFields = new();
    private readonly Button apply = new() { AutoSize = true };
    private readonly Button revert = new() { AutoSize = true };
    private readonly Button undo = new() { AutoSize = true };
    private readonly Button redo = new() { AutoSize = true };
    private readonly Button historyButton = new() { AutoSize = true };
    private readonly Button export = new() { AutoSize = true };
    private readonly Button reset = new() { AutoSize = true };
    private readonly Button officialGog = new() { AutoSize = true };
    private readonly Dictionary<int, CardEdit> stagedEdits = new();
    private readonly Stack<Dictionary<int, CardEdit>> stagedUndo = new();
    private readonly Stack<Dictionary<int, CardEdit>> stagedRedo = new();
    private readonly Label source = new() { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, Height = 25 };
    private readonly ToolTip sourceToolTip = new();
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly List<(Control Control, string Key)> translated = new();
    private readonly System.Windows.Forms.Timer searchDelay = new() { Interval = 180 };
    private EditorProject? project;
    private int? selectedId;
    private bool rendering, draftDirty, testClosing;
    private int currentFactionIndex;
    private string currentSearch = "";
    private bool currentHidden;
    private int currentSort;
    private int currentGroup;
    private bool descending;
    private int lastHeaderColumn = -1;
    private EditorSettings settings;
    private readonly bool testing;
    private Font? appearanceFont;
    private readonly List<Font> appearanceFonts = new();
    private string lastExportSignature = "";
    private string Signature() => project == null ? "" : System.Text.Json.JsonSerializer.Serialize(project.Changes());
    private bool Unexported => draftDirty || project?.HasChanges == true && Signature() != lastExportSignature;

    public EditorForm(string? directory, EditorSettings settings, bool testing = false)
    {
        this.settings = settings;
        this.testing = testing;
        UiText.Language = EditorProject.Languages.Contains(settings.Language) ? settings.Language : "en-us";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        Size = new Size(1320, 930);
        MinimumSize = new Size(1060, 740);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        BuildLayout();
        language.Items.AddRange(EditorProject.Languages.Select(LanguageCatalog.Name).Cast<object>().ToArray());
        language.SelectedIndex = Array.IndexOf(EditorProject.Languages, UiText.Language);
        language.SelectedIndexChanged += (_, _) => Guard(() =>
        {
            if (!ResolveDraft()) { rendering = true; language.SelectedIndex = Array.IndexOf(EditorProject.Languages, UiText.Language); rendering = false; return; }
            UiText.Language = EditorProject.Languages[language.SelectedIndex];
            this.settings.Language = UiText.Language;
            Translate();
            SaveSettings();
        });
        Translate();
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); FilterChanged(); };
        search.TextChanged += (_, _) => { if (!rendering) { searchDelay.Stop(); searchDelay.Start(); } };
        hidden.CheckedChanged += (_, _) => FilterChanged();
        sort.SelectedIndexChanged += (_, _) => FilterChanged();
        group.SelectedIndexChanged += (_, _) => FilterChanged();
        grid.ColumnHeaderMouseClick += (_, e) => Guard(() => SortColumn(e.ColumnIndex));
        factions.SelectedIndexChanged += (_, _) => FilterChanged();
        grid.SelectionChanged += (_, _) => Guard(SelectionChanged);
        categories.ItemCheck += (_, _) => MarkDraft();
        apply.Click += (_, _) => Guard(ReviewPendingChanges);
        revert.Click += (_, _) => { if (selectedId.HasValue) ShowCard(selectedId.Value); };
        undo.Click += (_, _) => Guard(() => { if (ResolveDraft()) { UndoStagedChanges(1); RefreshList(selectedId); } });
        redo.Click += (_, _) => Guard(() => { if (ResolveDraft()) { RedoStagedChanges(1); RefreshList(selectedId); } });
        historyButton.Click += (_, _) => Guard(ShowHistory);
        reset.Click += (_, _) => Guard(() =>
        {
            if (project != null && ResolveDraft() && Ask(UiText.Get("ResetConfirm")))
            {
                var previous = CloneStagedEdits();
                project.Reset();
                stagedUndo.Push(previous);
                stagedRedo.Clear();
                stagedEdits.Clear();
                RefreshList(selectedId);
            }
        });
        export.Click += (_, _) => Guard(ExportProject);
        officialGog.Click += (_, _) => Guard(OpenOfficialGog);
        relatedCards.Click += (_, _) => Guard(OpenRelatedCards);
        FormClosing += (_, e) =>
        {
            if (!testClosing && Unexported && !Ask(UiText.Get("Discard"))) e.Cancel = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S) { Guard(ExportProject); e.SuppressKeyPress = true; }
        };
        Shown += (_, _) => { if (!string.IsNullOrWhiteSpace(directory)) Guard(() => LoadProject(directory)); };
        ApplyAppearance();
    }

    private T Bind<T>(T control, string key) where T : Control { translated.Add((control, key)); return control; }
    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        root.RowCount = 6;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        var open = Bind(new Button { AutoSize = true }, "Open");
        open.Click += (_, _) => Guard(OpenProject);
        var appearance = Bind(new Button { AutoSize = true }, "Appearance");
        appearance.Click += (_, _) => Guard(() =>
        {
            using var dialog = new AppearanceDialog(this.settings);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            this.settings = dialog.Result;
            ApplyAppearance();
            SaveSettings();
        });
        undo.Text = UiText.Get("Undo"); redo.Text = UiText.Language == "ru-ru" ? "Повторить" : "Redo";
        historyButton.Text = UiText.Language == "ru-ru" ? "История" : "History";
        toolbar.Controls.AddRange([open, Bind(officialGog, "OpenOfficialGog"), Bind(export, "Export"), Bind(undo, "Undo"), redo, historyButton, Bind(reset, "Reset"), appearance, language]);
        root.Controls.Add(toolbar, 0, 0);
        source.Padding = new Padding(12, 2, 12, 0);
        root.Controls.Add(source, 0, 1);
        root.Controls.Add(BuildBalanceBar(), 0, 2);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 2, 8, 6), WrapContents = true };
        filters.Controls.Add(search);
        filters.Controls.Add(Bind(new Label { AutoSize = true, Margin = new Padding(8, 7, 4, 0) }, "CardGroup"));
        filters.Controls.Add(group);
        filters.Controls.Add(Bind(new Label { AutoSize = true, Margin = new Padding(10, 7, 4, 0) }, "Sort"));
        filters.Controls.Add(sort);
        filters.Controls.Add(Bind(hidden, "Hidden"));
        root.Controls.Add(filters, 0, 3);
        root.Controls.Add(split, 0, 4);
        status.Padding = new Padding(12, 6, 12, 0);
        root.Controls.Add(status, 0, 5);
        Controls.Add(root);
        split.Size = new Size(1280, 700);
        split.Panel1MinSize = 380;
        split.Panel2MinSize = 460;
        split.SplitterDistance = 690;
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Controls.Add(factions, 0, 0);
        factions.Multiline = true;
        factions.SizeChanged += (_, _) =>
        {
            int height = Math.Max(64, factions.RowCount * (factions.Font.Height + 12) + 10);
            if (Math.Abs(left.RowStyles[0].Height - height) > 1) left.RowStyles[0].Height = height;
        };
        left.Controls.Add(grid, 0, 1);
        split.Panel1.Controls.Add(left);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", FillWeight = 20 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", FillWeight = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rarity", FillWeight = 28 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Power", FillWeight = 15 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Provision", FillWeight = 18 });
        foreach (var (name, width) in new[] { ("Id", 72), ("Rarity", 95), ("Power", 44), ("Provision", 68) })
        {
            grid.Columns[name]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            grid.Columns[name]!.Width = width;
        }
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.Programmatic;
        parameterScroll.Controls.Add(detail);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var parametersPage = Bind(new TabPage(), "Parameters");
        parametersPage.Controls.Add(parameterScroll);
        var textsPage = Bind(new TabPage(), "Texts");
        var textsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        textsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        textsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        textsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        textsLayout.Controls.Add(textTabs, 0, 0);
        textsLayout.Controls.Add(Bind(new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray }, "TextSafety"), 0, 1);
        textsPage.Controls.Add(textsLayout);
        sections.TabPages.AddRange([parametersPage, textsPage]);
        var numbersPage = Bind(new TabPage(), "Ability");
        var numberScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        numberTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        numberTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        numberScroll.Controls.Add(numberTable);
        numberScroll.SizeChanged += (_, _) =>
        {
            int width = Math.Max(200, numberScroll.ClientSize.Width - 55);
            foreach (Control control in numberTable.Controls)
                if (control is Label) control.MaximumSize = new Size(width, 0);
        };
        numbersPage.Controls.Add(numberScroll);
        sections.TabPages.Add(numbersPage);
        sections.TabPages.Add(BuildArtPage());
        sections.TabPages.Add(BuildVisualPage());
        sections.TabPages.Add(BuildAudioPage());
        right.Controls.Add(sections, 0, 0);
        split.Panel2.Controls.Add(right);
        split.SizeChanged += (_, _) =>
        {
            int maximum = split.ClientSize.Width - split.Panel2MinSize - split.SplitterWidth;
            if (maximum >= split.Panel1MinSize && split.SplitterDistance > maximum) split.SplitterDistance = maximum;
        };
        parameterScroll.SizeChanged += (_, _) =>
        {
            int width = Math.Max(220, parameterScroll.ClientSize.Width - 45);
            foreach (Control control in detail.Controls)
                if (control is Label && detail.GetColumnSpan(control) == 2) control.MaximumSize = new Size(width, 0);
        };
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddWide(heading);
        AddWide(identity);
        AddWide(reason);
        AddWide(Bind(balanceNote, "LocalBalanceNote"));
        foreach (string key in new[] { "Power", "Armor", "Provision" })
        {
            var input = new NumericUpDown { Minimum = 0, Maximum = 99999, Width = 120 };
            input.ValueChanged += (_, _) => MarkDraft();
            fields[key] = input;
            AddRow(key, input);
        }
        foreach (string key in new[] { "Tier", "Rarity", "FactionId", "SecondaryFactionId" })
        {
            var input = new EditorComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            input.SelectedIndexChanged += (_, _) => MarkDraft();
            fields[key] = input;
            AddRow(key, input);
        }
        initialZeal.CheckedChanged += (_, _) => MarkDraft();
        AddRow("Initial", initialZeal);
        AddWide(zealNote);
        var statusPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        foreach (string key in EditorProject.InitialStatusMasks.Keys)
        {
            var input = new EditorCheckBox { AutoSize = true };
            input.CheckedChanged += (_, _) => MarkDraft();
            initialStatuses[key] = input; statusPanel.Controls.Add(input);
        }
        AddRow("InitialStatuses", statusPanel);
        var primary = new EditorComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        primary.SelectedIndexChanged += (_, _) => MarkDraft();
        fields["PrimaryCategory"] = primary;
        AddRow("PrimaryCategory", primary);
        AddRow("Categories", categories);
        AddWide(Bind(new Label { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(530, 0) }, "UnknownBits"));
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        actions.Controls.AddRange([Bind(apply, "Apply"), Bind(revert, "Revert"), Bind(relatedCards, "RelatedCards")]);
        actions.Padding = new Padding(12, 5, 5, 7);
        right.Controls.Add(actions, 0, 1);
    }
    private void AddWide(Control control)
    {
        int row = detail.RowCount++;
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0, 4, 0, 6);
        detail.Controls.Add(control, 0, row);
        detail.SetColumnSpan(control, 2);
    }
    private void AddRow(string key, Control control)
    {
        int row = detail.RowCount++;
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.Controls.Add(Bind(new Label { AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(160, 0) }, key), 0, row);
        control.Margin = new Padding(0, 3, 0, 5);
        detail.Controls.Add(control, 1, row);
    }

    private void Translate()
    {
        rendering = true;
        Text = UiText.Get("Title") + " " + typeof(EditorForm).Assembly.GetName().Version?.ToString(3);
        foreach (var (control, key) in translated) control.Text = UiText.Get(key);
        redo.Text = UiText.Language == "ru-ru" ? "Повторить" : "Redo";
        historyButton.Text = UiText.Language == "ru-ru" ? "История" : "History";
        search.PlaceholderText = UiText.Get("Search");
        sort.Items.Clear();
        sort.Items.AddRange(new[] { "Name", "Rarity", "Power", "Provision", "ID" }.Select(UiText.Get).Cast<object>().ToArray());
        sort.SelectedIndex = currentSort;
        group.Items.Clear();
        group.Items.AddRange(new[] { "MainCards", "Symbols", "AllCards", "Leaders" }.Select(UiText.Get).Cast<object>().ToArray());
        group.SelectedIndex = currentGroup;
        factions.TabPages.Clear();
        foreach (string key in FactionKeys) factions.TabPages.Add(UiText.Get(key));
        factions.SelectedIndex = currentFactionIndex;
        foreach (DataGridViewColumn column in grid.Columns) column.HeaderText = column.Name == "Id" ? "ID" : UiText.Get(column.Name == "Provision" ? "ShortProvision" : column.Name);
        FillChoice("Tier", [2, 8], ["Bronze", "Gold"]);
        FillChoice("Rarity", EditorProject.Rarities, ["Common", "Rare", "Epic", "Legendary"]);
        FillChoice("FactionId", EditorProject.Factions, FactionKeys);
        FillChoice("SecondaryFactionId", [0, .. EditorProject.Factions], ["None", .. FactionKeys]);
        categories.Items.Clear();
        foreach (var category in CategoryRegistry.All.OrderBy(x => CategoryLabel(x.Name), CultureComparer()))
            categories.Items.Add(new CategoryChoice(category.Name, CategoryLabel(category.Name)));
        foreach (TabPage oldPage in textTabs.TabPages.Cast<TabPage>().ToArray()) oldPage.Dispose();
        textTabs.TabPages.Clear();
        textFields.Clear();
        textTabs.Multiline = true;
        foreach (string locale in EditorProject.Languages)
        {
            var page = new TabPage(LanguageCatalog.Name(locale));
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), ColumnCount = 1, RowCount = 6 };
            foreach (string field in EditorProject.TextFields)
            {
                int row = table.Controls.Count;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.Controls.Add(new Label { AutoSize = true, Text = UiText.Get(field), Dock = DockStyle.Fill }, 0, row);
                table.RowStyles.Add(new RowStyle(field == "name" ? SizeType.Absolute : SizeType.Percent, field == "name" ? 34 : 50));
                var input = new TextBox { Dock = DockStyle.Fill, Multiline = field != "name", ScrollBars = field == "name" ? ScrollBars.None : ScrollBars.Vertical, MaxLength = 50000 };
                input.TextChanged += (_, _) => MarkDraft();
                table.Controls.Add(input, 0, row + 1);
                textFields[(locale, field)] = input;
            }
            page.Controls.Add(table);
            textTabs.TabPages.Add(page);
        }
        textTabs.SelectedIndex = Array.IndexOf(EditorProject.Languages, "en-us");
        rendering = false;
        RefreshList(selectedId);
        ApplyAppearance();
    }
    private static StringComparer CultureComparer() => StringComparer.Create(CultureInfo.GetCultureInfo(UiText.Language), true);
    private void FillChoice(string field, int[] values, string[] keys)
    {
        var combo = (ComboBox)fields[field];
        combo.Items.Clear();
        for (int i = 0; i < values.Length; i++) combo.Items.Add(new Choice(values[i], UiText.Get(keys[i])));
    }
    private void MarkDraft()
    {
        if (rendering || project == null || !selectedId.HasValue) return;
        draftDirty = true;
        UpdateActions();
    }
    private void FilterChanged()
    {
        if (rendering) return;
        Guard(() =>
        {
            if (!ResolveDraft())
            {
                rendering = true;
                factions.SelectedIndex = currentFactionIndex;
                hidden.Checked = currentHidden;
                search.Text = currentSearch;
                sort.SelectedIndex = currentSort;
                group.SelectedIndex = currentGroup;
                rendering = false;
                return;
            }
            currentFactionIndex = Math.Max(0, factions.SelectedIndex);
            currentHidden = hidden.Checked;
            currentSearch = search.Text;
            int selectedSort = Math.Max(0, sort.SelectedIndex);
            if (selectedSort != currentSort) { descending = false; lastHeaderColumn = -1; }
            currentSort = selectedSort;
            currentGroup = Math.Max(0, group.SelectedIndex);
            RefreshList(selectedId);
        });
    }
    private void SortColumn(int column)
    {
        if (column < 0 || !ResolveDraft()) return;
        int next = grid.Columns[column].Name switch { "Rarity" => 1, "Power" => 2, "Provision" => 3, "Id" => 4, _ => 0 };
        descending = lastHeaderColumn == column ? !descending : false;
        lastHeaderColumn = column;
        currentSort = next;
        rendering = true; sort.SelectedIndex = next; rendering = false;
        RefreshList(selectedId);
    }
    private void RefreshList(int? preferred)
    {
        rendering = true;
        grid.Rows.Clear();
        if (project != null)
        {
            string query = currentSearch.Trim();
            var ordered = CardCatalog.Query(project, EditorProject.Factions[currentFactionIndex], UiText.Language,
                query, (CardGroup)currentGroup, currentHidden, (CardSort)currentSort, descending, BalanceCardView);
            foreach (CardEntry c in ordered)
            {
                int row = grid.Rows.Add(c.Id, project.Name(c.Id, UiText.Language), Rarity(c.Rarity), c.Type == 4 ? c.Power < 0 ? "?" : c.Power : "", c.Type == 16 ? "" : c.Provision < 0 ? "?" : c.Provision);
                grid.Rows[row].Tag = c.Id;
                if (!c.Editable) grid.Rows[row].DefaultCellStyle.ForeColor = Theme.Mix(Color.FromArgb(settings.Background), Theme.Text(Color.FromArgb(settings.Background)), .6);
            }
        }
        grid.ClearSelection();
        DataGridViewRow? chosen = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (int)r.Tag! == preferred)
            ?? grid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
        if (chosen != null) { grid.CurrentCell = chosen.Cells[0]; chosen.Selected = true; }
        rendering = false;
        if (chosen != null) ShowCard((int)chosen.Tag!);
        else
        {
            selectedId = null; draftDirty = false;
            heading.Text = UiText.Get(project == null ? "NoProject" : "NoCards"); identity.Text = ""; reason.Text = ""; detail.Enabled = false;
            rendering = true;
            foreach (TextBox input in textFields.Values) { input.Text = ""; input.Enabled = false; }
            numberTable.Controls.Clear(); numberInputs.Clear(); numberValues = Array.Empty<AbilityNumber>();
            initialZeal.Checked = false; initialZeal.Enabled = false; zealNote.Text = "";
            foreach (var input in initialStatuses.Values) { input.Checked = false; input.Enabled = false; }
            rendering = false;
        }
        UpdateActions();
        foreach (DataGridViewColumn column in grid.Columns) column.HeaderCell.SortGlyphDirection = SortOrder.None;
        grid.Columns[new[] { "Name", "Rarity", "Power", "Provision", "Id" }[currentSort]]!.HeaderCell.SortGlyphDirection = descending ? SortOrder.Descending : SortOrder.Ascending;
        if (appearanceFont != null) Theme.Apply(grid, settings, appearanceFont);
    }
    private static string Rarity(int value) => UiText.Get(value switch { 1 => "Common", 2 => "Rare", 4 => "Epic", 8 => "Legendary", _ => "Other" });
    private void SelectionChanged()
    {
        if (rendering || grid.SelectedRows.Count == 0) return;
        int id = (int)grid.SelectedRows[0].Tag!;
        if (id == selectedId) return;
        if (!ResolveDraft())
        {
            rendering = true;
            grid.ClearSelection();
            var previous = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (int)r.Tag! == selectedId);
            if (previous != null) { grid.CurrentCell = previous.Cells[0]; previous.Selected = true; }
            rendering = false;
            return;
        }
        ShowCard(id);
        rendering = true;
        var chosen = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (int)r.Tag! == id);
        if (chosen != null) { grid.ClearSelection(); grid.CurrentCell = chosen.Cells[0]; chosen.Selected = true; }
        rendering = false;
    }
    private void ShowCard(int id)
    {
        if (project == null) return;
        rendering = true;
        selectedId = id;
        draftDirty = false;
        detail.Enabled = true;
        CardEntry card = project.Card(id);
        var balanceCard = BalanceCardView(card);
        RefreshBalanceNote(card);
        heading.Text = project.Name(id, UiText.Language);
        parameterScroll.AutoScrollPosition = Point.Empty;
        identity.Text = $"ID {id}   |   {UiText.Get(card.Type switch { 1 => "Leader", 4 => "Unit", 2 => "Special", 8 => "Artifact", 16 => "Stratagem", _ => "Other" })}";
        foreach (var label in translated.Where(t => t.Key == "Provision"))
            label.Control.Text = UiText.Get(card.Type == 1 ? "LeaderProvision" : "Provision");
        reason.Text = card.Editable ? "" : UiText.Get(card.BlockReason);
        reason.Visible = reason.Text.Length > 0;
        var zealSetting = project.Zeal(id);
        initialZeal.Text = GameKeyword("zeal");
        initialZeal.Checked = zealSetting.Value;
        initialZeal.Enabled = zealSetting.Editable;
        zealNote.Text = UiText.Get(zealSetting.Editable ? "DescriptionNotChanged" : zealSetting.Reason);
        var inspection = project.InspectInitialStatuses(id);
        var statuses = inspection.Settings;
        foreach (var setting in statuses)
        {
            var input = initialStatuses[setting.Key];
            input.Text = GameKeyword(setting.Key); input.Checked = setting.Value; input.Enabled = setting.Editable;
            input.AccessibleDescription = InitialStatusReason(setting, inspection.Blockers);
            input.Cursor = setting.Editable ? Cursors.Default : Cursors.Help;
        }
        foreach (var setting in statuses)
            sourceToolTip.SetToolTip(initialStatuses[setting.Key], setting.Editable ? UiText.Get("InitialStatusDescription") : InitialStatusReason(setting, inspection.Blockers));
        foreach (var (key, value) in new[] { ("Power", balanceCard.Power), ("Armor", card.Armor), ("Provision", balanceCard.Provision) })
        {
            var input = (NumericUpDown)fields[key];
            input.Minimum = 0;
            input.Maximum = Math.Max(value, key == "Provision" ? 99 : 999);
            input.Value = Math.Clamp(value, (int)input.Minimum, (int)input.Maximum);
            input.Minimum = key == "Power" && value > 0 ? 1 : 0;
            input.Enabled = card.Editable && (key == "Provision" ? card.Type != 16 : card.Type == 4);
            if (key is "Power" or "Provision") ConfigureBalanceInput(card, key, input);
        }
        var tierChoice = (ComboBox)fields["Tier"];
        foreach (var old in tierChoice.Items.Cast<Choice>().Where(c => c.Value == 1).ToArray()) tierChoice.Items.Remove(old);
        if (card.Type == 1) tierChoice.Items.Add(new Choice(1, UiText.Get("Leader")));
        foreach (var (key, value) in new[] { ("Tier", card.Tier), ("Rarity", card.Rarity), ("FactionId", card.Faction), ("SecondaryFactionId", card.SecondaryFaction) })
        {
            var input = (ComboBox)fields[key];
            input.SelectedItem = input.Items.Cast<Choice>().FirstOrDefault(c => c.Value == value);
            input.Enabled = card.Editable && card.Type != 1;
        }
        var currentCategories = project.Categories(id);
        var primary = (ComboBox)fields["PrimaryCategory"];
        string? primaryName = project.PrimaryCategory(id);
        primary.Items.Clear();
        primary.Items.Add(new CategoryChoice("", UiText.Get("None")));
        foreach (var category in CategoryRegistry.All.OrderBy(x => CategoryLabel(x.Name), CultureComparer()))
            primary.Items.Add(new CategoryChoice(category.Name, CategoryLabel(category.Name)));
        primary.SelectedItem = primary.Items.Cast<CategoryChoice>().FirstOrDefault(x => x.Name == primaryName);
        if (primaryName == null) { primary.Items.Add(new CategoryChoice("?", UiText.Get("KeepOriginal"))); primary.SelectedIndex = primary.Items.Count - 1; }
        primary.Enabled = card.Editable && card.Type != 1 && primaryName != null;
        categories.Enabled = card.Editable && card.Type != 1;
        for (int i = 0; i < categories.Items.Count; i++) categories.SetItemChecked(i, currentCategories.Contains(((CategoryChoice)categories.Items[i]).Name));
        foreach (var ((locale, field), input) in textFields)
        {
            bool exists = project.HasText(id, locale, field);
            input.Enabled = card.Editable && exists;
            input.Text = exists ? project.Text(id, locale, field) : "";
            input.PlaceholderText = UiText.Get(project.AvailableLanguages.Contains(locale) ? "MissingText" : "MissingLocale");
        }
        RenderNumbers(id);
        rendering = false;
        UpdateActions();
    }
    private void RenderNumbers(int id)
    {
        foreach (Control control in numberTable.Controls.Cast<Control>().ToArray()) control.Dispose();
        numberTable.Controls.Clear(); numberTable.RowStyles.Clear(); numberTable.RowCount = 0;
        numberInputs.Clear();
        var symbiosis = project!.AdditionalSymbiosis(id);
        additionalSymbiosis = new EditorCheckBox { AutoSize = true, Text = GameKeyword("symbiosis"), Checked = symbiosis.Value, Enabled = symbiosis.Editable };
        additionalSymbiosis.CheckedChanged += (_,_) => MarkDraft();
        var additions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin = new Padding(0,0,0,10) };
        additions.Controls.Add(new Label { AutoSize = true, Text = UiText.Get("AdditionalAbilities"), Margin = new Padding(0,5,12,0) });
        var moduleChoices=new FlowLayoutPanel {AutoSize=true,Margin=Padding.Empty,WrapContents=true};
        void SizeModuleChoices()
        {
            int visible=Math.Min(numberTable.ClientSize.Width,numberTable.Parent?.ClientSize.Width??numberTable.ClientSize.Width);
            int width=Math.Max(120,visible-numberTable.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth-8);
            if(moduleChoices.MaximumSize.Width!=width)moduleChoices.MaximumSize=new Size(width,0);
        }
        additions.SizeChanged+=(_,_)=>SizeModuleChoices();
        SizeModuleChoices();
        additions.Controls.Add(moduleChoices);
        moduleChoices.Controls.Add(additionalSymbiosis);
        var assimilate=project.AdditionalAssimilate(id);
        additionalAssimilate=new EditorCheckBox {AutoSize=true,Text=GameKeyword("assimilate"),Checked=assimilate.Value,Enabled=assimilate.Editable};
        additionalAssimilate.CheckedChanged+=(_,_)=>MarkDraft();
        moduleChoices.Controls.Add(additionalAssimilate);
        var harmony=project.AdditionalHarmony(id);
        additionalHarmony=new EditorCheckBox {AutoSize=true,Text=GameKeyword("harmony"),Checked=harmony.Value,Enabled=harmony.Editable};
        additionalHarmony.CheckedChanged+=(_,_)=>MarkDraft();
        moduleChoices.Controls.Add(additionalHarmony);
        var intimidate=project.AdditionalIntimidate(id);
        additionalIntimidate=new EditorCheckBox {AutoSize=true,Text=GameKeyword("intimidate"),Checked=intimidate.Value,Enabled=intimidate.Editable};
        additionalIntimidate.CheckedChanged+=(_,_)=>MarkDraft();
        moduleChoices.Controls.Add(additionalIntimidate);
        var thrive=project.AdditionalThrive(id);
        additionalThrive=new EditorCheckBox {AutoSize=true,Text=GameKeyword("thrive"),Checked=thrive.Value,Enabled=thrive.Editable};
        additionalThrive.CheckedChanged+=(_,_)=>MarkDraft();
        moduleChoices.Controls.Add(additionalThrive);
        var veteran=project.AdditionalVeteran(id);
        additionalVeteran=new EditorCheckBox {AutoSize=true,Text=GameKeyword("veteran"),Checked=veteran.Value,Enabled=veteran.Editable};
        additionalVeteran.CheckedChanged+=(_,_)=>MarkDraft();
        moduleChoices.Controls.Add(additionalVeteran);
        var formation=project.AdditionalFormation(id);
        additionalFormation=new EditorCheckBox {AutoSize=true,Text=GameKeyword("formation"),Checked=formation.Value,Enabled=formation.Editable};
        additionalFormation.CheckedChanged+=(_,_)=>
        {
            if(additionalFormation.Checked)initialZeal.Checked=false;
            initialZeal.Enabled=!additionalFormation.Checked && project.Zeal(id).Editable;
            MarkDraft();
        };
        moduleChoices.Controls.Add(additionalFormation);
        var profit=project.AdditionalProfit(id);
        var profitCard=project.Card(id);
        additionalProfit=new EditorCheckBox {AutoSize=true,Text=GameKeyword("profit"),Checked=profit.Value,Enabled=profit.Editable,Visible=profitCard.Faction==64 || profitCard.SecondaryFaction==64};
        additionalProfit.CheckedChanged+=(_,_)=>MarkDraft();
        moduleChoices.Controls.Add(additionalProfit);
        var resupply=project.AdditionalResupply(id);
        additionalResupply=new EditorCheckBox {AutoSize=true,Text=GameKeyword("resupply"),Checked=resupply.Value,Enabled=resupply.Editable};
        additionalResupply.CheckedChanged+=(_,_)=>MarkDraft();
        moduleChoices.Controls.Add(additionalResupply);
        var echo=project.AdditionalEcho(id);
        additionalEcho=new EditorCheckBox {AutoSize=true,Text=GameKeyword("echo"),Checked=echo.Value,Enabled=echo.Editable,Visible=profitCard.Type==2};
        additionalEcho.CheckedChanged+=(_,_)=>MarkDraft();
        moduleChoices.Controls.Add(additionalEcho);
        var flanking=project.AdditionalFlanking(id);
        additionalFlanking=new EditorCheckBox {AutoSize=true,Text=GameKeyword("flanking"),Checked=flanking.Value,Enabled=flanking.Editable,Visible=profitCard.Type==4};
        additionalFlanking.CheckedChanged+=(_,_)=>MarkDraft();
        moduleChoices.Controls.Add(additionalFlanking);
        var insanity=project.AdditionalInsanity(id);
        additionalInsanity=new EditorCheckBox {AutoSize=true,Text=GameKeyword("insanity"),Checked=insanity.Value,Enabled=insanity.Editable,Visible=profitCard.Type==4 && (profitCard.Faction==64 || profitCard.SecondaryFaction==64)};
        additionalInsanity.CheckedChanged+=(_,_)=>MarkDraft();
        moduleChoices.Controls.Add(additionalInsanity);
        CheckBox[] modules=[additionalSymbiosis,additionalAssimilate,additionalHarmony,additionalIntimidate,additionalThrive,additionalVeteran,additionalFormation,additionalProfit,additionalResupply,additionalEcho,additionalFlanking,additionalInsanity];
        foreach(var input in modules)input.CheckedChanged+=(_,_)=>
        {
            if(!rendering && input.Checked)foreach(var other in modules.Where(c=>c!=input && c.Enabled && c.Checked))other.Checked=false;
        };
        RenderAdditionalValues(id,moduleChoices);
        RenderNativeVeteran(id,moduleChoices);
        numberTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        numberTable.Controls.Add(additions,0,numberTable.RowCount++); numberTable.SetColumnSpan(additions,2);
        numberValues = project!.Numbers(id).Where(value => value.Editable).ToArray();
        if (numberValues.Count == 0)
        {
            var empty = new Label { AutoSize = true, Text = UiText.Get("NoAbilityNumbers"), Dock = DockStyle.Fill };
            numberTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            numberTable.Controls.Add(empty, 0, numberTable.RowCount++); numberTable.SetColumnSpan(empty, 2);
        }
        foreach (var numberGroup in project.NumberGroups(id))
        {
            if(nativeVeteranNumber.HasValue && numberGroup.Members.Any(n=>n.Id==nativeVeteranNumber.Value))continue;
            AbilityNumber value = numberGroup.Display;
            int row = numberTable.RowCount++;
            numberTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            string role = AbilityLabel(value.LabelRole ?? value.Role);
            var label = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 8, 3),
                Text = $"{role}\n{AbilityContext(value.Context)}" };
            numberTable.Controls.Add(label, 0, row);
            var input = new NumericUpDown { Dock = DockStyle.Top, Minimum = value.ToDisplayValue(Math.Min(value.Minimum, value.Value)),
                Maximum = value.ToDisplayValue(Math.Max(value.Maximum, value.Value)), Value = value.DisplayValue, Margin = new Padding(0, 8, 0, 3) };
            input.ValueChanged += (_, _) => MarkDraft();
            numberTable.Controls.Add(input, 1, row);
            foreach (var member in numberGroup.Members) numberInputs[member.Id] = input;
            int noteRow = numberTable.RowCount++;
            numberTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var note = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10),
                Text = DescriptionNote(id, numberGroup) };
            numberTable.Controls.Add(note, 0, noteRow); numberTable.SetColumnSpan(note, 2);
        }
        if (project.HasDescriptionRepair(id))
        {
            var repair = new Button { AutoSize = true, Text = UiText.Get("RepairDescription"), Tag = "RepairDescription" };
            repair.Click += (_, _) => Guard(() => { MarkDraft(); CommitDraft(false); });
            numberTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            numberTable.Controls.Add(repair, 0, numberTable.RowCount++); numberTable.SetColumnSpan(repair, 2);
        }
        if (appearanceFont != null) Theme.Apply(numberTable, settings, appearanceFont);
    }
    private CardEdit Draft()
    {
        int Number(string key) => (int)((NumericUpDown)fields[key]).Value;
        int Selected(string key) => ((ComboBox)fields[key]).SelectedItem is Choice c ? c.Value : -1;
        return new(StoredBalanceInput("Power"), Number("Armor"), StoredBalanceInput("Provision"), Selected("Tier"), Selected("Rarity"), Selected("FactionId"),
            categories.CheckedItems.Cast<CategoryChoice>().Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            textFields.Where(p => p.Value.Enabled).GroupBy(p => p.Key.Language).ToDictionary(g => g.Key,
                g => (IReadOnlyDictionary<string, string>)g.ToDictionary(x => x.Key.Field, x => x.Value.Text)),
            ((ComboBox)fields["PrimaryCategory"]).SelectedItem is CategoryChoice main && main.Name != "?" ? main.Name : null,
            numberValues.Where(v => v.Editable && numberInputs.TryGetValue(v.Id, out var input) && (int)input.Value != v.DisplayValue)
                .ToDictionary(v => v.Id, v => v.ToStoredValue((int)numberInputs[v.Id].Value)),
            initialZeal.Enabled ? initialZeal.Checked : null,
            initialStatuses.Where(p => p.Value.Enabled).ToDictionary(p => p.Key, p => p.Value.Checked),
            additionalSymbiosis?.Enabled == true ? additionalSymbiosis.Checked : null,
            additionalAssimilate?.Enabled == true ? additionalAssimilate.Checked : null,
            additionalHarmony?.Enabled == true ? additionalHarmony.Checked : null,
            additionalIntimidate?.Enabled == true ? additionalIntimidate.Checked : null,
            additionalThrive?.Enabled == true ? additionalThrive.Checked : null,
            additionalVeteran?.Enabled == true ? additionalVeteran.Checked : null,
            additionalFormation?.Enabled == true ? additionalFormation.Checked : null,
            additionalValueInputs.Where(p=>p.Value.Enabled).ToDictionary(p=>p.Key,p=>(int)p.Value.Value),
            additionalProfit?.Enabled==true?additionalProfit.Checked:null,
            additionalResupply?.Enabled==true?additionalResupply.Checked:null,
            additionalEcho?.Enabled==true?additionalEcho.Checked:null,
            additionalFlanking?.Enabled==true?additionalFlanking.Checked:null,
            additionalInsanity?.Enabled==true?additionalInsanity.Checked:null,
            SecondaryFaction: Selected("SecondaryFactionId"));
    }
    private bool CommitDraft(bool confirm)
    {
        if (project == null || !selectedId.HasValue || !draftDirty) return true;
        CardEdit draft = Draft();
        int id = selectedId.Value;
        IReadOnlyList<Change> changes = project.Preview(id, draft);
        if (changes.Count == 0) { draftDirty = false; UpdateActions(); return true; }
        if (confirm && !ConfirmChanges(changes, project.DescriptionWarnings(id, draft))) return false;
        TrackGeneratedLocalizationChanges(id, draft, changes);
        if (!project.Apply(id, draft)) { draftDirty = false; UpdateActions(); return true; }
        stagedUndo.Push(CloneStagedEdits());
        stagedRedo.Clear();
        stagedEdits.Remove(id);
        if (HasChangesForCard(id)) stagedEdits[id] = draft;
        lastReviewedSignature = "";
        draftDirty = false;
        RefreshList(id);
        return true;
    }
    private bool ResolveDraft()
    {
        if (!draftDirty) return true;
        try { return CommitDraft(false); }
        catch (Exception e) { MessageBox.Show(this, UiText.Error(e), UiText.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
    }
    private bool ConfirmChanges(IReadOnlyList<Change> changes, IReadOnlyList<DescriptionWarning>? descriptionWarnings = null)
    {
        using var dialog = CreateChangeConfirmation(changes, descriptionWarnings);
        return dialog.ShowDialog(this) == DialogResult.OK;
    }
    private Form CreateChangeConfirmation(IReadOnlyList<Change> changes, IReadOnlyList<DescriptionWarning>? descriptionWarnings)
    {
        var dialog = new Form { Text = UiText.Get("Confirm"), Size = new Size(850, 570), MinimumSize = new Size(650, 420), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var balanceWarning = new Label { AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(790, 0),
            Text = selectedId.HasValue && project?.Card(selectedId.Value).IsToken == true ? UiText.Get("TokenLocalNote")
                : selectedId.HasValue && project?.Card(selectedId.Value).Type == 1 ? UiText.Get("LeaderLocalNote")
                : selectedId.HasValue && UsesLocalBalance(project?.Card(selectedId.Value)) ? UiText.Get("LocalBalanceNote")
                : balanceCouncil.Checked ? UiText.Get("CouncilExportNote") : UiText.Get("LocalBalanceNote"), Visible = changes.Any(c => c.Field is "Power" or "Provision" || c.Field.EndsWith("/Power") || c.Field.EndsWith("/Provision")),
            Margin = new Padding(0, 0, 0, 10) };
        var warnings = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        warnings.Controls.Add(balanceWarning);
        layout.Controls.Add(warnings, 0, 0);
        var listing = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
        listing.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        listing.Columns.Add("field", UiText.Get("Changes"));
        listing.Columns.Add("before", UiText.Get("Before"));
        listing.Columns.Add("after", UiText.Get("After"));
        foreach (Change change in VisibleNumberChanges(changes)) listing.Rows.Add(ChangeName(change.Field), ChangeValue(change.Field, change.Before), ChangeValue(change.Field, change.After));
        layout.Controls.Add(listing, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = UiText.Get("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([ok, cancel]);
        layout.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        return dialog;
    }
    private string ChangeName(string field)
    {
        string[] parts = field.Split('/');
        if (parts.Length == 2 && EditorProject.Languages.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
        {
            int separator = parts[1].IndexOf('_');
            if (separator > 0 && int.TryParse(parts[1].AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                return UiText.Get(parts[1][(separator + 1)..]) + " · " + LanguageCatalog.Name(parts[0]);
        }
        if (parts[^1] == "Categories") return UiText.Get("Categories");
        if (parts[^1] == "PrimaryCategory") return UiText.Get("PrimaryCategory");
        if(parts[^1]=="GigaChoiceIsolation")return UiText.Get("GigaChoiceIsolation");
        if(parts.Length>=2 && parts[^2]=="AdditionalValue")return GameKeyword(parts[^1]);
        if (parts[^1] == "AdditionalSymbiosis") return GameKeyword("symbiosis");
        if (parts[^1] == "AdditionalAssimilate") return GameKeyword("assimilate");
        if (parts[^1] == "AdditionalHarmony") return GameKeyword("harmony");
        if (parts[^1] == "AdditionalIntimidate") return GameKeyword("intimidate");
        if (parts[^1] == "AdditionalThrive") return GameKeyword("thrive");
        if (parts[^1] == "AdditionalVeteran") return GameKeyword("veteran");
        if (parts[^1] == "AdditionalFormation") return GameKeyword("formation");
        if (parts[^1] == "AdditionalProfit") return GameKeyword("profit");
        if (parts[^1] == "AdditionalResupply") return GameKeyword("resupply");
        if (parts[^1] == "AdditionalEcho") return GameKeyword("echo");
        if (parts[^1] == "AdditionalFlanking") return GameKeyword("flanking");
        if (parts[^1] == "Madness") return GameKeyword("insanity");
        if (parts[^1] == "Provision")
        {
            int? owner = parts.Length == 2 && int.TryParse(parts[0], out int leaderId) ? leaderId : selectedId;
            if (owner.HasValue && project?.Card(owner.Value).Type == 1) return UiText.Get("LeaderProvision");
        }
        if (parts[^1] == "SummoningSickness") return UiText.Get("Initial") + " · " + GameKeyword("zeal");
        if (parts[^1] == "InitalTapCharges") return AbilityLabel("initial-charges");
        if (parts[^1] == "Tokens") return UiText.Get("InitialStatuses");
        if (parts[^1] == "InitialTimer") return AbilityLabel("initial-timer");
        if (parts.Length == 3 && parts[1] == "Ability" && int.TryParse(parts[0], out int card) && int.TryParse(parts[2].TrimStart('#'), out int variable))
        {
            if(card==202515 && variable is 49 or 57)return project?.Name(variable==49?201709:201710,UiText.Language)+": "+AbilityLabel(variable==49?"giga-condense-damage":"giga-spread-total");
            if (card == 202674 && variable is >= 1130 and <= 1133)
            {
                int choice = variable == 1133 ? 202702 : 202700 + variable - 1130;
                return $"{project?.Name(choice, UiText.Language)}: {AbilityLabel(variable == 1133 ? "vitality-duration" : "boost")}";
            }
            if (card == 202674 && variable is 1150 or 1151) return AbilityLabel("nature-shared-targets");
            var refill = project?.ChargeRefill(card);
            var computed = project?.VerifiedEffects(card).FirstOrDefault(s => s.StoredValues(s.Value).ContainsKey(variable));
            if (computed != null) return AbilityLabel(computed.Role);
            if (refill != null && (refill.TextVariableId == variable || refill.LiteralVariableId == variable))
                return AbilityLabel("charge-refill");
            var timing = project?.Timing(card).FirstOrDefault(s => s.TextVariableId == variable || s.GraphValues.Contains(variable));
            if (timing != null) return AbilityLabel(timing.Role);
            AbilityNumber? value = project?.Numbers(card).FirstOrDefault(n => n.Id == variable);
            var group = project?.NumberGroups(card).FirstOrDefault(g => g.Members.Any(v => v.Id == variable));
            if (value != null) return AbilityLabel(group?.Display.LabelRole ?? group?.Display.Role ?? value.LabelRole ?? value.Role) + " · " + AbilityContext(value.Context);
            return UiText.Get("AbilityValueFallback");
        }
        return parts.Length >= 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? UiText.Get(parts[^1]) : UiText.Get(field);
    }
    private IReadOnlyList<Change> VisibleNumberChanges(IReadOnlyList<Change> changes)
    {
        if (project == null) return changes;
        var hiddenFields = new HashSet<string>();
        foreach (var (choice, alias, parent) in new[] { (202700, 1, 1130), (202701, 1, 1131), (202702, 1, 1132), (202702, 2, 1133) })
        {
            var child = changes.FirstOrDefault(c => c.Field == $"{choice}/Ability/#{alias}");
            var owner = changes.FirstOrDefault(c => c.Field == $"202674/Ability/#{parent}");
            if (child != null && owner != null && child.Before == owner.Before && child.After == owner.After) hiddenFields.Add(owner.Field);
        }
        var min = changes.FirstOrDefault(c => c.Field == "202674/Ability/#1150");
        var max = changes.FirstOrDefault(c => c.Field == "202674/Ability/#1151");
        if (min != null && max != null && min.Before == max.Before && min.After == max.After) hiddenFields.Add(max.Field);
        foreach (int card in changes.Select(c => c.Field.Split('/')[0]).Distinct()
            .Select(s => int.TryParse(s, out int id) ? id : -1).Where(id => id >= 0))
        {
            var charge = project.InitialCharges(card);
            var refill = project.ChargeRefill(card);
            foreach (var effect in project.VerifiedEffects(card))
            {
                var main = changes.FirstOrDefault(c => c.Field == $"{card}/Ability/#{effect.TextVariableId}");
                if (main == null || !int.TryParse(main.Before, out int before) || !int.TryParse(main.After, out int after)) continue;
                var oldValues = effect.StoredValues(before); var newValues = effect.StoredValues(after);
                if (newValues.All(p => changes.Any(c => c.Field == $"{card}/Ability/#{p.Key}"
                    && c.Before == oldValues[p.Key].ToString(CultureInfo.InvariantCulture) && c.After == p.Value.ToString(CultureInfo.InvariantCulture))))
                    foreach (int field in newValues.Keys.Where(key => key != effect.TextVariableId)) hiddenFields.Add($"{card}/Ability/#{field}");
            }
            if (refill?.LiteralVariableId is int literal)
            {
                string aliasField = $"{card}/Ability/#{refill.TextVariableId}", literalField = $"{card}/Ability/#{literal}";
                var alias = changes.FirstOrDefault(c => c.Field == aliasField);
                var value = changes.FirstOrDefault(c => c.Field == literalField);
                if (alias != null && value != null && alias.Before == value.Before && alias.After == value.After) hiddenFields.Add(aliasField);
            }
            foreach (var timing in project.Timing(card))
            {
                if (timing.Id != -2 && timing.GraphValues.Count == 0) continue;
                string aliasField = $"{card}/Ability/#{timing.TextVariableId}";
                string mainField = timing.Id == -2 ? $"{card}/InitialTimer" : $"{card}/Ability/#{timing.GraphValues[0]}";
                var main = changes.FirstOrDefault(c => c.Field == mainField || selectedId == card && timing.Id == -2 && c.Field == "InitialTimer");
                var alias = changes.FirstOrDefault(c => c.Field == aliasField);
                static string PlainTiming(string value) => value.StartsWith('<') ? System.Xml.Linq.XElement.Parse(value).Value : value;
                if (main != null && alias != null && PlainTiming(main.Before) == alias.Before && PlainTiming(main.After) == alias.After)
                    hiddenFields.Add(aliasField);
            }
            if (charge.Editable && charge.TextVariableId.HasValue)
            {
                var templateChange = changes.FirstOrDefault(c => c.Field == $"{card}/InitalTapCharges"
                    || selectedId == card && c.Field == "InitalTapCharges");
                string aliasField = $"{card}/Ability/#{charge.TextVariableId}";
                var alias = changes.FirstOrDefault(c => c.Field == aliasField);
                static string Plain(string text) => text.StartsWith('<') ? System.Xml.Linq.XElement.Parse(text).Value : text;
                if (templateChange != null && alias != null && Plain(templateChange.Before) == alias.Before
                    && Plain(templateChange.After) == alias.After) hiddenFields.Add(aliasField);
            }
            foreach (var group in project.NumberGroups(card).Where(g => g.Members.Count == 2))
            {
                string first = $"{card}/Ability/#{group.Members[0].Id}", second = $"{card}/Ability/#{group.Members[1].Id}";
                var a = changes.FirstOrDefault(c => c.Field == first);
                var b = changes.FirstOrDefault(c => c.Field == second);
                if (a != null && b != null && a.Before == b.Before && a.After == b.After) hiddenFields.Add(second);
            }
        }
        return changes.Where(c => !hiddenFields.Contains(c.Field)).ToArray();
    }
    private string ChangeValue(string field, string raw)
    {
        string[] parts = field.Split('/');
        if (parts[^1] == "Categories") return FormatCategoryChange(raw);
        if (parts[^1] == "PrimaryCategory") return FormatPrimaryCategoryChange(raw);
        if (raw.StartsWith('<'))
        {
            try { raw = System.Xml.Linq.XElement.Parse(raw).Value; }
            catch (System.Xml.XmlException) { }
        }
        if (parts[^1] == "FactionId" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int faction))
            return FactionName(faction);
        if (parts[^1] == "SecondaryFactionId" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int secondaryFaction))
            return secondaryFaction == 0 ? UiText.Get("None") : FactionName(secondaryFaction);
        if (parts[^1] == "Rarity" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rarity))
            return rarity switch { 1 => UiText.Get("Common"), 2 => UiText.Get("Rare"), 4 => UiText.Get("Epic"), 8 => UiText.Get("Legendary"), _ => raw };
        if (parts[^1] == "Tier" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tier))
            return tier switch { 2 => UiText.Get("Bronze"), 8 => UiText.Get("Gold"), _ => raw };
        if (parts[^1] is "GigaChoiceIsolation" or "AdditionalSymbiosis" or "AdditionalAssimilate" or "AdditionalHarmony"
            or "AdditionalIntimidate" or "AdditionalThrive" or "AdditionalVeteran" or "AdditionalFormation"
            or "AdditionalProfit" or "AdditionalResupply" or "AdditionalEcho" or "AdditionalFlanking" or "Madness")
            return bool.TryParse(raw, out bool enabled) ? UiText.Get(enabled ? "Yes" : "No") : raw;
        if (parts[^1] == "Tokens")
        {
            string plain = raw.StartsWith('<') ? System.Xml.Linq.XElement.Parse(raw).Value : raw;
            if (int.TryParse(plain, out int mask)) return string.Join(", ", EditorProject.InitialStatusMasks.Select(s =>
                GameKeyword(s.Key) + ": " + UiText.Get((mask & s.Value) != 0 ? "Yes" : "No")));
        }
        if (parts[^1] is "Power" or "Provision" && balanceCouncil.Checked)
        {
            int? id = parts.Length == 2 && int.TryParse(parts[0], out int cardId) ? cardId : selectedId;
            string text = raw.StartsWith('<') ? System.Xml.Linq.XElement.Parse(raw).Value : raw;
            if (id.HasValue && int.TryParse(text, out int baseValue) && balanceProjection?.TryDelta(project!.Card(id.Value), out var delta) == true)
                return (baseValue + (parts[^1] == "Power" ? delta.Power : delta.Provision)).ToString(CultureInfo.InvariantCulture);
        }
        if (parts[^1] == "SummoningSickness")
        {
            string value = raw.StartsWith('<') ? System.Xml.Linq.XElement.Parse(raw).Value : raw;
            if (bool.TryParse(value, out bool sick)) return UiText.Get(!sick ? "Yes" : "No");
        }
        if (parts.Length == 3 && parts[1] == "Ability" && int.TryParse(parts[0], out int card)
            && int.TryParse(parts[2].TrimStart('#'), out int variable) && int.TryParse(raw, out int stored))
        {
            var number = project?.Numbers(card).FirstOrDefault(n => n.Id == variable);
            if (number != null) return number.ToDisplayValue(stored).ToString();
        }
        return raw;
    }
    private string FormatCategoryChange(string raw)
    {
        try
        {
            var xml = System.Xml.Linq.XElement.Parse(raw);
            var names = CategoryRegistry.All.Where(category =>
            {
                var slot = xml.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("e" + category.Slot, StringComparison.OrdinalIgnoreCase));
                var attribute = slot?.Attributes().FirstOrDefault(item => item.Name.LocalName.Equals("v", StringComparison.OrdinalIgnoreCase));
                return ulong.TryParse(attribute?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong mask)
                    && (mask & category.Mask) == category.Mask;
            }).Select(category => CategoryLabel(category.Name)).ToArray();
            return names.Length == 0 ? UiText.Get("None") : string.Join(", ", names);
        }
        catch (System.Xml.XmlException) { return raw; }
    }
    private string FormatPrimaryCategoryChange(string raw)
    {
        try
        {
            var xml = System.Xml.Linq.XElement.Parse(raw);
            ulong[] masks = Enumerable.Range(0, 3).Select(slot =>
            {
                var element = xml.Elements().FirstOrDefault(item => item.Name.LocalName.Equals("e" + slot, StringComparison.OrdinalIgnoreCase));
                var value = element?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals("v", StringComparison.OrdinalIgnoreCase))?.Value;
                return ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong mask) ? mask : 0UL;
            }).ToArray();
            var category = CategoryRegistry.All.FirstOrDefault(candidate =>
                Enumerable.Range(0, 3).All(slot => masks[slot] == (slot == candidate.Slot ? candidate.Mask : 0UL)));
            return category == null ? UiText.Get("None") : CategoryLabel(category.Name);
        }
        catch (System.Xml.XmlException) { return raw; }
    }
    private string FactionName(int faction)
    {
        int index = Array.IndexOf(EditorProject.Factions, faction);
        return index >= 0 ? UiText.Get(FactionKeys[index]) : faction.ToString(CultureInfo.InvariantCulture);
    }
    private string GameKeyword(string key)
    {
        string? local = project?.GameTerm(key, UiText.Language);
        if (local != null) return local;
        string english = project?.GameTerm(key, "en-us") ?? UiText.English("Keyword." + key);
        return english + (UiText.Language == "en-us" ? "" : " (EN)");
    }
    private string CategoryLabel(string name)=>project?.CategoryTerm(name,UiText.Language) ?? UiText.Get("category/"+name);
    private string AbilityContext(string context) => context is "Deploy" or "Order"
        ? GameKeyword(context.ToLowerInvariant()) : UiText.Get(context);
    private string AbilityLabel(string role) => role switch
    {
        "tribute-targets"=>GameKeyword("tribute")+" · "+UiText.Get("Ability.targets"),
        "without-tribute-targets"=>UiText.Get("WithoutTributeTargets"),
        "first-banish-choices"=>UiText.Get("FirstBanishChoices"),
        "extra-bronze-banish-choices"=>UiText.Get("ExtraBronzeBanishChoices"),
        "fee-single-damage"=>GameKeyword("fee")+" · "+UiText.Get("SingleTargetDamage"),
        "berserk-all-damage"=>GameKeyword("berserk")+" · "+UiText.Get("AllEnemyDamage"),
        "copy-count-unbonded"=>UiText.Get("CopiesWithoutCondition"),
        "copy-count-bonded"=>GameKeyword("bonded")+" · "+UiText.Get("Ability.copy-count"),
        "giga-condense-damage"=>UiText.Get("GigaCondenseDamage"),
        "giga-spread-total"=>UiText.Get("GigaSpreadTotal"),
        "veteran-strengthen"=>GameKeyword("veteran")+" · "+UiText.Get("Ability.strengthen"),
        "bran-start-veteran"=>GameKeyword("veteran")+" · "+UiText.Get("BranStartVeteran"),
        _ when role.StartsWith("keyword-boost:",StringComparison.Ordinal)=>GameKeyword(role["keyword-boost:".Length..])+" · "+UiText.Get("Ability.boost"),
        "initial-charges" => GameKeyword("charge") + " · " + UiText.Get("InitialValue"),
        "charge-refill" => GameKeyword("charge") + " · " + UiText.Get("Refill"),
        "initial-timer" => GameKeyword("timer"),
        "cooldown-reduction" => GameKeyword("cooldown") + " · " + UiText.Get("Reduction"),
        "adrenaline-threshold" => GameKeyword("adrenaline"),
        "bloodthirst" => GameKeyword("bloodthirst"),
        "berserk-threshold" => GameKeyword("berserk") + " · " + UiText.Get("Threshold"),
        "intimidate-boost" => GameKeyword("intimidate") + " · " + UiText.Get("Ability.boost"),
        "bleeding-duration" => GameKeyword("bleeding") + " · " + UiText.Get("Ability.duration"),
        "vitality-duration" => GameKeyword("vitality") + " · " + UiText.Get("Ability.duration"),
        "profit" => GameKeyword("profit"),
        "base-profit" => GameKeyword("profit") + " · " + UiText.Get("BaseValue"),
        "base-damage" => UiText.Get("Ability.damage") + " · " + UiText.Get("BaseValue"),
        "base-boost" => UiText.Get("Ability.boost") + " · " + UiText.Get("BaseValue"),
        "alternate-damage" => UiText.Get("Ability.damage") + " · " + UiText.Get("AlternativeValue"),
        "alternate-boost" => UiText.Get("Ability.boost") + " · " + UiText.Get("AlternativeValue"),
        "tribute-cost" => GameKeyword("tribute") + " · " + UiText.Get("Ability.cost"),
        "fee-cost" => GameKeyword("fee") + " · " + UiText.Get("Ability.cost"),
        "coin-gain" => GameKeyword("coin") + " · " + UiText.Get("Gain"),
        "cooldown" => GameKeyword("cooldown"),
        "ability-charges" or "ability-charge" => GameKeyword("charge"),
        _ => UiText.Get("Ability." + role)
    };
    private void UpdateActions()
    {
        RefreshArtState();
        int relatedCount = selectedId.HasValue && project != null ? project.RelatedCards(selectedId.Value).Count : 0;
        relatedCards.Enabled = relatedCount > 0;
        relatedCards.Text = UiText.Get("RelatedCards") + (relatedCount > 0 ? $" ({relatedCount})" : "");
        undo.Enabled = project?.CanUndo == true;
        redo.Enabled = project?.CanRedo == true;
        historyButton.Enabled = project != null && (project.CanUndo || project.CanRedo);
        apply.Enabled = draftDirty || project?.HasChanges == true;
        revert.Enabled = draftDirty;
        export.Enabled = draftDirty || project?.HasChanges == true;
        reset.Enabled = export.Enabled;
        foreach (var (control, _) in translated) if (control is Button button) Theme.ButtonColors(button, settings);
        source.Text = project == null ? UiText.Get("NoProject") : DescribeClientSource(project);
        sourceToolTip.SetToolTip(source, project == null ? "" : DescribeClientSource(project, includeHash: true));
        status.Text = $"{UiText.Get("Cards")}: {grid.Rows.Count}    |    {UiText.Get("Changes")}: {project?.Changes().Count ?? 0}{(draftDirty ? " *" : "")}";
        if (!UiText.Complete) status.Text += "    |    " + UiText.Get("TranslationPreview");
    }
    private bool Ask(string message) => MessageBox.Show(this, message, UiText.Get("Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    private void Guard(Action action)
    {
        if (rendering) return;
        try { action(); }
        catch (Exception e) { MessageBox.Show(this, UiText.Error(e), UiText.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private void OpenProject()
    {
        if (Unexported && !Ask(UiText.Get("Discard"))) return;
        using var chooser = new FolderBrowserDialog { Description = UiText.Get("OpenClient"), UseDescriptionForTitle = true,
            InitialDirectory = project == null ? AppContext.BaseDirectory : Directory.Exists(project.SourcePath) ? project.SourcePath : Path.GetDirectoryName(project.SourcePath) ?? AppContext.BaseDirectory };
        if (chooser.ShowDialog(this) == DialogResult.OK) LoadProject(chooser.SelectedPath);
    }
    private void LoadProject(string directory)
    {
        var next = ClientSource.Open(directory, Path.Combine(EditorSettings.Home, "Projects"));
        project = next;
        LoadBalanceSettings();
        lastExportSignature = "";
        lastReviewedSignature = "";
        generatedLocalizationChanges.Clear();
        stagedEdits.Clear(); stagedUndo.Clear(); stagedRedo.Clear();
        selectedId = null;
        draftDirty = false;
        RefreshList(null);
        settings.LastSource = next.Client?.ArchivePath ?? directory;
        SaveSettings();
    }
    private void ExportProject()
    {
        if (project == null || !ResolveDraft() || !project.HasChanges) return;
        string signature = Signature();
        if (lastReviewedSignature != signature
            && !ConfirmChanges(project.Changes(), project.PendingDescriptionWarnings())) return;
        string source = project.Client == null ? project.DirectoryPath : Path.GetDirectoryName(project.Client.ArchivePath)!;
        string? folder = PackageStorage.Prepare(this, source);
        if (folder == null) return;
        using var chooser = new FolderBrowserDialog { Description = UiText.Get("ExportFolder"), UseDescriptionForTitle = true, InitialDirectory = folder };
        if (chooser.ShowDialog(this) != DialogResult.OK) return;
        string output = PackageStorage.NewPath(chooser.SelectedPath, UiText.Get("BackupCards"), null, 80);
        project.ExportBalanceCouncil = balanceCouncil.Checked;
        project.ExportBalanceSnapshot = balanceProjection?.Snapshot;
        project.Export(output);
        PackageStorage.Describe(output, source, UiText.Get("BackupCards"));
        lastExportSignature = Signature();
        MessageBox.Show(this, UiText.Get(project.Client == null ? "Exported" : "ClientExported") + Environment.NewLine + output, UiText.Get("Title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        Process.Start(new ProcessStartInfo("explorer.exe", output) { UseShellExecute = true });
    }

    public void CloseForTest() { testClosing = true; Close(); }
    private void SaveSettings() { if (!testing) settings.Save(); }
    private void ApplyAppearance()
    {
        appearanceFont = settings.CreateFont();
        appearanceFonts.Add(appearanceFont);
        Theme.Apply(this, settings, appearanceFont);
        var titleFont = new Font(appearanceFont.FontFamily, appearanceFont.Size + 4, FontStyle.Bold);
        appearanceFonts.Add(titleFont);
        heading.Font = titleFont;
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            searchDelay.Dispose();
            foreach (Font font in appearanceFonts) font.Dispose();
        }
    }
    private void SmokeInitialCharges(string output)
    {
        foreach (var card in project!.Cards.Where(c => project.InitialCharges(c.Id).Editable).ToArray())
        {
            group.SelectedIndex = card.Type == 1 ? (int)CardGroup.Leaders : (int)CardGroup.Cards;
            search.Text = ""; searchDelay.Stop();
            factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, card.Faction);
            search.Text = card.Id.ToString(); searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 2;
            var old = project.InitialCharges(card.Id);
            if (!numberInputs.TryGetValue(-1, out var input) || !input.Enabled || input.Value != old.Value
                || numberInputs.ContainsKey(old.TextVariableId!.Value) || Draft().Numbers?.Count > 0)
                throw new InvalidOperationException("Initial charge GUI needs one synchronized field.");
            int next = old.Value < 9 ? old.Value + 1 : old.Value - 1;
            input.Value = next;
            var preview = project.Preview(card.Id, Draft());
            if (preview.Count != 2 || VisibleNumberChanges(preview).Count != 1)
                throw new InvalidOperationException("Initial charge confirmation must combine template and text alias.");
            CommitDraft(false);
            if (project.InitialCharges(card.Id) is not { Editable: true } setting || setting.Value != next
                || project.Changes().Count != 2 || VisibleNumberChanges(project.Changes()).Count != 1)
                throw new InvalidOperationException("Initial charge GUI apply failed.");
            project.Undo(); RefreshList(card.Id);
            if (project.HasChanges || draftDirty || numberInputs[-1].Value != old.Value)
                throw new InvalidOperationException("Initial charge GUI undo failed.");
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, "initial-charges-" + card.Id + ".png"));
        }
        group.SelectedIndex = 0;
    }
    public void SmokeStatusesOnly(string output)
    {
        Directory.CreateDirectory(output);
        language.SelectedIndex = 0;
        SmokeInitialStatuses(output, [112103,112214,112215,132202,132215,132216,132307,142108,162207,
            200037,200539,201701,201774,202098,202105,202784,202815,203108,
            152210,152302,200139,202222,202230,202233,202245,202272,202307,202309,202316,202318,202344,202429,202547,202557,202625,
            132206,132208,132210,132213,132404,152213,202238,202383,203051,203057,203270]);
        const int exportCard = 132206;
        bool originalShield = project!.InitialStatuses(exportCard).Single(s => s.Key == "shield").Value;
        project.Apply(exportCard, project.CurrentEdit(exportCard) with
            { Statuses = new Dictionary<string, bool> { ["shield"] = !originalShield } });
        string package = Path.Combine(output, "status-export-" + exportCard);
        project.Export(package);
        var roundTrip = ClientSource.Open(package, Path.Combine(EditorSettings.Home, "VerificationProjects"));
        if (roundTrip.InitialStatuses(exportCard).Single(s => s.Key == "shield").Value == originalShield)
            throw new InvalidOperationException("Starting status export round-trip failed.");
        project.Undo();
        if (project.HasChanges) throw new InvalidOperationException("Starting status export undo failed.");
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"),
            "{\"Passed\":true,\"Scope\":\"statuses-only\",\"Cards\":46,\"StatusExportRoundTrip\":true,\"ExportCard\":132206}");
    }

    private void SmokeInitialStatuses(string output, int[]? ids = null)
    {
        foreach (int id in ids ?? new[] { 200041, 202609, 202418, 122212, 113311, 112211, 112212, 142207, 142317, 202198, 202462,
            122314, 152314, 162307, 201616, 202224, 202335, 202432,
            122211, 200062, 201630, 201661, 202106, 202479, 202602,
            142309, 152317, 202799, 202407, 202044, 201753, 162313, 122204, 202340,
            142101, 142212, 200102, 200103, 202122, 202483,
            202296, 202370, 202379, 202386, 202559, 202849, 202306, 202311, 202426,
            162208, 200050, 202260, 202419, 112405, 200145, 203231, 203233 })
        {
            var card = project!.Card(id);
            search.Text = ""; searchDelay.Stop();
            factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, card.Faction);
            search.Text = id.ToString(); searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 0;
            var settings = project.InitialStatuses(id);
            var old = settings.ToDictionary(s => s.Key, s => s.Value);
            foreach (var (key, value) in old)
            {
                bool editable = settings.Single(s => s.Key == key).Editable;
                if (initialStatuses[key].Enabled != editable || initialStatuses[key].Checked != value)
                    throw new InvalidOperationException("Initial status control is not ready.");
                if (editable) initialStatuses[key].Checked = !value;
            }
            var preview = project.Preview(id, Draft());
            var tokenChange = preview.SingleOrDefault(change => change.Field == "Tokens");
            int expectedTextChanges = settings.Any(setting => setting.Editable ? !old[setting.Key] : setting.Value)
                ? project.AvailableLanguages.Count(language => project.HasText(id, language, "tooltip")) : 0;
            int actualTextChanges = preview.Count(change => change.Field.EndsWith("/tooltip", StringComparison.Ordinal));
            if (tokenChange == null || ChangeName(tokenChange.Field) != UiText.Get("InitialStatuses")
                || EditorProject.InitialStatusMasks.Keys.Any(key => !ChangeValue(tokenChange.Field, tokenChange.After).Contains(GameKeyword(key), StringComparison.Ordinal))
                || actualTextChanges != expectedTextChanges)
                throw new InvalidOperationException("Status confirmation must include named flags and their localized descriptions.");
            CommitDraft(false);
            if (project.Changes().Count != 1 + expectedTextChanges || project.InitialStatuses(id).Any(s => s.Value != (s.Editable ? !old[s.Key] : old[s.Key])))
                throw new InvalidOperationException("Initial status GUI apply failed.");
            parameterScroll.ScrollControlIntoView(initialStatuses["shield"].Parent!);
            if (initialStatuses.Values.Any(input => !input.Parent!.ClientRectangle.Contains(input.Bounds)))
                throw new InvalidOperationException("Initial status controls are clipped.");
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, "initial-status-" + id + ".png"));
            project.Undo(); RefreshList(id);
            if (project.HasChanges || draftDirty || initialStatuses.Any(p => p.Value.Checked != old[p.Key]))
                throw new InvalidOperationException("Initial status GUI undo failed.");
        }
    }
    private void SmokeChargeRefill(string output)
    {
        foreach (var card in project!.Cards.Where(c => project.ChargeRefill(c.Id) != null).ToArray())
        {
            var old = project.ChargeRefill(card.Id)!;
            int initial = project.InitialCharges(card.Id).Value;
            search.Text = ""; searchDelay.Stop();
            factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, card.Faction);
            search.Text = card.Id.ToString(); searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 2;
            if (!numberInputs.TryGetValue(-5, out var input) || !input.Enabled || input.Value != old.Value
                || numberInputs.ContainsKey(old.TextVariableId) || old.LiteralVariableId is int literal && numberInputs.ContainsKey(literal))
                throw new InvalidOperationException("Refill GUI needs one synchronized field.");
            input.Value = old.Value + 1;
            var preview = project.Preview(card.Id, Draft());
            if (preview.Count != (old.LiteralVariableId.HasValue ? 2 : 1) || VisibleNumberChanges(preview).Count != 1)
                throw new InvalidOperationException("Refill confirmation must combine source and description.");
            CommitDraft(false);
            if (project.ChargeRefill(card.Id)?.Value != old.Value + 1 || project.InitialCharges(card.Id).Value != initial
                || VisibleNumberChanges(project.Changes()).Count != 1)
                throw new InvalidOperationException("Refill GUI apply affected the initial reserve.");
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, "charge-refill-" + card.Id + ".png"));
            project.Undo(); RefreshList(card.Id);
            if (project.HasChanges || draftDirty || numberInputs[-5].Value != old.Value)
                throw new InvalidOperationException("Refill GUI undo failed.");
        }
    }
    private void SmokeTiming(string output)
    {
        foreach (int id in new[] { 112110, 112113, 200539, 202201, 142210, 122204, 112107, 201659 })
        {
            var card = project!.Card(id);
            var old = project.Timing(id).Single();
            search.Text = ""; searchDelay.Stop();
            factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, card.Faction);
            search.Text = id.ToString(); searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 2;
            if (!numberInputs.TryGetValue(old.Id, out var input) || !input.Enabled || input.Value != old.Value
                || numberInputs.ContainsKey(old.TextVariableId) || old.GraphValues.Any(numberInputs.ContainsKey))
                throw new InvalidOperationException("Timing GUI needs one synchronized input.");
            input.Value = old.Value + 1;
            var preview = project.Preview(id, Draft());
            int expected = old.Id == -2 ? 2 : old.GraphValues.Count + 1;
            if (preview.Count != expected || VisibleNumberChanges(preview).Count != 1)
                throw new InvalidOperationException("Timing confirmation must combine runtime and text values.");
            CommitDraft(false);
            if (project.Timing(id).Single().Value != old.Value + 1 || VisibleNumberChanges(project.Changes()).Count != 1)
                throw new InvalidOperationException("Timing GUI apply failed.");
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, "timing-" + id + ".png"));
            project.Undo(); RefreshList(id);
            if (project.HasChanges || draftDirty || numberInputs[old.Id].Value != old.Value)
                throw new InvalidOperationException("Timing GUI undo failed.");
        }
    }
    public void SmokeTest(string output, string? balanceFile = null)
    {
        if (project == null) throw new InvalidOperationException("Smoke test needs --project.");
        Directory.CreateDirectory(output);
        var counts = new List<int>();
        foreach (int faction in Enumerable.Range(0, 7))
        {
            factions.SelectedIndex = faction;
            Application.DoEvents();
            counts.Add(grid.Rows.Count);
            if (grid.Rows.Count == 0) throw new InvalidOperationException("Empty faction: " + faction);
        }
        factions.SelectedIndex = 0;
        search.Text = "112101";
        searchDelay.Stop();
        FilterChanged();
        if (grid.Rows.Count != 1 || selectedId != 112101) throw new InvalidOperationException("ID search failed.");
        foreach (int index in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = index;
            Application.DoEvents();
            if (language.SelectedItem == null || sort.SelectedItem == null
                || ((ComboBox)fields["Tier"]).SelectedItem is not Choice { Value: 8 }
                || ((ComboBox)fields["FactionId"]).SelectedItem is not Choice { Value: 1 })
                throw new InvalidOperationException("Dropdown selection lost after translation.");
            foreach (string key in UiText.Keys)
                if (string.IsNullOrWhiteSpace(UiText.Get(key))) throw new InvalidOperationException("Untranslated: " + key);
            foreach (Size size in new[] { new Size(1320, 930), new Size(1060, 740) })
            {
                Size = size;
                PerformLayout();
                Application.DoEvents();
                if (split.Panel2.Width < 450) throw new InvalidOperationException("Right panel clipped at " + size.Width);
                if (split.Right > ClientSize.Width || apply.RectangleToScreen(apply.ClientRectangle).Right > RectangleToScreen(ClientRectangle).Right)
                    throw new InvalidOperationException("Content exceeds window at " + size.Width);
                using var bitmap = new Bitmap(Width, Height);
                DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                bitmap.Save(Path.Combine(output, $"desktop-{UiText.Language}-{size.Width}.png"));
            }
            sections.SelectedIndex = 1;
            Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                bitmap.Save(Path.Combine(output, $"texts-{UiText.Language}.png"));
            }
            sections.SelectedIndex = 0;
        }
        language.SelectedIndex = 0;
        int oldPower = project.Card(112101).Power;
        ((NumericUpDown)fields["Power"]).Value = oldPower + 1;
        if (!apply.Enabled || !draftDirty) throw new InvalidOperationException("Draft event failed.");
        CommitDraft(false);
        if (project.Card(112101).Power != oldPower + 1 || !undo.Enabled || !export.Enabled)
            throw new InvalidOperationException("Apply controls failed.");
        project.Undo();
        RefreshList(112101);
        if (project.HasChanges || draftDirty) throw new InvalidOperationException("Undo failed.");
        factions.SelectedIndex = 2;
        search.Text = "122301"; searchDelay.Stop(); FilterChanged();
        sections.SelectedIndex = 2;
        if (selectedId != 122301 || !numberInputs.TryGetValue(501, out var damageInput) || !damageInput.Enabled)
            throw new InvalidOperationException("Verified damage input is missing.");
        if (numberValues.Any(n => !n.Editable) || numberInputs.ContainsKey(1108)
            || numberInputs.Count != project.Numbers(122301).Count(n => n.Editable))
            throw new InvalidOperationException("Read-only numeric values must be hidden.");
        if (numberTable.Controls.OfType<Label>().Any(l => l.Text.Contains('#'))
            || ChangeName("122301/Ability/#501").Contains('#'))
            throw new InvalidOperationException("Technical parameter IDs must not appear in the interface.");
        if (balanceNote.Text != UiText.Get("LocalBalanceNote") || string.IsNullOrWhiteSpace(balanceNote.Text))
            throw new InvalidOperationException("The local balance limitation must be explicit.");
        int oldDamage = (int)damageInput.Value;
        damageInput.Value = oldDamage + 1;
        CommitDraft(false);
        if (project.Numbers(122301).Single(n => n.Id == 501).Value != oldDamage + 1)
            throw new InvalidOperationException("Ability GUI apply failed.");
        project.Undo(); RefreshList(122301);
        if (project.HasChanges || draftDirty || apply.Enabled || export.Enabled) throw new InvalidOperationException("Ability GUI undo failed.");
        var targetGroup = project.NumberGroups(122301).First(g => g.Members.Count == 2);
        var targetInput = numberInputs[targetGroup.Members[0].Id];
        if (!ReferenceEquals(targetInput, numberInputs[targetGroup.Members[1].Id]))
            throw new InvalidOperationException("Target bounds need one shared input.");
        targetInput.Value = targetGroup.Display.DisplayValue + 1;
        var targetPreview = project.Preview(122301, Draft());
        if (targetPreview.Count != 2 || VisibleNumberChanges(targetPreview).Count != 1)
            throw new InvalidOperationException("Target count must update both bounds with one confirmation row.");
        CommitDraft(false);
        if (targetGroup.Members.Any(m => project.Numbers(122301).Single(v => v.Id == m.Id).Value != m.Value + 1))
            throw new InvalidOperationException("Target count did not update both bounds.");
        project.Undo(); RefreshList(122301);
        if (project.HasChanges || draftDirty) throw new InvalidOperationException("Target count undo failed.");
        foreach (int index in Enumerable.Range(0, EditorProject.Languages.Length))
        {
            language.SelectedIndex = index;
            Application.DoEvents();
            foreach (string keyword in new[] { "deploy", "order", "zeal", "intimidate", "adrenaline", "profit", "tribute", "fee", "bleeding", "vitality", "coin" })
            {
                string? official = project.GameTerm(keyword, UiText.Language);
                if (official != null && GameKeyword(keyword) != official)
                    throw new InvalidOperationException("Official game term mismatch: " + keyword + " / " + UiText.Language);
                if (official == null && UiText.Language != "en-us" && !GameKeyword(keyword).EndsWith(" (EN)"))
                    throw new InvalidOperationException("Missing game term needs explicit English fallback.");
            }
            if (UiText.Keys.Any(k => string.IsNullOrWhiteSpace(UiText.Get(k)))) throw new InvalidOperationException("Missing UI translation fallback.");
            if (index >= 3 && UiText.Complete) throw new InvalidOperationException("Partial translation incorrectly marked complete.");
            if (index < 3 || index == 4)
            {
                using var bitmap = new Bitmap(Width, Height);
                DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                bitmap.Save(Path.Combine(output, "ability-" + UiText.Language + ".png"));
            }
        }
        language.SelectedIndex = 0;
        foreach (var (cardId, role) in new[] { (152210, "bleeding-duration"), (142202, "vitality-duration"), (202340, "coin-gain"),
            (202427, "profit"), (202386, "profit"), (202310, "profit"), (202299, "base-profit"),
            (202427, "tribute-cost"), (202386, "fee-cost"), (202296, "fee-cost"),
            (112202, "adrenaline-threshold"), (200046, "berserk-threshold"), (152307, "berserk-threshold"), (152301, "bloodthirst"),
            (112208, "copy-count"), (122310, "copy-count"), (202312, "intimidate-boost"),
            (113207, "base-boost"), (202619, "base-damage"),
            (152204, "alternate-damage"), (202454, "alternate-boost"),
            (122209, "armor-gain"), (122209, "set-armor"), (132205, "strengthen"),
            (203074, "base-power-loss"), (203268, "set-base-power") })
        {
            search.Text = ""; searchDelay.Stop();
            factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, project.Card(cardId).Faction);
            search.Text = cardId.ToString(); searchDelay.Stop(); FilterChanged();
            sections.SelectedIndex = 2;
            var number = project.Numbers(cardId).First(n => n.Editable && n.Role == role);
            if (Draft().Numbers?.Count > 0) throw new InvalidOperationException("Rendering numeric values must not create draft changes.");
            if (selectedId != cardId || !numberInputs.TryGetValue(number.Id, out var input) || !input.Enabled)
                throw new InvalidOperationException("Semantic numeric input missing: " + role);
            if ((int)input.Value != number.DisplayValue || input.Minimum != number.ToDisplayValue(number.Minimum)
                || input.Maximum != number.ToDisplayValue(number.Maximum)) throw new InvalidOperationException("Incorrect displayed numeric range: " + role);
            if (role == "adrenaline-threshold" && (ChangeValue($"{cardId}/Ability/#{number.Id}", number.Value.ToString()) != number.DisplayValue.ToString()))
                throw new InvalidOperationException("Confirmation must show displayed adrenaline value.");
            int changedValue = number.Value < number.Maximum ? number.Value + 1 : number.Value - 1;
            input.Value = number.ToDisplayValue(changedValue);
            CommitDraft(false);
            if (project.Numbers(cardId).Single(n => n.Id == number.Id).Value != changedValue)
                throw new InvalidOperationException("Semantic GUI apply failed: " + role);
            project.Undo(); RefreshList(cardId);
            if (project.HasChanges || draftDirty) throw new InvalidOperationException("Semantic GUI undo failed: " + role);
            Application.DoEvents();
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, role + "-" + cardId + ".png"));
        }
        sections.SelectedIndex = 0;
        SmokeInitialCharges(output);
        SmokeChargeRefill(output);
        SmokeInitialStatuses(output);
        SmokeTiming(output);
        sections.SelectedIndex = 0;
        foreach (int cardId in new[] { 122301, 122212 })
        {
            search.Text = ""; searchDelay.Stop();
            factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, project.Card(cardId).Faction);
            search.Text = cardId.ToString(); searchDelay.Stop(); FilterChanged();
            bool oldZeal = project.Zeal(cardId).Value;
            if (!initialZeal.Enabled || initialZeal.Checked != oldZeal || string.IsNullOrWhiteSpace(zealNote.Text))
                throw new InvalidOperationException("Initial Zeal control is not ready.");
            initialZeal.Checked = !oldZeal;
            CommitDraft(false);
            if (project.Zeal(cardId).Value == oldZeal || project.Changes().Count != 1)
                throw new InvalidOperationException("Initial Zeal GUI apply failed.");
            project.Undo(); RefreshList(cardId);
            if (project.HasChanges || draftDirty || initialZeal.Checked != oldZeal)
                throw new InvalidOperationException("Initial Zeal GUI undo failed.");
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, "zeal-" + cardId + ".png"));
        }
        SmokeBalance(output, balanceFile);
        SmokeLeaders(output);
        SmokeDescriptionBindings(output);
        SmokeTokens(output);
        SmokeRelatedCards(output);
        factions.SelectedIndex = 0;
        search.Text = "";
        searchDelay.Stop();
        FilterChanged();
        Size = new Size(1320, 930);
        Application.DoEvents();
        using (var bitmap = new Bitmap(Width, Height))
        {
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, "full-list-ru.png"));
        }
        int powerColumn = grid.Columns["Power"]!.Index;
        SortColumn(powerColumn);
        var powers = grid.Rows.Cast<DataGridViewRow>().Select(r => project.Card((int)r.Tag!).Power).ToArray();
        if (!powers.SequenceEqual(powers.Order())) throw new InvalidOperationException("Header ascending sort failed.");
        SortColumn(powerColumn);
        powers = grid.Rows.Cast<DataGridViewRow>().Select(r => project.Card((int)r.Tag!).Power).ToArray();
        if (!powers.SequenceEqual(powers.OrderDescending())) throw new InvalidOperationException("Header descending sort failed.");
        foreach (string column in new[] { "Name", "Rarity", "Provision", "Id" })
        {
            SortColumn(grid.Columns[column]!.Index);
            if (descending) throw new InvalidOperationException("First header click must be ascending.");
            SortColumn(grid.Columns[column]!.Index);
            if (!descending) throw new InvalidOperationException("Second header click must be descending.");
        }
        group.SelectedIndex = 1;
        factions.SelectedIndex = 1;
        if (grid.Rows.Count == 0 || grid.Rows.Cast<DataGridViewRow>().Any(r => !CardCatalog.IsCatalogToken(project.Card((int)r.Tag!))))
            throw new InvalidOperationException("Token UI filter failed.");
        group.SelectedIndex = 0;
        factions.SelectedIndex = 0;
        settings.Background = Color.FromArgb(32, 34, 36).ToArgb();
        settings.Accent = Color.FromArgb(55, 180, 160).ToArgb();
        settings.FontFamily = "Arial";
        settings.FontSize = 12;
        ApplyAppearance();
        if (grid.DefaultCellStyle.ForeColor != Color.White || Math.Abs(Font.Size - 12) > .1)
            throw new InvalidOperationException("Appearance settings failed.");
        string settingsPath = Path.Combine(output, "settings-test.json");
        settings.Save(settingsPath);
        EditorSettings saved = EditorSettings.Load(settingsPath);
        if (saved.Background != settings.Background || saved.Accent != settings.Accent || saved.FontFamily != "Arial" || saved.FontSize != 12)
            throw new InvalidOperationException("Settings persistence failed.");
        Application.DoEvents();
        using (var bitmap = new Bitmap(Width, Height))
        {
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, "dark-font-12.png"));
        }
        using (var appearance = new AppearanceDialog(settings))
        {
            appearance.Show(this);
            Application.DoEvents();
            using var bitmap = new Bitmap(appearance.Width, appearance.Height);
            appearance.DrawToBitmap(bitmap, new Rectangle(Point.Empty, appearance.Size));
            bitmap.Save(Path.Combine(output, "appearance-dialog.png"));
            appearance.Close();
        }
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), System.Text.Json.JsonSerializer.Serialize(new
        { Passed = true, FactionCounts = counts, Languages = EditorProject.Languages, Search = true, Apply = true, Undo = true,
            HeaderSortBothDirections = true, TokenCategory = true, TokenLocalStatsWithCouncil = true, RelatedCardsSearchAndNavigation = true,
            LeaderCategoryAndLocalProvision = true, LeaderCombinedApplyUndo = true,
            Appearance = true, Font = true, SettingsPersistence = true,
            NumericAbilityApplyUndo = true, InitialZealApplyUndo = true, OfficialGameTerms = true,
            TechnicalParameterIdsHidden = true, LocalBalanceWarning = true,
            CombinedTargetCount = true,
            InitialChargesApplyUndoAndTextSync = true,
            ChargeRefillApplyUndoAndTextSync = true,
            InitialStatusesApplyUndoAndNamedConfirmation = true,
            TimingApplyUndoAndTextSync = true,
            CouncilDefaultOn = true, CouncilApplyUndo = true, CouncilMissingDataBlocked = true, CouncilPreferencePerSource = true,
            OfficialSnapshotFile = balanceFile,
            ReadOnlyNumbersHidden = true, AllLanguageFallbacks = true, FullInterfaceLanguages = new[] { "ru-ru", "en-us", "zh-cn" } }));
    }
}
