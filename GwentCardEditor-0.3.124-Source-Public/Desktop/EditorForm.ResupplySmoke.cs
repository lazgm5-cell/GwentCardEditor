namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeResupply(string output)
    {
        Directory.CreateDirectory(output);
        foreach(int locale in Enumerable.Range(0,3))
        {
            language.SelectedIndex=locale;
            NavigateToCard(112103);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalResupply is not {Checked:false,Enabled:false})throw new Exception("No cooldown must block Resupply");
            NavigateToCard(200049);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalResupply is not {Checked:true,Enabled:false})throw new Exception("Native Resupply must stay read-only");
            foreach(int id in new[]{112113,142210,202639})
            {
                NavigateToCard(id);sections.SelectedIndex=2;Application.DoEvents();
                if(additionalResupply is not {Checked:false,Enabled:true} || !numberInputs.ContainsKey(-3))throw new Exception("Resupply or cooldown control missing");
                int before=(int)numberInputs[-3].Value;
                additionalResupply.Checked=true;numberInputs[-3].Value=before+1;
                var edit=Draft();
                if(edit.Resupply!=true || !project!.Preview(id,edit).Any(c=>c.Field=="AdditionalResupply"))throw new Exception("Resupply missing from draft");
                project.Apply(id,edit);draftDirty=false;ShowCard(id);Application.DoEvents();
                if(additionalResupply is not {Checked:true,Enabled:true} || numberInputs[-3].Value!=before+1)throw new Exception("Applied Resupply or cooldown missing");
                var choices=additionalResupply.Parent!;
                if(choices.Controls.Cast<Control>().Any(c=>c.Visible && (c.Right>choices.ClientSize.Width || c.Bottom>choices.ClientSize.Height)))throw new Exception("Module controls clipped");
                using(var capture=new Bitmap(Width,Height))
                {DrawToBitmap(capture,new Rectangle(Point.Empty,Size));capture.Save(Path.Combine(output,id+"-"+UiText.Language+".png"));}
                additionalResupply.Checked=false;numberInputs[-3].Value=before;
                project.Apply(id,Draft());draftDirty=false;ShowCard(id);Application.DoEvents();
                if(project.HasChanges)throw new Exception("Resupply add/remove leaves changes");
            }
        }
        File.WriteAllText(Path.Combine(output,"ui-smoke.json"),"{\"Passed\":true,\"Languages\":3,\"EditableCards\":3,\"NativeAndIncompatible\":true,\"CooldownEditing\":true,\"ClientModified\":false}");
    }
}
