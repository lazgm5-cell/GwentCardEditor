namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeBanArdDescription(string output)
    {
        Directory.CreateDirectory(output);
        const int id = 200048;
        foreach (int locale in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = locale; NavigateToCard(id); sections.SelectedIndex = 2; Application.DoEvents();
            bool needed = project!.HasDescriptionRepair(id);
            bool button = numberTable.Controls.OfType<Button>().Any(b => (string?)b.Tag == "RepairDescription");
            if (needed != button) throw new Exception("Description repair action state incorrect");
            if (!ReferenceEquals(numberInputs[110], numberInputs[182]) || numberInputs[110].Value != 2) throw new Exception("Shared boost input must display actual effect");
            using (var capture = new Bitmap(Width, Height))
            {
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size)); capture.Save(Path.Combine(output, $"repair-{UiText.Language}.png"));
            }
            if (needed) { project.Apply(id, Draft()); draftDirty = false; ShowCard(id); }
            if (project.HasDescriptionRepair(id) || numberTable.Controls.OfType<Button>().Any(b => (string?)b.Tag == "RepairDescription")) throw new Exception("Repair did not clear");
            numberInputs[110].Value = 3;
            project.Apply(id, Draft()); draftDirty = false; ShowCard(id);
            if (numberInputs[110].Value != 3 || project.HasDescriptionRepair(id)) throw new Exception("New boost is not synchronized");
            project.Undo(); if (needed) project.Undo(); draftDirty = false; ShowCard(id);
            if (project.HasChanges || project.HasDescriptionRepair(id) != needed) throw new Exception("Repair UI test changed source state");
        }
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Languages\":3,\"GameplayBoostPreserved\":true,\"ClientModified\":false}");
    }
}
