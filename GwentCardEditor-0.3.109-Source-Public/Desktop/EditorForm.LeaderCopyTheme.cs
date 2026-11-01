namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeLeaderCopyTheme(string output)
    {
        Directory.CreateDirectory(output);int background=settings.Background;
        void Readable(CheckBox box,string name)
        {
            if(box is not EditorCheckBox || box.Enabled)throw new Exception("Disabled custom checkbox missing: "+name);
            if(Theme.Contrast(box.BackColor,Theme.DisabledText(box.BackColor))<4.5)throw new Exception("Disabled text contrast too low");
            using var bitmap=new Bitmap(box.Width,box.Height);box.DrawToBitmap(bitmap,box.ClientRectangle);
            int start=(int)Math.Round(13*box.DeviceDpi/96d)+5,pixels=0;
            for(int y=0;y<bitmap.Height;y++)for(int x=start;x<bitmap.Width;x++)
                if(Theme.Contrast(box.BackColor,bitmap.GetPixel(x,y))>=3)pixels++;
            if(pixels<8)throw new Exception("Disabled caption not rendered: "+name);
        }
        try
        {
            foreach(var color in new[]{Color.Black,Color.White,Color.FromArgb(149,149,149),Color.Red,Color.FromArgb(20,40,60)})
                if(Theme.Contrast(color,Theme.Text(color))<4.5 || Theme.Contrast(color,Theme.DisabledText(color))<4.5)throw new Exception("Theme palette contrast failed");
            foreach(int locale in Enumerable.Range(0,3))
            {
                language.SelectedIndex=locale;settings.Background=Color.Black.ToArgb();ApplyAppearance();
                foreach(int id in new[]{202880,202236,162313,203040})
                {
                    NavigateToCard(id);sections.SelectedIndex=2;Application.DoEvents();
                    if(id is 202880 or 202236 && numberValues.Any(n=>n.Editable && n.Role is "spawn-count" or "boost"))throw new Exception("Leader technical parameter visible");
                    if(id==162313 && (!numberInputs.ContainsKey(293) || numberInputs.ContainsKey(345)))throw new Exception("Engineer fields incorrect");
                    if(id==203040)
                    {
                        string labels=string.Join("\n",numberTable.Controls.OfType<Label>().Select(l=>l.Text));
                        foreach(string role in new[]{"copy-count-unbonded","copy-count-bonded"})if(!labels.Contains(AbilityLabel(role)))throw new Exception("Copy branch label missing");
                        int normal=(int)numberInputs[82].Value,bonded=(int)numberInputs[235].Value;
                        numberInputs[82].Value=2;numberInputs[235].Value=3;
                        project!.Apply(id,Draft());draftDirty=false;ShowCard(id);Application.DoEvents();
                        if(numberInputs[82].Value!=2 || numberInputs[235].Value!=3)throw new Exception("Copy branch update failed");
                        numberInputs[82].Value=normal;numberInputs[235].Value=bonded;
                        project.Apply(id,Draft());draftDirty=false;ShowCard(id);Application.DoEvents();
                        if(project.HasChanges)throw new Exception("Copy reset leaves changes");
                    }
                    using var capture=new Bitmap(Width,Height);DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                    capture.Save(Path.Combine(output,"black-"+id+"-"+UiText.Language+".png"));
                }
                NavigateToCard(202981);sections.SelectedIndex=2;Application.DoEvents();Readable(additionalFormation!,"Formation");
                using(var capture=new Bitmap(Width,Height))
                {
                    DrawToBitmap(capture,new Rectangle(Point.Empty,Size));capture.Save(Path.Combine(output,"melusine-black-"+UiText.Language+".png"));
                }
                NavigateToCard(132305);sections.SelectedIndex=2;Application.DoEvents();
                if(additionalThrive is not {Checked:true,Enabled:false})throw new Exception("Native checkbox state changed");Readable(additionalThrive,"Checked Thrive");
                foreach(var color in new[]{Color.White,Color.FromArgb(149,149,149)})
                {
                    settings.Background=color.ToArgb();ApplyAppearance();Application.DoEvents();Readable(additionalThrive!,"Other background");
                }
                group.SelectedIndex=(int)CardGroup.All;hidden.Checked=true;search.Text="";searchDelay.Stop();
                foreach(int tab in Enumerable.Range(0,7))
                {
                    factions.SelectedIndex=tab;FilterChanged();Application.DoEvents();
                    if(grid.Rows.Cast<DataGridViewRow>().Any(r=>r.DataBoundItem is CardEntry card && CardCatalog.IsService(card)))throw new Exception("Faction service record visible");
                }
            }
            if(project!.HasChanges || draftDirty)throw new Exception("Theme smoke modified project");
            File.WriteAllText(Path.Combine(output,"ui-smoke.json"),"{\"Passed\":true,\"Languages\":3,\"CardCases\":5,\"DarkDisabledPixels\":true,\"ContrastAtLeast4_5\":true,\"FactionServicesHidden\":true,\"ClientModified\":false}");
        }
        finally{settings.Background=background;ApplyAppearance();}
    }
}
