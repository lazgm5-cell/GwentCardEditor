namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeSyndicate(string output)
    {
        Directory.CreateDirectory(output);
        foreach(int locale in Enumerable.Range(0,3))
        {
            language.SelectedIndex=locale;
            foreach(int id in new[]{202294,202374,202349})
            {
                NavigateToCard(id);sections.SelectedIndex=2;Application.DoEvents();
                if(additionalProfit is not {Enabled:true,Checked:false} || !additionalValueInputs.TryGetValue("profit",out var amount) || amount.Maximum!=9)
                    throw new Exception("Profit addition control missing "+id);
                additionalProfit.Checked=true;amount.Value=4;
                project!.Apply(id,Draft());draftDirty=false;ShowCard(id);Application.DoEvents();
                if(additionalProfit is not {Enabled:true,Checked:true} || additionalValueInputs["profit"].Value!=4)throw new Exception("Profit control update failed");
                var choices=additionalProfit.Parent!.Parent!;
                if(choices.Controls.Cast<Control>().Any(c=>c.Visible && (c.Right>choices.ClientSize.Width || c.Bottom>choices.ClientSize.Height)))throw new Exception("Module controls clipped");
                using(var capture=new Bitmap(Width,Height))
                {DrawToBitmap(capture,new Rectangle(Point.Empty,Size));capture.Save(Path.Combine(output,"profit-"+id+"-"+UiText.Language+".png"));}
                additionalProfit.Checked=false;project.Apply(id,Draft());draftDirty=false;ShowCard(id);
                if(project.HasChanges)throw new Exception("Profit UI roundtrip leaves changes");
            }
            NavigateToCard(202310);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalProfit is not {Enabled:false,Checked:true} || additionalValueInputs.ContainsKey("profit") || !numberValues.Any(n=>n.Role=="profit"))throw new Exception("Native Profit editing changed");
            NavigateToCard(202294);sections.SelectedIndex=0;Application.DoEvents();
            string[] gangs=["firesworn","crownsplitters","cutups","tidecloaks","blindeyes","salamandra"];
            foreach(string name in gangs)
            {
                int slot=categories.Items.Cast<CategoryChoice>().Select((c,i)=>(c,i)).Single(p=>p.c.Name==name).i;
                if(((CategoryChoice)categories.Items[slot]).Label!=project!.CategoryTerm(name,UiText.Language))throw new Exception("Gang official label missing");
                categories.SetItemChecked(slot,true);
            }
            project!.Apply(202294,Draft());draftDirty=false;ShowCard(202294);Application.DoEvents();
            if(!gangs.All(g=>project.Categories(202294).Contains(g)))throw new Exception("Gang selection failed");
            using(var capture=new Bitmap(Width,Height))
            {DrawToBitmap(capture,new Rectangle(Point.Empty,Size));capture.Save(Path.Combine(output,"gangs-"+UiText.Language+".png"));}
            project.Undo();draftDirty=false;ShowCard(202294);Application.DoEvents();
            if(project.HasChanges)throw new Exception("Gang UI undo leaves changes");
        }
        File.WriteAllText(Path.Combine(output,"ui-smoke.json"),"{\"Passed\":true,\"Languages\":3,\"ProfitCardTypes\":3,\"Gangs\":6,\"NativeProfitPreserved\":true,\"ClientModified\":false}");
    }
}
