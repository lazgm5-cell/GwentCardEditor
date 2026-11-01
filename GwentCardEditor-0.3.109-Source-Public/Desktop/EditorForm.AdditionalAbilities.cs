namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    private CheckBox? additionalSymbiosis;
    private CheckBox? additionalAssimilate;
    private CheckBox? additionalHarmony;
    private CheckBox? additionalIntimidate;
    private CheckBox? additionalThrive;
    private CheckBox? additionalVeteran;
    private CheckBox? additionalFormation;
    private CheckBox? additionalProfit;
    private CheckBox? additionalResupply;
    private CheckBox? additionalEcho;
    private CheckBox? additionalFlanking;
    private CheckBox? additionalInsanity;
    private readonly Dictionary<string,NumericUpDown> additionalValueInputs=new();
    private int? nativeVeteranNumber;
    private NumericUpDown? nativeVeteranInput;
    private void RenderNativeVeteran(int id,FlowLayoutPanel choices)
    {
        nativeVeteranNumber=null;nativeVeteranInput=null;
        if(additionalVeteran is not {Checked:true,Enabled:false})return;
        var values=project!.Numbers(id).Where(n=>n.Editable && n.LabelRole=="veteran-strengthen").ToArray();
        if(values is not [var value])return;
        int slot=choices.Controls.GetChildIndex(additionalVeteran);choices.Controls.Remove(additionalVeteran);
        var row=new FlowLayoutPanel {AutoSize=true,WrapContents=false,Margin=new Padding(0,0,8,4)};
        var input=new NumericUpDown {Minimum=value.Minimum,Maximum=value.Maximum,Value=value.Value,Width=72,
            AccessibleName=AbilityLabel("veteran-strengthen"),Margin=Padding.Empty};
        input.ValueChanged+=(_,_)=>MarkDraft();
        var tip=new ToolTip();tip.SetToolTip(input,UiText.Get("AbilityHelp.VeteranStrengthen"));input.Disposed+=(_,_)=>tip.Dispose();
        row.Controls.Add(additionalVeteran);row.Controls.Add(input);choices.Controls.Add(row);choices.Controls.SetChildIndex(row,slot);
        numberInputs[value.Id]=input;nativeVeteranNumber=value.Id;nativeVeteranInput=input;
    }
    private void RenderAdditionalValues(int id,FlowLayoutPanel choices)
    {
        additionalValueInputs.Clear();
        foreach(var (keyword,checkbox) in new[]{("assimilate",additionalAssimilate),("harmony",additionalHarmony),("intimidate",additionalIntimidate),("thrive",additionalThrive),("profit",additionalProfit)})
        {
            if(checkbox is not {Enabled:true})continue;
            int slot=choices.Controls.GetChildIndex(checkbox);
            choices.Controls.Remove(checkbox);
            var row=new FlowLayoutPanel {AutoSize=true,WrapContents=false,Margin=new Padding(0,0,8,4)};
            var input=new NumericUpDown {Minimum=1,Maximum=keyword=="profit"?9:99,Width=56,Value=project!.AdditionalAbilityValue(id,keyword),
                Enabled=checkbox.Checked,AccessibleName=checkbox.Text,Margin=new Padding(0,0,0,0)};
            checkbox.Margin=new Padding(3,3,3,0);
            checkbox.CheckedChanged+=(_,_)=>input.Enabled=checkbox.Checked;
            input.ValueChanged+=(_,_)=>MarkDraft();
            row.Controls.Add(checkbox);row.Controls.Add(input);
            choices.Controls.Add(row);choices.Controls.SetChildIndex(row,slot);
            additionalValueInputs[keyword]=input;
        }
    }

    public void SmokeAdditionalAbilities(string output)
    {
        Directory.CreateDirectory(output);
        foreach(int locale in Enumerable.Range(0,3))
        {
            language.SelectedIndex=locale;
            NavigateToCard(202475);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalSymbiosis is not {Checked:true,Enabled:false}) throw new Exception("Native Symbiosis must be read-only");
            NavigateToCard(112103);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalSymbiosis is not {Checked:false,Enabled:true})throw new Exception("Symbiosis addition unavailable");
            additionalSymbiosis.Checked=true;
            var edit=Draft();
            if(edit.Symbiosis!=true || !draftDirty)throw new Exception("Keyword toggle missing from draft");
            var changes=project!.Preview(112103,edit);
            if(!changes.Any(c=>c.Field=="AdditionalSymbiosis") || changes.Count!=project.AvailableLanguages.Count+1)throw new Exception("Keyword localization missing in preview");
            project.Apply(112103,edit);draftDirty=false;ShowCard(112103);Application.DoEvents();
            if(additionalSymbiosis is not {Checked:true,Enabled:true})throw new Exception("Applied keyword not displayed");
            using(var capture=new Bitmap(Width,Height))
            {
                DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                capture.Save(Path.Combine(output,"symbiosis-"+UiText.Language+".png"));
            }
            additionalSymbiosis.Checked=false;
            project.Apply(112103,Draft());draftDirty=false;ShowCard(112103);
            if(project.HasChanges)throw new Exception("Keyword add/remove leaves changes");
            NavigateToCard(202246);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalAssimilate is not {Checked:true,Enabled:false})throw new Exception("Native Assimilate must be read-only");
            NavigateToCard(112103);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalAssimilate is not {Checked:false,Enabled:true})throw new Exception("Assimilate addition unavailable");
            var valueChoices=additionalAssimilate.Parent!.Parent!;
            if(valueChoices.Width>numberTable.ClientSize.Width || valueChoices.Controls.Cast<Control>().Any(c=>c.Right>valueChoices.ClientSize.Width || c.Bottom>valueChoices.ClientSize.Height))throw new Exception("Value controls clipped");
            using(var capture=new Bitmap(Width,Height))
            {
                DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                capture.Save(Path.Combine(output,"value-choices-"+UiText.Language+".png"));
            }
            additionalAssimilate.Checked=true;
            var assimilateDraft=Draft();
            if(assimilateDraft.Assimilate!=true || !draftDirty)throw new Exception("Assimilate missing from draft");
            project.Apply(112103,assimilateDraft);draftDirty=false;ShowCard(112103);Application.DoEvents();
            if(additionalAssimilate is not {Checked:true,Enabled:true} || additionalSymbiosis?.Enabled==true)throw new Exception("Added module compatibility incorrect");
            using(var capture=new Bitmap(Width,Height))
            {
                DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                capture.Save(Path.Combine(output,"assimilate-"+UiText.Language+".png"));
            }
            additionalAssimilate.Checked=false;
            project.Apply(112103,Draft());draftDirty=false;ShowCard(112103);
            if(project.HasChanges)throw new Exception("Assimilate add/remove leaves changes");
            NavigateToCard(202276);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalHarmony is not {Checked:true,Enabled:false})throw new Exception("Native Harmony must be read-only");
            NavigateToCard(112103);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalHarmony is not {Checked:false,Enabled:true})throw new Exception("Harmony addition unavailable");
            additionalHarmony.Checked=true;
            var harmonyDraft=Draft();
            if(harmonyDraft.Harmony!=true || !draftDirty)throw new Exception("Harmony missing from draft");
            project.Apply(112103,harmonyDraft);draftDirty=false;ShowCard(112103);Application.DoEvents();
            if(additionalHarmony is not {Checked:true,Enabled:true} || additionalSymbiosis?.Enabled==true || additionalAssimilate?.Enabled==true)throw new Exception("Harmony module compatibility incorrect");
            using(var capture=new Bitmap(Width,Height))
            {
                DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                capture.Save(Path.Combine(output,"harmony-"+UiText.Language+".png"));
            }
            additionalHarmony.Checked=false;
            project.Apply(112103,Draft());draftDirty=false;ShowCard(112103);
            if(project.HasChanges)throw new Exception("Harmony add/remove leaves changes");
            NavigateToCard(202382);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalIntimidate is not {Checked:true,Enabled:false})throw new Exception("Native Intimidate must be read-only");
            NavigateToCard(112103);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalIntimidate is not {Checked:false,Enabled:true})throw new Exception("Intimidate addition unavailable");
            additionalIntimidate.Checked=true;
            var intimidateDraft=Draft();
            if(intimidateDraft.Intimidate!=true || !draftDirty)throw new Exception("Intimidate missing from draft");
            project.Apply(112103,intimidateDraft);draftDirty=false;ShowCard(112103);Application.DoEvents();
            if(additionalIntimidate is not {Checked:true,Enabled:true} || additionalSymbiosis?.Enabled==true || additionalAssimilate?.Enabled==true || additionalHarmony?.Enabled==true)throw new Exception("Intimidate module compatibility incorrect");
            var choices=additionalIntimidate.Parent!;
            if(choices.Width>numberTable.ClientSize.Width || choices.Controls.Cast<Control>().Any(c=>c.Right>choices.ClientSize.Width || c.Bottom>choices.ClientSize.Height))throw new Exception("Additional ability controls clipped");
            using(var capture=new Bitmap(Width,Height))
            {
                DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                capture.Save(Path.Combine(output,"intimidate-"+UiText.Language+".png"));
            }
            additionalIntimidate.Checked=false;
            project.Apply(112103,Draft());draftDirty=false;ShowCard(112103);
            if(project.HasChanges)throw new Exception("Intimidate add/remove leaves changes");
            NavigateToCard(132305);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalThrive is not {Checked:true,Enabled:false})throw new Exception("Native Thrive must be read-only");
            NavigateToCard(112103);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalThrive is not {Checked:false,Enabled:true})throw new Exception("Thrive addition unavailable");
            string thriveTitle=UiText.Language switch {"ru-ru"=>"Симбиоз","zh-cn"=>"成长",_=>"Thrive"};
            if(additionalThrive.Text!=thriveTitle)throw new Exception("Official Thrive title missing");
            additionalThrive.Checked=true;
            var thriveDraft=Draft();
            if(thriveDraft.Thrive!=true || !draftDirty)throw new Exception("Thrive missing from draft");
            project.Apply(112103,thriveDraft);draftDirty=false;ShowCard(112103);Application.DoEvents();
            if(additionalThrive is not {Checked:true,Enabled:true} || additionalSymbiosis?.Enabled==true || additionalAssimilate?.Enabled==true || additionalHarmony?.Enabled==true || additionalIntimidate?.Enabled==true)throw new Exception("Thrive module compatibility incorrect");
            using(var capture=new Bitmap(Width,Height))
            {
                DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                capture.Save(Path.Combine(output,"thrive-"+UiText.Language+".png"));
            }
            additionalThrive.Checked=false;
            project.Apply(112103,Draft());draftDirty=false;ShowCard(112103);
            if(project.HasChanges)throw new Exception("Thrive add/remove leaves changes");
            NavigateToCard(202618);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalVeteran is not {Checked:true,Enabled:false})throw new Exception("Native Veteran must be read-only");
            NavigateToCard(112103);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalVeteran is not {Checked:false,Enabled:true})throw new Exception("Veteran addition unavailable");
            string veteranTitle=UiText.Language switch {"ru-ru"=>"Ветеран","zh-cn"=>"老兵",_=>"Veteran"};
            if(additionalVeteran.Text!=veteranTitle)throw new Exception("Official Veteran title missing");
            additionalVeteran.Checked=true;
            var veteranDraft=Draft();
            if(veteranDraft.Veteran!=true || !draftDirty)throw new Exception("Veteran missing from draft");
            project.Apply(112103,veteranDraft);draftDirty=false;ShowCard(112103);Application.DoEvents();
            if(additionalVeteran is not {Checked:true,Enabled:true} || additionalSymbiosis?.Enabled==true || additionalAssimilate?.Enabled==true || additionalHarmony?.Enabled==true || additionalIntimidate?.Enabled==true || additionalThrive?.Enabled==true)throw new Exception("Veteran module compatibility incorrect");
            using(var capture=new Bitmap(Width,Height))
            {
                DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                capture.Save(Path.Combine(output,"veteran-"+UiText.Language+".png"));
            }
            additionalVeteran.Checked=false;
            project.Apply(112103,Draft());draftDirty=false;ShowCard(112103);
            if(project.HasChanges)throw new Exception("Veteran add/remove leaves changes");
            NavigateToCard(122203);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalFormation is not {Checked:true,Enabled:false})throw new Exception("Native Formation must be read-only");
            NavigateToCard(200540);sections.SelectedIndex=2;Application.DoEvents();
            if(additionalFormation is not {Checked:false,Enabled:true})throw new Exception("Formation addition unavailable");
            string formationTitle=UiText.Language switch {"ru-ru"=>"Строй","zh-cn"=>"列阵",_=>"Formation"};
            if(additionalFormation.Text!=formationTitle)throw new Exception("Official Formation title missing");
            additionalFormation.Checked=true;
            if(initialZeal.Enabled || initialZeal.Checked)throw new Exception("Formation draft must block permanent Zeal");
            var formationDraft=Draft();
            if(formationDraft.Formation!=true || formationDraft.Zeal!=null || !draftDirty)throw new Exception("Formation missing from draft");
            project.Apply(200540,formationDraft);draftDirty=false;ShowCard(200540);Application.DoEvents();
            if(additionalFormation is not {Checked:true,Enabled:true} || initialZeal.Enabled)throw new Exception("Formation module compatibility incorrect");
            using(var capture=new Bitmap(Width,Height))
            {
                DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                capture.Save(Path.Combine(output,"formation-"+UiText.Language+".png"));
            }
            additionalFormation.Checked=false;
            project.Apply(200540,Draft());draftDirty=false;ShowCard(200540);
            if(project.HasChanges || !initialZeal.Enabled)throw new Exception("Formation add/remove leaves changes or locks Zeal");
            foreach(string keyword in new[]{"assimilate","harmony","intimidate","thrive"})
            {
                NavigateToCard(112103);sections.SelectedIndex=2;Application.DoEvents();
                CheckBox Box()=>keyword switch {"assimilate"=>additionalAssimilate!,"harmony"=>additionalHarmony!,"intimidate"=>additionalIntimidate!,_=>additionalThrive!};
                if(additionalValueInputs[keyword].Enabled)throw new Exception("Unchecked module value enabled");
                Box().Checked=true;additionalValueInputs[keyword].Value=2;
                var valueDraft=Draft();
                if(valueDraft.AdditionalValues?.Count!=1 || valueDraft.AdditionalValues[keyword]!=2)throw new Exception("Value missing from draft");
                project.Apply(112103,valueDraft);draftDirty=false;ShowCard(112103);Application.DoEvents();
                if(additionalValueInputs[keyword].Value!=2 || !additionalValueInputs[keyword].Enabled)throw new Exception("Applied value missing");
                additionalValueInputs[keyword].Value=3;
                project.Apply(112103,Draft());draftDirty=false;ShowCard(112103);Application.DoEvents();
                if(project.AdditionalAbilityValue(112103,keyword)!=3)throw new Exception("Module value update failed");
                using(var capture=new Bitmap(Width,Height))
                {
                    DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                    capture.Save(Path.Combine(output,"value-"+keyword+"-"+UiText.Language+".png"));
                }
                Box().Checked=false;
                if(additionalValueInputs[keyword].Enabled || Draft().AdditionalValues?.Count!=0)throw new Exception("Disabled value retained in draft");
                project.Apply(112103,Draft());draftDirty=false;ShowCard(112103);
                if(project.HasChanges)throw new Exception("Value add/update/remove leaves changes");
            }
            foreach(var (card,keyword) in new[]{(202246,"assimilate"),(202382,"intimidate"),(132305,"thrive"),(202276,"harmony"),(202468,"harmony"),(202449,"assimilate"),(202385,"intimidate")})
            {
                NavigateToCard(card);sections.SelectedIndex=2;Application.DoEvents();
                var setting=project.Numbers(card).Single(n=>n.LabelRole=="keyword-boost:"+keyword);
                if(!numberInputs.TryGetValue(setting.Id,out var input) || input.Minimum!=1 || input.Maximum!=99)throw new Exception("Native keyword input missing");
                int changed=setting.Value+1;input.Value=changed;
                project.Apply(card,Draft());draftDirty=false;ShowCard(card);Application.DoEvents();
                if(numberInputs[setting.Id].Value!=changed || project.DescriptionExcerpt(card,[setting.Id],"en-us").Length==0)throw new Exception("Native keyword value or description missing");
                string labels=string.Join("\n",numberTable.Controls.OfType<Label>().Select(l=>l.Text));
                if(!labels.Contains(AbilityLabel("keyword-boost:"+keyword)))throw new Exception("Native keyword label missing");
                var nativeChoices=additionalSymbiosis!.Parent!;
                int visibleRight=numberTable.Parent!.PointToScreen(new Point(numberTable.Parent.ClientSize.Width,0)).X;
                if(nativeChoices.Controls.Cast<Control>().Any(c=>c.PointToScreen(new Point(c.Width,0)).X>visibleRight
                    || c.Right>nativeChoices.ClientSize.Width || c.Bottom>nativeChoices.ClientSize.Height))throw new Exception("Native keyword choices clipped");
                using(var capture=new Bitmap(Width,Height))
                {
                    DrawToBitmap(capture,new Rectangle(Point.Empty,Size));
                    capture.Save(Path.Combine(output,"native-"+keyword+"-"+card+"-"+UiText.Language+".png"));
                }
                numberInputs[setting.Id].Value=setting.Value;
                project.Apply(card,Draft());draftDirty=false;ShowCard(card);
                if(project.HasChanges)throw new Exception("Native keyword reset leaves changes");
            }
        }
        if(project!.HasChanges || draftDirty)throw new Exception("Additional UI smoke modified project");
        File.WriteAllText(Path.Combine(output,"ui-smoke.json"),"{\"Passed\":true,\"Languages\":3,\"Abilities\":7,\"ConfigurableValues\":4,\"NativeKeywordTypes\":4,\"NativeKeywordCases\":7,\"LocalizedDescriptions\":13,\"AddRemove\":true,\"ClientModified\":false}");
    }
}
