using GwentCardEditor;
using System.Xml.Linq;

internal static class CardClarityRevisionTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        Check(source.Numbers(202383).Any(n=>n.Editable && n.Role=="gord-boost-cap"),"Harald Gord cap remains editable");
        Check(!source.Numbers(142108).Any(n=>n.Editable && n.Role=="damage"),"Schirru presentation damage remains hidden");
        Check(!source.Numbers(200104).Any(n=>n.Editable && n.Role=="damage"),"Ulfhedinn computed half-power damage stays hidden");
        foreach(var (id,number) in new[]{(202277,563),(202981,333),(203240,163)})
        {
            Check(!source.Numbers(id).Any(n=>n.Id==number && n.Editable),"Service counter or fixed targets hidden "+id);
            bool denied=false;try{source.Preview(id,source.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{{number,2}}});}catch(InvalidOperationException){denied=true;}
            Check(denied && !source.HasChanges,"Hidden parameter cannot bypass API "+id);
        }
        foreach(var (id,field,label) in new[]{(202620,324,"veteran-strengthen"),(202618,515,"veteran-strengthen"),(203095,51,"veteran-strengthen"),(203095,145,"bran-start-veteran")})
            Check(source.Numbers(id).Any(n=>n.Id==field && n.Editable && n.LabelRole==label),"Veteran meaning identified "+id+"/"+field);
        Check(source.Numbers(202277).Any(n=>n.Editable && n.Role=="berserk-threshold")
            && source.Numbers(202981).Count(n=>n.Editable && n.Role=="damage")==2,"Real Knut and Melusine parameters retained");
        int[] ids=[203095,202620,202618];string folder=Path.Combine(scratch,"card-clarity-revision");Directory.CreateDirectory(folder);
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)
            || (string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==37)));
        baseline.Save(Path.Combine(folder,"Abilities.xml"));
        new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        var p=new EditorProject(folder);
        p.Apply(203095,p.CurrentEdit(203095) with {Numbers=new Dictionary<int,int>{{51,2},{145,3}}});
        Check(p.Numbers(203095).Single(n=>n.Id==51).LabelRole=="veteran-strengthen" && p.Numbers(203095).Single(n=>n.Id==145).LabelRole=="bran-start-veteran","Bran labels survive independent edits");
        string output=Path.Combine(scratch,"bran-clarity-export");p.Export(output);var exported=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        var graph=exported.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==203095);
        graph.Descendants().Single(e=>(int?)e.Attribute("Id")==51).SetAttributeValue("V",1);
        graph.Descendants().Single(e=>(int?)e.Attribute("Id")==145).SetAttributeValue("V",1);
        Check(XNode.DeepEquals(exported,baseline),"Bran own Veteran and game-start effect change only separate mapped values");
        p.Undo();Check(!p.HasChanges,"Bran edits undo exactly");
        return passed;
    }
}
