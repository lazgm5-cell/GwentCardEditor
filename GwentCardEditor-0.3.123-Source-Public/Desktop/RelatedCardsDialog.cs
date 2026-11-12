using System.Globalization;

namespace GwentCardEditor;

internal sealed class RelatedCardsDialog : Form
{
    internal readonly TextBox Search = new() { Dock = DockStyle.Fill };
    internal readonly DataGridView Cards = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
    };
    internal readonly Button OpenCard = new() { AutoSize = true };
    public int? SelectedCardId { get; private set; }

    public RelatedCardsDialog(EditorProject project, int id, EditorSettings settings, Font? font)
    {
        Text = UiText.Get("RelatedCards") + ": " + project.Name(id, UiText.Language);
        Size = new Size(820, 520); MinimumSize = new Size(620, 380);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        Search.PlaceholderText = UiText.Get("Search");
        OpenCard.Text = UiText.Get("OpenCard");
        var close = new Button { AutoSize = true, Text = UiText.Get("Cancel"), DialogResult = DialogResult.Cancel };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Cards.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", FillWeight = 15 });
        Cards.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = UiText.Get("Name"), FillWeight = 45 });
        Cards.Columns.Add(new DataGridViewTextBoxColumn { Name = "Relation", HeaderText = UiText.Get("Relation"), FillWeight = 40 });
        Cards.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        var references = project.RelatedCards(id);
        void Filter()
        {
            Cards.Rows.Clear();
            string query = Search.Text.Trim();
            var culture = CultureInfo.GetCultureInfo(UiText.Language);
            foreach (var reference in references.OrderBy(r => CardCatalog.NameSortKey(project.Name(r.CardId, UiText.Language)), StringComparer.Create(culture, true)))
            {
                string name = project.Name(reference.CardId, UiText.Language);
                if (query.Length > 0 && !reference.CardId.ToString().Contains(query, StringComparison.Ordinal)
                    && culture.CompareInfo.IndexOf(name, query, CompareOptions.IgnoreCase) < 0) continue;
                int row = Cards.Rows.Add(reference.CardId, name, UiText.Get(reference.ReferencedBy ? "ReferencedBy" : "ReferencesCard"));
                Cards.Rows[row].Tag = reference.CardId;
            }
            OpenCard.Enabled = Cards.Rows.Count > 0;
        }
        void Choose()
        {
            if (Cards.CurrentRow?.Tag is not int target) return;
            SelectedCardId = target; DialogResult = DialogResult.OK; Close();
        }
        Search.TextChanged += (_, _) => Filter();
        OpenCard.Click += (_, _) => Choose();
        Cards.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Choose(); };
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([close, OpenCard]);
        layout.Controls.Add(Search, 0, 0); layout.Controls.Add(Cards, 0, 1);
        layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, Text = UiText.Get("ReferenceNote"), Margin = new Padding(0, 8, 0, 8) }, 0, 2);
        layout.Controls.Add(buttons, 0, 3); Controls.Add(layout);
        AcceptButton = OpenCard; CancelButton = close;
        Filter(); Theme.Apply(this, settings, font ?? Font);
    }
}
