namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeFlanking(string output)
    {
        Directory.CreateDirectory(output);
        foreach(int locale in Enumerable.Range(0,3))
        {
            language.SelectedIndex=locale;
            NavigateToCard(112103);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalFlanking is not {Enabled:false,Checked:false})throw new Exception("No end-turn must block Flanking");
            NavigateToCard(202244);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalFlanking is not {Enabled:false,Checked:true})throw new Exception("Native Flanking must stay read-only");
            foreach(int id in new[]{200299,202609,202981})
            {
                NavigateToCard(id);sections.SelectedIndex=2;Application.DoEvents();
                if(additionalFlanking is not {Enabled:true,Checked:false,Visible:true})throw new Exception("Flanking control missing");
                var before=numberInputs.Keys.Order().ToArray();
                var number=numberValues.First(n=>n.Editable && n.Value<n.Maximum);decimal initial=numberInputs[number.Id].Value;
                additionalFlanking.Checked=true;numberInputs[number.Id].Value=initial+1;
                var draft=Draft();if(draft.Flanking!=true)throw new Exception("Flanking missing from draft");
                project!.Apply(id,draft);draftDirty=false;ShowCard(id);Application.DoEvents();
                if(additionalFlanking is not {Enabled:true,Checked:true} || !numberInputs.Keys.Order().SequenceEqual(before)
                    || numberInputs[number.Id].Value!=initial+1)throw new Exception("Flanking changed original settings");
                var choices=additionalFlanking.Parent!;
                if(choices.Controls.Cast<Control>().Any(c=>c.Visible && (c.Right>choices.ClientSize.Width || c.Bottom>choices.ClientSize.Height)))throw new Exception("Module controls clipped");
                using(var capture=new Bitmap(Width,Height))
                {DrawToBitmap(capture,new Rectangle(Point.Empty,Size));capture.Save(Path.Combine(output,id+"-"+UiText.Language+".png"));}
                additionalFlanking.Checked=false;numberInputs[number.Id].Value=initial;
                project.Apply(id,Draft());draftDirty=false;ShowCard(id);Application.DoEvents();
                if(project.HasChanges)throw new Exception("Flanking UI roundtrip leaves changes");
            }
        }
        File.WriteAllText(Path.Combine(output,"ui-smoke.json"),"{\"Passed\":true,\"Languages\":3,\"EditableCards\":3,\"NativeAndIncompatible\":true,\"OriginalNumbers\":true,\"ClientModified\":false}");
    }
}
