using GwentCardEditor;
using System.Xml.Linq;
using System.Text.RegularExpressions;

internal static class NativeKeywordTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        var settings=source.Cards.Where(c=>c.Editable).SelectMany(c=>source.Numbers(c.Id)
            .Where(n=>n.Editable && n.LabelRole?.StartsWith("keyword-boost:",StringComparison.Ordinal)==true).Select(n=>(Card:c.Id,Number:n))).ToArray();
        Check(settings.Length>=20,"Native keyword constants covered structurally: "+settings.Length);
        Check(settings.All(s=>s.Number.Minimum==1 && s.Number.Maximum==99 && s.Number.TextLinked),"Native keyword ranges and description links");
        int[] ids=settings.Select(s=>s.Card).Concat(new[]{112103,202246,202276,202382,132305}).Distinct().ToArray();
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)
            || (string?)e.Attribute("Type")=="SubAbility" && new[]{8,14,17,15}.Contains((int?)e.Attribute("AbilityID")??-1))));
        string folder=Path.Combine(scratch,"native-keywords");Directory.CreateDirectory(folder);
        new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),
            File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        var p=Load(baseline);
        foreach(var item in settings)
        {
            string key=item.Number.LabelRole!["keyword-boost:".Length..];
            int desired=item.Number.Value+1;
            var edit=p.CurrentEdit(item.Card) with {Numbers=new Dictionary<int,int>{{item.Number.Id,desired}}};
            int beforeChanges=p.Changes().Count;
            var preview=p.Preview(item.Card,edit);
            Check(preview.Any(c=>c.Field.Contains("/Ability/#",StringComparison.Ordinal)),"Native value included in preview "+item.Card);
            Check(p.Changes().Count==beforeChanges,"Native preview has no independent mutations "+item.Card);
            p.Apply(item.Card,edit);
            foreach(string lang in source.AvailableLanguages.Where(l=>source.HasText(item.Card,l,"tooltip")))
            {
                string before=source.Text(item.Card,lang,"tooltip"),after=p.Text(item.Card,lang,"tooltip");
                bool alias=Regex.IsMatch(before,"<keyword="+key+">[^<>]+</keyword>(?:[ \\u00a0]+(?:de[ \\u00a0]+)?|:[ \\u00a0]*|[（(])\\{"+Regex.Escape(item.Number.Name)+"\\}");
                Check(alias?after==before:after!=before && after.Contains("</keyword> "+desired),"Native localized number "+item.Card+" "+lang);
                Check(p.DescriptionBindings(item.Card,[item.Number.Id]).Single(b=>b.Language==lang).Linked,"Native description linked "+item.Card+" "+lang);
                Check(p.DescriptionExcerpt(item.Card,[item.Number.Id],lang).Contains("[[value]]"),"Native excerpt identifies number "+item.Card+" "+lang);
            }
            Check(p.PendingDescriptionWarnings().All(w=>w.CardId!=item.Card || !w.NumberIds.Contains(item.Number.Id) || w.Kind=="MissingText"),"No false native description warning "+item.Card);
        }
        string output=Path.Combine(scratch,"native-keyword-export");p.Export(output);
        var reopened=new EditorProject(output);
        foreach(var item in settings)
        {
            var number=reopened.Numbers(item.Card).Single(n=>n.Id==item.Number.Id);
            Check(number.Editable && number.Value==item.Number.Value+1,"Native value editable after export "+item.Card);
            reopened.Apply(item.Card,reopened.CurrentEdit(item.Card) with {Numbers=new Dictionary<int,int>{{item.Number.Id,item.Number.Value}}});
            foreach(string lang in source.AvailableLanguages.Where(l=>source.HasText(item.Card,l,"tooltip")))
                Check(reopened.Text(item.Card,lang,"tooltip")==source.Text(item.Card,lang,"tooltip"),"Native reset restores exact text "+item.Card+" "+lang);
        }
        string reset=Path.Combine(scratch,"native-keyword-reset");reopened.Export(reset);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(reset,"Abilities.xml")),baseline),"Native reset restores every graph exactly");
        reopened=new EditorProject(output);
        reopened.Apply(112103,reopened.CurrentEdit(112103) with {Assimilate=true});
        Check(reopened.AdditionalAbilityValue(112103,"assimilate")==1,"Changed native donor does not leak its rank into a new module");
        reopened.Undo();
        var sample=settings.First(s=>s.Card==202246);
        foreach(int invalid in new[]{0,-1,100,int.MaxValue})
        {
            bool denied=false;try{reopened.Apply(sample.Card,reopened.CurrentEdit(sample.Card) with {Numbers=new Dictionary<int,int>{{sample.Number.Id,invalid}}});}
            catch(InvalidOperationException){denied=true;}
            Check(denied && !reopened.HasChanges,"Native out-of-range change atomic "+invalid);
        }
        var changed=new XDocument(baseline);
        var graph=changed.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==sample.Card);
        var data=graph.Descendants().Single(e=>(int?)e.Attribute("Id")==sample.Number.Id);data.SetAttributeValue("ExternalIn","True");
        Check(!Load(changed).Numbers(sample.Card).Any(n=>n.Id==sample.Number.Id && n.Editable),"Native external input blocked");
        changed=new XDocument(baseline);
        changed.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==8).SetAttributeValue("NId",99999);
        Check(!Load(changed).Numbers(sample.Card).Any(n=>n.Id==sample.Number.Id && n.Editable),"Modified native subability blocked");
        changed=new XDocument(baseline);
        graph=changed.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==sample.Card);
        graph.SetAttributeValue("OtherVarId",sample.Number.Id);
        Check(!Load(changed).Numbers(sample.Card).Any(n=>n.Id==sample.Number.Id && n.Editable),"Native runtime reference blocked");
        changed=new XDocument(baseline);
        graph=changed.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==sample.Card);
        data=graph.Descendants().Single(e=>(int?)e.Attribute("Id")==sample.Number.Id);
        data.Parent!.Element("IsInput")!.Value="False";
        Check(!Load(changed).Numbers(sample.Card).Any(n=>n.Id==sample.Number.Id && n.Editable),"Native output mapping blocked");
        changed=new XDocument(baseline);
        graph=changed.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==sample.Card);
        data=graph.Descendants().Single(e=>(int?)e.Attribute("Id")==sample.Number.Id);
        data.SetAttributeValue("V",2);
        Check(!Load(changed).Numbers(sample.Card).Any(n=>n.Id==sample.Number.Id && n.Editable),"Native text and value mismatch blocked");
        Check(source.DescriptionBindings(sample.Card,[sample.Number.Id],new Dictionary<string,string>{{"en-us","Custom description without keyword"}})
            .Single(b=>b.Language=="en-us").Kind=="Unlinked","Missing native keyword in edited text is not treated as linked");
        changed=new XDocument(baseline);
        graph=changed.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==132305);
        graph.Descendants().Single(e=>(int?)e.Attribute("Id")==198).SetAttributeValue("V",2);
        Check(Load(changed).Numbers(132303).Any(n=>n.LabelRole=="keyword-boost:thrive" && n.Editable),"Native value does not depend on unrelated edits to donor card");
        for(int i=0;i<settings.Select(s=>s.Card).Distinct().Count();i++)p.Undo();
        Check(!p.HasChanges,"All native keyword edits undo exactly");
        return passed;
    }
}
