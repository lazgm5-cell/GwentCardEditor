namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeEcho(string output)
    {
        Directory.CreateDirectory(output);
        foreach(int locale in Enumerable.Range(0,3))
        {
            language.SelectedIndex=locale;
            NavigateToCard(112103);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalEcho is not {Enabled:false,Checked:false} || additionalEcho.Visible)throw new Exception("Unit Echo must not be offered");
            NavigateToCard(202607);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalEcho is not {Enabled:false,Checked:true,Visible:true})throw new Exception("Native Echo must stay read-only");
            foreach(int id in new[]{113301,113207,202310})
            {
                NavigateToCard(id);sections.SelectedIndex=2;Application.DoEvents();
                if(additionalEcho is not {Enabled:true,Checked:false,Visible:true})throw new Exception("Echo control missing");
                var before=numberInputs.Keys.Order().ToArray();
                var number=numberValues.First(n=>n.Editable && n.Value<n.Maximum);
                var input=numberInputs[number.Id];decimal initial=input.Value;
                additionalEcho.Checked=true;input.Value=initial+1;
                var draft=Draft();if(draft.Echo!=true)throw new Exception("Echo missing from draft");
                project!.Apply(id,draft);draftDirty=false;ShowCard(id);Application.DoEvents();
                if(additionalEcho is not {Enabled:true,Checked:true} || !numberInputs.Keys.Order().SequenceEqual(before)
                    || numberInputs[number.Id].Value!=initial+1)throw new Exception("Echo changed original settings");
                var choices=additionalEcho.Parent!;
                if(choices.Controls.Cast<Control>().Any(c=>c.Visible && (c.Right>choices.ClientSize.Width || c.Bottom>choices.ClientSize.Height)))throw new Exception("Module controls clipped");
                using(var capture=new Bitmap(Width,Height))
                {DrawToBitmap(capture,new Rectangle(Point.Empty,Size));capture.Save(Path.Combine(output,id+"-"+UiText.Language+".png"));}
                additionalEcho.Checked=false;numberInputs[number.Id].Value=initial;
                project.Apply(id,Draft());draftDirty=false;ShowCard(id);Application.DoEvents();
                if(project.HasChanges)throw new Exception("Echo UI roundtrip leaves changes");
            }
        }
        File.WriteAllText(Path.Combine(output,"ui-smoke.json"),"{\"Passed\":true,\"Languages\":3,\"EditableCards\":3,\"NativeAndUnitGuard\":true,\"ExistingNumbers\":true,\"ClientModified\":false}");
    }
}
