namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeInsanity(string output)
    {
        Directory.CreateDirectory(output);
        foreach(int locale in Enumerable.Range(0,3))
        {
            language.SelectedIndex=locale;NavigateToCard(203209);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalInsanity is not {Checked:true,Enabled:false})throw new Exception("Native Insanity must be read-only");
            foreach(int id in new[]{202309,202337,202341})
            {
                NavigateToCard(id);sections.SelectedIndex=2;Application.DoEvents();
                if(additionalInsanity is not {Checked:false,Enabled:true})throw new Exception("Insanity addition unavailable");
                var fee=numberValues.First(n=>n.Editable && n.Role=="fee-cost");decimal before=numberInputs[fee.Id].Value;
                additionalInsanity.Checked=true;numberInputs[fee.Id].Value=before==9?8:before+1;
                project!.Apply(id,Draft());draftDirty=false;ShowCard(id);Application.DoEvents();
                if(additionalInsanity is not {Checked:true,Enabled:true} || !numberInputs.ContainsKey(fee.Id))throw new Exception("Applied Insanity or Fee missing");
                using(var capture=new Bitmap(Width,Height)){DrawToBitmap(capture,new Rectangle(Point.Empty,Size));capture.Save(Path.Combine(output,id+"-"+UiText.Language+".png"));}
                additionalInsanity.Checked=false;numberInputs[fee.Id].Value=before;
                project.Apply(id,Draft());draftDirty=false;ShowCard(id);Application.DoEvents();
                if(project.HasChanges)throw new Exception("Insanity roundtrip leaves changes");
            }
        }
        File.WriteAllText(Path.Combine(output,"ui-smoke.json"),"{\"Passed\":true,\"Languages\":3,\"Cards\":3,\"NativeReadOnly\":true,\"FeeEditing\":true,\"ClientModified\":false}");
    }
}
