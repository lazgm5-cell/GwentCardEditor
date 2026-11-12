namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeReviewBatch(string output)
    {
        Directory.CreateDirectory(output);
        int[] ids=[202108,162204,202659,203092,202906,202295,202358,202841,203209];
        foreach(int locale in Enumerable.Range(0,3))
        {
            language.SelectedIndex=locale;
            foreach(int id in ids)
            {
                NavigateToCard(id);sections.SelectedIndex=2;Application.DoEvents();
                foreach(var (card,field) in new[]{(202108,83),(202108,84),(162204,21),(202358,21),(202358,22),
                    (202358,209),(202358,210),(202659,558),(202659,648),(203092,180),(202841,853)})
                    if(card==id && numberInputs.ContainsKey(field))throw new Exception("Service input visible "+id+"/"+field);
                string labels=string.Join("\n",numberTable.Controls.OfType<Label>().Select(l=>l.Text));
                string[] roles=id switch
                {
                    202906=>["first-banish-choices","extra-bronze-banish-choices"],
                    202295=>["tribute-targets","without-tribute-targets"],
                    203209=>["fee-single-damage","berserk-all-damage"],
                    _=>[]
                };
                foreach(string role in roles)
                    if(!labels.Contains(AbilityLabel(role)))throw new Exception("Branch caption missing "+role);
                if(id is 202906 or 202295 or 203209 or 202841)
                {
                    using var capture=new Bitmap(Width,Height);DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                    capture.Save(Path.Combine(output,id+"-"+UiText.Language+".png"));
                }
            }
        }
        if(project!.HasChanges || draftDirty)throw new Exception("Smoke changed project");
        File.WriteAllText(Path.Combine(output,"ui-smoke.json"),"{\"Passed\":true,\"Languages\":3,\"Cards\":9,\"BranchLabels\":6,\"ClientModified\":false}");
    }
}
