namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeCardClarityRevision(string output)
    {
        Directory.CreateDirectory(output);
        int[] cards=[202383,142108,202277,202981,203095,202620,202618,200104,203240,202515,201709,201710];
        foreach(int locale in Enumerable.Range(0,3))
        {
            language.SelectedIndex=locale;
            foreach(int id in cards)
            {
                NavigateToCard(id);sections.SelectedIndex=2;Application.DoEvents();
                if(id==202383 && !numberInputs.ContainsKey(-12))throw new Exception("Gord cap missing");
                if(id is 142108 or 200104 && numberValues.Any(n=>n.Editable && n.Role=="damage"))throw new Exception("Technical damage visible");
                if(id==202277 && numberInputs.ContainsKey(563) || id==202981 && numberInputs.ContainsKey(333) || id==203240 && numberInputs.ContainsKey(163))throw new Exception("Service field visible");
                if(id is 202620 or 202618 or 203095)
                {
                    int own=id==202620?324:id==202618?515:51;
                    if(nativeVeteranNumber!=own || nativeVeteranInput==null || nativeVeteranInput.Parent!=additionalVeteran?.Parent
                        || !ReferenceEquals(numberInputs[own],nativeVeteranInput))throw new Exception("Veteran number not combined with checkbox");
                    if(id==203095 && (!numberInputs.ContainsKey(145) || !numberTable.Controls.OfType<Label>().Any(l=>l.Text.Contains(UiText.Get("BranStartVeteran")))))throw new Exception("Bran game-start effect unclear");
                    int before=(int)nativeVeteranInput.Value;nativeVeteranInput.Value=before+1;
                    project!.Apply(id,Draft());draftDirty=false;ShowCard(id);Application.DoEvents();
                    if(nativeVeteranInput?.Value!=before+1)throw new Exception("Inline Veteran update failed");
                    nativeVeteranInput.Value=before;project.Apply(id,Draft());draftDirty=false;ShowCard(id);Application.DoEvents();
                    if(project.HasChanges)throw new Exception("Inline Veteran reset leaves changes");
                }
                if(id==202515 && numberInputs.Count!=0)throw new Exception("Giga parent controls duplicated");
                if(id is 201709 or 201710 && !numberInputs.ContainsKey(1))throw new Exception("Giga choice input missing");
                var empty=numberTable.Controls.OfType<Label>().SingleOrDefault(l=>l.Text==UiText.Get("NoAbilityNumbers"));
                var modules=numberTable.Controls.OfType<FlowLayoutPanel>().SingleOrDefault();
                if(empty!=null && modules!=null && empty.Top<modules.Bottom)throw new Exception("Empty label overlaps ability controls");
                using var capture=new Bitmap(Width,Height);DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                capture.Save(Path.Combine(output,"clarity-"+id+"-"+UiText.Language+".png"));
            }
            NavigateToCard(201709);sections.SelectedIndex=2;Application.DoEvents();numberInputs[1].Value=3;
            var preview=project!.Preview(201709,Draft());
            if(preview.Select(c=>ChangeName(c.Field)).Any(s=>s.Contains('#')))throw new Exception("Giga preview exposes internal IDs");
            project.Apply(201709,Draft());draftDirty=false;ShowCard(201709);
            NavigateToCard(201710);numberInputs[1].Value=9;project.Apply(201710,Draft());draftDirty=false;ShowCard(201710);
            if(project.Numbers(201709).Single().Value!=3)throw new Exception("Giga choices coupled");
            NavigateToCard(201709);numberInputs[1].Value=1;project.Apply(201709,Draft());draftDirty=false;ShowCard(201709);
            NavigateToCard(201710);numberInputs[1].Value=6;project.Apply(201710,Draft());draftDirty=false;ShowCard(201710);
            if(project.HasChanges || draftDirty)throw new Exception("Giga UI reset leaves changes");
        }
        File.WriteAllText(Path.Combine(output,"ui-smoke.json"),"{\"Passed\":true,\"Languages\":3,\"Cards\":12,\"InlineVeteran\":true,\"IndependentGigaChoices\":true,\"ClientModified\":false}");
    }
}
