using GwentCardEditor;
using System.Xml.Linq;

internal static class GordTests
{
    public static IEnumerable<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed.Add(name); }
        Check(source.NumberGroups(142108).Count == 0, "Schirru has no editable damage parameter");
        Check(source.VerifiedEffects(202383).Single() is {Id:-12, Value:12}, "Gord native counter cap recognized");
        Check(source.DescriptionBindings(202383,[-12]).Any(b=>b.Language=="en-us" && b.Linked),"Gord cap linked to native description");
        int[] ids=[142108,202383];
        string folder=Path.Combine(scratch,"gord"); Directory.CreateDirectory(folder);
        var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)
            || (string?)e.Attribute("Type")=="BattleAbility" && (int?)e.Attribute("AbilityID")==5)));
        new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string locale in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder,locale+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,locale+".csv")).Where(l=>ids.Any(id=>l.StartsWith(id+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        XElement Card(XDocument doc,int id)=>doc.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id);
        XElement Battle(XDocument doc)=>doc.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="BattleAbility");
        XElement Field(XElement graph,int id)=>graph.Descendants().Single(e=>(int?)e.Attribute("Id")==id);
        void Reject(EditorProject p,int id,int field,int value)
        {
            bool denied=false;
            try{p.Preview(id,p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{[field]=value}});}
            catch(InvalidOperationException){denied=true;}
            Check(denied,$"Reject unsafe Gord/Schirru edit {id}/{field}/{value}");
        }
        foreach(int cap in new[]{0,1,15,9999})
        {
            var p=Load(baseline);
            var edit=p.CurrentEdit(202383) with {Numbers=new Dictionary<int,int>{[-12]=cap}};
            Check(p.Preview(202383,edit).Count==1,"One user-facing cap change "+cap);
            p.Apply(202383,edit);
            string output=Path.Combine(scratch,"gord-export-"+cap);p.Export(output);
            var doc=XDocument.Load(Path.Combine(output,"Abilities.xml"));
            Check((int)Field(Card(doc,202383),551).Attribute("V")! ==cap && (int)Field(Battle(doc),90).Attribute("V")! ==cap,"Actual counter and tooltip cap exported together "+cap);
            Field(Card(doc,202383),551).SetAttributeValue("V",12);Field(Battle(doc),90).SetAttributeValue("V",12);
            Check(XNode.DeepEquals(doc,baseline),"No other graph or counter logic changes "+cap);
            var reopened=new EditorProject(output);Check(reopened.VerifiedEffects(202383).Single().Value==cap,"Gord cap reopens "+cap);
            Check(source.AvailableLanguages.All(l=>File.ReadAllBytes(Path.Combine(folder,l+".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(output,l+".csv")))),"Every localization preserved "+cap);
            p.Undo();Check(!p.HasChanges,"Gord cap undo removes both export effects "+cap);
        }
        var clean=Load(baseline);Reject(clean,142108,385,1);Reject(clean,202383,-12,-1);Reject(clean,202383,-12,10000);Reject(clean,202383,551,15);
        var changed=new XDocument(baseline);Field(Battle(changed),90).SetAttributeValue("V",15);
        var blocked=Load(changed);Check(blocked.VerifiedEffects(202383).Count==0,"Mismatched counter and tooltip blocked");Reject(blocked,202383,-12,15);
        changed=new XDocument(baseline);Field(Battle(changed),35).SetAttributeValue("V",4);
        blocked=Load(changed);Check(blocked.VerifiedEffects(202383).Count==0,"Modified counter filter blocked");
        changed=new XDocument(baseline);
        var other=new XElement(Card(changed,202383));other.SetAttributeValue("Id",999001);changed.Root!.Add(other);
        blocked=Load(changed);Check(blocked.VerifiedEffects(202383).Count==0,"Additional global counter consumer blocks editing");
        changed=new XDocument(baseline);Field(Card(changed,142108),385).SetAttributeValue("V",2);
        Check(Load(changed).Numbers(142108).Any(n=>n.Id==385 && n.Editable),"Custom nonzero Schirru damage not silently hidden");
        return passed;
    }
}
