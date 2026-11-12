using GwentCardEditor;
using System.Xml.Linq;

internal static class ChoiceEffectTests
{
    public static IEnumerable<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed.Add(name); }
        void Reject(EditorProject p, int card, int field, int value)
        {
            bool rejected = false;
            try { p.Preview(card, p.CurrentEdit(card) with { Numbers = new Dictionary<int,int> { [field] = value } }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, $"Choice contract rejects {card}/{field}/{value}");
        }
        Check(source.NumberGroups(202107).Count == 1 && source.NumberGroups(202107)[0].Display.LabelRole == "barnabas-boost", "Barnabas has one shared boost, no misleading selection parameters");
        Check(source.NumberGroups(200228).Single().Display is { Id: -11, Value: 6 }, "Pitfall exposes total Spring damage only");
        Check(source.NumberGroups(202674).Count == 0, "Shaping Nature parameters belong to choices");
        foreach (int id in new[] {202700,202701,202702})
            Check(source.Numbers(id).Count == (id == 202702 ? 3 : 2), "Choice settings available " + id);
        Reject(source,202107,903,2); Reject(source,200228,455,2); Reject(source,200228,689,2);
        Reject(source,200228,-11,100); Reject(source,202674,1133,8); Reject(source,202674,1150,2);
        Reject(source,202700,-10,0); Reject(source,202702,2,100); Reject(source,202701,2,5);
        int[] ids = [202107,200228,202674,202700,202701,202702];
        string folder = Path.Combine(scratch,"choice-effects"); Directory.CreateDirectory(folder);
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));
        var baseline = new XDocument(new XElement("Abilities",abilities.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1))));
        new XDocument(new XElement("Templates",templates.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string locale in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder,locale+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,locale+".csv")).Where(l=>ids.Any(id=>l.StartsWith(id+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument a) { a.Save(Path.Combine(folder,"Abilities.xml")); return new EditorProject(folder); }
        XElement Graph(XDocument a,int id)=>a.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id);
        XElement Field(XDocument a,int id,int key)=>Graph(a,id).Descendants().Single(e=>(int?)e.Attribute("Id")==key);
        var p = Load(baseline);
        foreach(var (card,field,value) in new[]{(202700,1,10),(202701,1,12),(202702,1,8),(202702,2,9),(202700,-10,2),(200228,-11,8)})
        {
            var edit = p.CurrentEdit(card) with { Numbers = new Dictionary<int,int>{[field]=value} };
            Check(p.Preview(card,edit).Count == (card==200228?1:2), "Synchronized preview " + card + "/" + field);
            p.Apply(card,edit);
        }
        Check(new[]{202700,202701,202702}.All(id=>p.Numbers(id).Single(n=>n.Id == -10).Value==2),"Shared targets reflected in all three choices");
        string output=Path.Combine(scratch,"choice-export"); p.Export(output);
        var after=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        foreach(var (card,field,value) in new[]{(202674,1130,10),(202674,1131,12),(202674,1132,8),(202674,1133,9),(202674,1150,2),(202674,1151,2),(202700,1,10),(202701,1,12),(202702,1,8),(202702,2,9),(200228,764,8)})
        {
            Check((int)Field(after,card,field).Attribute("V")! == value,"Exported effect and tooltip " + card+"/"+field);
            Field(after,card,field).SetAttributeValue("V",(string?)Field(baseline,card,field).Attribute("V"));
        }
        Check(XNode.DeepEquals(after,baseline),"Choice export changes only intended scalar values, not control flow");
        var reopened=new EditorProject(output);
        Check(reopened.Numbers(202702).Single(n=>n.Id==2).Value==9,"Choice export reopens");
        for(int i=0;i<6;i++) p.Undo(); Check(!p.HasChanges,"All linked edits undo atomically");
        foreach(string locale in source.AvailableLanguages)
            Check(File.ReadAllBytes(Path.Combine(folder,locale+".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(output,locale+".csv"))),"Localization preserved " + locale);
        var mismatch=new XDocument(baseline); Field(mismatch,202700,1).SetAttributeValue("V",20);
        var blocked=Load(mismatch); Check(blocked.Numbers(202700).Count==0,"Mismatched choice tooltip blocks editing"); Reject(blocked,202700,1,10);
        var quantum=new XDocument(baseline); Field(quantum,200228,455).SetAttributeValue("V",2);
        blocked=Load(quantum); Check(blocked.VerifiedEffects(200228).Count==0,"Modified damage quantum blocks total-damage contract");
        var changed=new XDocument(baseline); Field(changed,202674,1218).SetAttributeValue("V",1);
        blocked=Load(changed); Check(blocked.Numbers(202701).Count==0,"Modified parent effect blocks choice editing");
        return passed;
    }
}
