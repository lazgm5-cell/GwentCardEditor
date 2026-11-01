using System.Text.Json;

namespace GwentCardEditor;

internal sealed class CardbackPicker : Form
{
    private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = false, HideSelection = false };
    private readonly ImageList previews = new() { ImageSize = new Size(80, 120), ColorDepth = ColorDepth.Depth32Bit };
    internal int? SelectedId => list.SelectedItems.Count == 1 ? (int)list.SelectedItems[0].Tag! : null;
    internal string SelectedName => list.SelectedItems.Count == 1 ? list.SelectedItems[0].Text : "";
    internal int PreviewCount => previews.Images.Count;

    internal CardbackPicker(JsonElement catalog)
    {
        Text = UiText.Get("CardbackChoose"); Size = new Size(700, 440); MinimumSize = new Size(430, 360);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        list.LargeImageList = previews;
        int index = 0;
        foreach (var row in catalog.GetProperty("Cardbacks").EnumerateArray())
        {
            using var stream = new MemoryStream(Convert.FromBase64String(row.GetProperty("Preview").GetString()!));
            using var image = Image.FromStream(stream);
            previews.Images.Add(new Bitmap(image));
            list.Items.Add(new ListViewItem(string.Format(UiText.Get("CardbackDefault"), ++index), index - 1) { Tag = row.GetProperty("Id").GetInt32() });
        }
        var ok = new Button { Text = UiText.Get("ArtChooseImage"), AutoSize = true, Enabled = false, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = UiText.Get("ArtClose"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange([cancel, ok]); Controls.Add(list); Controls.Add(actions);
        AcceptButton = ok; CancelButton = cancel;
        list.SelectedIndexChanged += (_, _) => ok.Enabled = SelectedId.HasValue;
        list.DoubleClick += (_, _) => { if (SelectedId.HasValue) { DialogResult = DialogResult.OK; Close(); } };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) previews.Dispose();
        base.Dispose(disposing);
    }
}
