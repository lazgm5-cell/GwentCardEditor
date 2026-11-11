using GwentCardEditor;
using System.Xml.Linq;

internal static class LeaderCopyCatalogTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        foreach(var (card,field) in new[]{(202880,75),(202236,203),(202236,243),(162313,345)})
        {
            Check(!source.Numbers(card).Any(n=>n.Id==field && n.Editable),"Leader or charge presentation field hidden "+card+"/"+field);
            bool denied=false;try{source.Preview(card,source.CurrentEdit(card) with {Numbers=new Dictionary<int,int>{{field,2}}});}catch(InvalidOperationException){denied=true;}
            Check(denied && !source.HasChanges,"Service field cannot bypass API "+card+"/"+field);
        }
        Check(source.Numbers(162313).Any(n=>n.Id==293 && n.Editable && n.Role=="charge-change"),"Engineer's real charge effect retained");
        Check(source.Numbers(203040).Single(n=>n.Id==82).LabelRole=="copy-count-unbonded","Illusionist regular copy count identified");
        Check(source.Numbers(203040).Single(n=>n.Id==235).LabelRole=="copy-count-bonded","Illusionist Bonded copy count identified");
        var services=source.Cards.Where(CardCatalog.IsService).ToArray();
        Check(services.Length>=6 && Enumerable.Range(100,6).All(id=>services.Any(c=>c.Id==id)),"Faction service records classified by type, not translated names");
        foreach(var group in Enum.GetValues<CardGroup>())foreach(bool hidden in new[]{false,true})
            Check(EditorProject.Factions.SelectMany(f=>CardCatalog.Query(source,f,"ru-ru",group:group,showHidden:hidden)).All(c=>!CardCatalog.IsService(c)),"No services in catalog "+group+" hidden="+hidden);
        var leader=source.Cards.First(c=>c.Type==1 && c.Editable && !CardCatalog.IsHidden(c));
        var token=source.Cards.First(c=>c.IsToken && c.Editable && !c.IsTutorial && !CardCatalog.IsHidden(c));
        Check(CardCatalog.Query(source,leader.Faction,"en-us",group:CardGroup.Leaders).Any(c=>c.Id==leader.Id),"Real leaders retained");
        Check(CardCatalog.Query(source,token.Faction,"en-us",group:CardGroup.Tokens).Any(c=>c.Id==token.Id),"Real tokens retained");
        Check(CardCatalog.Query(source,source.Card(202140).Faction,"en-us",group:CardGroup.All).Any(c=>c.Id==202140),"Real stratagem retained");
        int[] ids=[202880,202236,162313,203040];string folder=Path.Combine(scratch,"leader-copy-review");Directory.CreateDirectory(folder);
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)
            || (string?)e.Attribute("Type")=="SubAbility" && new[]{10,30}.Contains((int?)e.Attribute("AbilityID")??-1))));
        new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        XElement Graph(XDocument doc,int id)=>doc.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id);
        XElement Field(XDocument doc,int id,int field)=>Graph(doc,id).Descendants().Single(e=>(int?)e.Attribute("Id")==field);
        var p=Load(baseline);
        p.Apply(203040,p.CurrentEdit(203040) with {Numbers=new Dictionary<int,int>{{82,2},{235,3},{140,2}}});
        p.Apply(162313,p.CurrentEdit(162313) with {Numbers=new Dictionary<int,int>{{293,2}}});
        Check(p.Numbers(203040).Single(n=>n.Id==82).Value==2 && p.Numbers(203040).Single(n=>n.Id==235).Value==3,"Illusionist counts independently editable");
        string output=Path.Combine(scratch,"leader-copy-export");p.Export(output);var exported=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        foreach(var (card,field,value) in new[]{(203040,82,2),(203040,235,3),(203040,140,2),(162313,293,2)})
        {
            Check((int?)Field(exported,card,field).Attribute("V")==value,"Exported requested field "+card+"/"+field);
            Field(exported,card,field).SetAttributeValue("V",(string?)Field(baseline,card,field).Attribute("V"));
        }
        Check(XNode.DeepEquals(exported,baseline),"No leader replacement flow, service signal or shared subability changed");
        var reopened=new EditorProject(output);Check(reopened.Numbers(203040).Single(n=>n.Id==235) is {Value:3,LabelRole:"copy-count-bonded"},"Bonded label and value survive export");
        p.Undo();p.Undo();Check(!p.HasChanges,"Requested edits undo exactly");
        var modified=new XDocument(baseline);Field(modified,162313,345).SetAttributeValue("V",2);
        Check(Load(modified).Numbers(162313).Any(n=>n.Id==345 && n.Editable && n.Value==2),"A real positive Engineer boost is not mistaken for a zero signal");
        modified=new XDocument(baseline);Field(modified,202880,133).SetAttributeValue("V",3);
        Check(Load(modified).Numbers(202880).Any(n=>n.Id==75 && n.Editable),"Non-leader position does not match leader replacement contract");
        modified=new XDocument(baseline);modified.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==10).SetAttributeValue("NId",9999);
        Check(Load(modified).Numbers(203040).Where(n=>n.Role=="copy-count").All(n=>n.LabelRole is not ("copy-count-bonded" or "copy-count-unbonded")),"Unknown Bonded body does not get assumed branch labels");
        modified=new XDocument(baseline);
        foreach(int id in ids)
        {
            var graph=Graph(modified,id);var globals=graph.Elements().Where(e=>e.Name.LocalName is "TemporaryVariables" or "PersistentVariables").Elements().Attributes("Id").Select(a=>(int)a).ToHashSet();
            foreach(var attr in graph.Descendants().Attributes().Where(a=>a.Name.LocalName is "Id" or "SrcId" or "DstId" || a.Name.LocalName.EndsWith("VarId",StringComparison.OrdinalIgnoreCase) && int.TryParse(a.Value,out int v) && globals.Contains(v)))
                attr.Value=((int)attr+10000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            graph.SetAttributeValue("NId",(int)graph.Attribute("NId")!+10000);
        }
        var remapped=Load(modified);
        Check(!remapped.Numbers(202880).Any(n=>n.Id==10075 && n.Editable) && !remapped.Numbers(202236).Any(n=>n.Id is 10203 or 10243 && n.Editable),"Leader service recognition ignores local numeric IDs");
        Check(remapped.Numbers(203040).Single(n=>n.Id==10082).LabelRole=="copy-count-unbonded" && remapped.Numbers(203040).Single(n=>n.Id==10235).LabelRole=="copy-count-bonded","Bonded branch labels ignore local numeric IDs");
        return passed;
    }
}
