using System.Security.Cryptography;

namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeExportContinuation(string output)
    {
        Directory.CreateDirectory(output);
        string fixture = Path.Combine(Path.GetFullPath(output), "client", "data_definitions");
        if (project?.Client == null || !Path.GetFullPath(project.Client.ArchivePath).Equals(fixture, StringComparison.OrdinalIgnoreCase)
            || balanceReference == null) throw new Exception("Continuation smoke requires its isolated fixture client");
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string originalHash = Hash(fixture);
        var raw = project.Card(112101);
        var cards = balanceReference.Cards.Values.Where(c => c.Type is 2 or 4 or 8 && c.Power >= 0 && c.Provision >= 0)
            .OrderBy(c => c.Id).Take(1900).Select(c => c.Id == raw.Id ? c with { Power = c.Power + 2, Provision = c.Provision - 1 } : c);
        balanceSnapshot = BalanceSnapshot.Create(cards, DateTimeOffset.UtcNow);
        balanceError = null; RebuildBalanceProjection(); balanceCouncil.Checked = true;
        project.ExportBalanceCouncil = true; project.ExportBalanceSnapshot = balanceSnapshot;
        foreach (int locale in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = locale; NavigateToCard(raw.Id); sections.SelectedIndex = 0; Application.DoEvents();
            ((NumericUpDown)fields["Power"]).Value = raw.Power + 3;
            CommitDraft(false);
            string first = Path.Combine(output, "first-" + UiText.Language); project.Export(first);
            File.Copy(Path.Combine(first, "data_definitions"), fixture, true);
            string appliedHash = Hash(fixture);
            ((NumericUpDown)fields["Provision"]).Value = raw.Provision;
            ((NumericUpDown)fields["Armor"]).Value = raw.Armor + 1;
            CommitDraft(false);
            string second = Path.Combine(output, "second-" + UiText.Language); project.Export(second);
            var reopened = ClientSource.Open(second, Path.Combine(output, "reopen-cache"));
            if (reopened.Card(raw.Id).Power != raw.Power + 1 || reopened.Card(raw.Id).Provision != raw.Provision + 1
                || reopened.Card(raw.Id).Armor != raw.Armor + 1 || Hash(fixture) != appliedHash)
                throw new Exception("Continuation lost edits, reapplied council or installed itself");
            if (((NumericUpDown)fields["Power"]).Value != raw.Power + 3 || ((NumericUpDown)fields["Provision"]).Value != raw.Provision)
                throw new Exception("Council UI coordinate system changed during continuation");
            using (var capture = new Bitmap(Width, Height))
            {
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, "continuation-" + UiText.Language + ".png"));
            }
            File.Copy(Path.Combine(first, "_editor", "backup", "data_definitions"), fixture, true);
            project.Reset(); draftDirty = false; RefreshList(raw.Id);
            if (project.HasChanges || Hash(fixture) != originalHash) throw new Exception("Continuation smoke did not reset its fixture");
        }
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Languages\":3,\"OwnPackageRecognized\":true,\"CumulativeEditsPreserved\":true,\"CouncilNotAppliedTwice\":true,\"RealClientModified\":false}");
    }
}
