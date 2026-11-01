using System.Globalization;

namespace GwentCardEditor;

internal sealed class AudioRangeDialog : Form
{
    private readonly NumericUpDown begin = new() { DecimalPlaces = 3, Increment = 1, Width = 130 };
    private readonly NumericUpDown end = new() { DecimalPlaces = 3, Increment = 1, Width = 130 };
    private readonly Button accept = new() { AutoSize = true, DialogResult = DialogResult.OK };
    internal string StartSeconds => begin.Value.ToString(CultureInfo.InvariantCulture);
    internal string EndSeconds => end.Value.ToString(CultureInfo.InvariantCulture);

    internal AudioRangeDialog(double seconds)
    {
        Text = UiText.Get("CustomAudioSelectRange"); Size = new Size(500, 300); MinimumSize = new Size(460, 280);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        begin.Maximum = end.Maximum = (decimal)seconds; end.Value = Math.Min(120, end.Maximum);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var duration = new Label { AutoSize = true, Text = UiText.Get("CustomAudioDuration") + " " + seconds.ToString("0.000", CultureInfo.CurrentCulture) };
        layout.Controls.Add(duration, 0, 0); layout.SetColumnSpan(duration, 2);
        layout.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = UiText.Get("CustomAudioStart") }, 0, 1); layout.Controls.Add(begin, 1, 1);
        layout.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = UiText.Get("CustomAudioEnd") }, 0, 2); layout.Controls.Add(end, 1, 2);
        var note = new Label { AutoSize = true, Text = UiText.Get("CustomAudioRangeLimit") };
        layout.Controls.Add(note, 0, 3); layout.SetColumnSpan(note, 2);
        layout.SizeChanged += (_, _) => note.MaximumSize = new Size(Math.Max(200, layout.ClientSize.Width - 35), 0);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        accept.Text = UiText.Get("CustomAudioExtract");
        var cancel = new Button { AutoSize = true, Text = UiText.Get("ArtClose"), DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([accept, cancel]); layout.Controls.Add(actions, 0, 4); layout.SetColumnSpan(actions, 2);
        Controls.Add(layout); AcceptButton = accept; CancelButton = cancel;
        begin.ValueChanged += (_, _) => UpdateActions(); end.ValueChanged += (_, _) => UpdateActions(); UpdateActions();
    }

    private void UpdateActions() => accept.Enabled = begin.Value < end.Value && end.Value - begin.Value <= 120;

    internal void CheckControlsForTest()
    {
        if (!accept.Enabled) throw new Exception("Initial audio range disabled");
        begin.Value = 10; end.Value = 5;
        if (accept.Enabled) throw new Exception("Reversed range allowed");
        begin.Value = 0; end.Value = 130;
        if (accept.Enabled) throw new Exception("Overlong range allowed");
        begin.Value = 10; end.Value = 20;
        if (!accept.Enabled || StartSeconds != "10" || EndSeconds != "20") throw new Exception("Audio range conversion failed");
    }
}
