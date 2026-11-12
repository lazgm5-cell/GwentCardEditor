using GwentCardEditor;
using AbilityAnalyzer.Services;
using System.Xml.Linq;

internal static class SyndicateEditingTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        foreach(var category in CategoryRegistry.All)
            foreach(string lang in source.AvailableLanguages)
                Check(!string.IsNullOrWhiteSpace(source.CategoryTerm(category.Name,lang)),"Official category localization "+category.Name+"/"+lang);
        var gangs=new[]{("firesworn",94,202630),("crownsplitters",95,202890),("cutups",97,202312),
            ("tidecloaks",98,203170),("blindeyes",99,203100),("salamandra",105,202427)};
        foreach(var (name,bit,card) in gangs)
        {
            var category=CategoryRegistry.Resolve(name);
            Check(category.Slot==1 && category.Mask==(1UL<<(bit-64)),"Verified gang bit "+name);
            Check(source.Categories(card).Contains(name),"Native gang decoded "+name);
            foreach(string lang in source.AvailableLanguages)
                Check(!string.IsNullOrWhiteSpace(source.CategoryTerm(name,lang)),"Official gang localization "+name+"/"+lang);
        }
        var eligible=source.Cards.Where(c=>source.AdditionalProfit(c.Id).Editable).Select(c=>c.Id).ToArray();
        Check(eligible.Length>50,"Profit addition covers ordinary Syndicate units, specials and artifacts: "+eligible.Length);
        foreach(int id in new[]{202294,202295,202374,202349})Check(eligible.Contains(id),"Expected profit recipient "+id);
        Check(source.AdditionalProfit(202310) is {Value:true,Editable:false,Added:false},"Native Profit preserved");
        Check(!source.AdditionalProfit(112103).Editable && !source.AdditionalProfit(202347).Editable,"Non-Syndicate card and leader blocked");
        int[] ids=eligible.Concat(new[]{202310,112103,202347,202427,202630,202890,202312,203170,203100,202246,202475}).Distinct().ToArray();
        string folder=Path.Combine(scratch,"syndicate-editing");Directory.CreateDirectory(folder);
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1) || (string?)e.Attribute("Type") is "SubAbility" or "BattleAbility")));
        var templateBaseline=new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1))));
        templateBaseline.Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal)) || l.StartsWith("card_category_",StringComparison.Ordinal)));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        var p=Load(baseline);
        void Reject(int id,CardEdit edit,string name){bool denied=false;try{p.Preview(id,edit);}catch(InvalidOperationException){denied=true;}Check(denied && !p.HasChanges,name);}
        Reject(202294,p.CurrentEdit(202294) with {Profit=true,AdditionalValues=new Dictionary<string,int>{{"profit",10}}},"Profit above coin capacity rejected");
        Reject(202294,p.CurrentEdit(202294) with {Profit=true,AdditionalValues=new Dictionary<string,int>{{"profit",0}}},"Zero Profit rejected");
        Reject(202294,p.CurrentEdit(202294) with {Profit=true,Harmony=true},"Two new modules rejected");
        Reject(112103,p.CurrentEdit(112103) with {Profit=true},"Non-Syndicate Profit API blocked");
        Reject(202310,p.CurrentEdit(202310) with {Profit=false},"Native Profit cannot be removed by module API");
        Reject(202294,p.CurrentEdit(202294) with {Profit=true,Faction=1},"Simultaneous incompatible faction change rejected");
        foreach(int id in eligible)
        {
            var before=p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>n.Id).Order().ToArray();
            var edit=p.CurrentEdit(id) with {Profit=true,AdditionalValues=new Dictionary<string,int>{{"profit",3}}};
            Check(p.Preview(id,edit).Any(c=>c.Field=="AdditionalProfit") && !p.HasChanges,"Profit preview is atomic "+id);
            p.Apply(id,edit);
            Check(p.AdditionalProfit(id) is {Value:true,Editable:true,Added:true} && p.AdditionalAbilityValue(id,"profit")==3,"Added Profit recognized "+id);
            Check(p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>n.Id).Order().SequenceEqual(before),"Original numeric settings preserved "+id);
            foreach(string lang in p.AvailableLanguages.Where(l=>p.HasText(id,l,"tooltip")))
                Check(p.Text(id,lang,"tooltip").Contains("<keyword=profit>") && p.Text(id,lang,"tooltip").EndsWith(source.Text(id,lang,"tooltip"),StringComparison.Ordinal)
                    && !p.Text(id,lang,"tooltip").Contains('\n'),"Profit localized "+id+"/"+lang);
            p.Apply(id,p.CurrentEdit(id) with {AdditionalValues=new Dictionary<string,int>{{"profit",9}}});
            Check(p.AdditionalAbilityValue(id,"profit")==9,"Added Profit amount editable "+id);
            p.Apply(id,p.CurrentEdit(id) with {Profit=false});Check(!p.HasChanges,"Profit add/change/remove exact "+id);
        }
        const int target=202294;
        var initial=p.Categories(target);
        var changed=new HashSet<string>(initial,StringComparer.OrdinalIgnoreCase);foreach(var (name,_,_) in gangs)changed.Add(name);
        p.Apply(target,p.CurrentEdit(target) with {Categories=changed,Profit=true,AdditionalValues=new Dictionary<string,int>{{"profit",4}}});
        Check(gangs.All(g=>p.Categories(target).Contains(g.Item1)) && initial.All(c=>p.Categories(target).Contains(c)),"Multiple gangs preserve unrelated categories");
        string output=Path.Combine(scratch,"syndicate-export");p.Export(output);var reopened=new EditorProject(output);
        Check(reopened.AdditionalProfit(target) is {Editable:true,Added:true} && reopened.AdditionalAbilityValue(target,"profit")==4,"Profit export reopens");
        Check(gangs.All(g=>reopened.Categories(target).Contains(g.Item1)),"Gang export reopens");
        var actual=XDocument.Load(Path.Combine(output,"Abilities.xml"));var graph=actual.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==target);
        var module=graph.Element("Nodes")!.Elements().Where(n=>((string?)n.Element("Comment"))?.StartsWith("CardEditor.Profit.")==true).ToArray();
        Check(module.Length==2 && module.Any(n=>(string?)n.Attribute("Type")=="IncomeTrigger"),"Native income trigger and shared profit call used");
        var input=module.Single(n=>(string?)n.Attribute("Type")=="UseSubAbilityNode").Descendants("Data").Single(d=>(string?)d.Attribute("Name")=="Income");
        Check((int?)input.Attribute("V")==4 && !graph.Element("Connections")!.Elements().Any(c=>(int?)c.Attribute("DstId")== (int?)input.Attribute("Id")),"Profit amount is isolated constant");
        bool blocked=false;try{p.Preview(target,p.CurrentEdit(target) with {Numbers=new Dictionary<int,int>{{(int)input.Attribute("Id")!,5}}});}catch(InvalidOperationException){blocked=true;}
        Check(blocked,"Direct edit of owned Profit constant blocked");
        reopened.Apply(target,reopened.CurrentEdit(target) with {Profit=false,Categories=initial});
        string removed=Path.Combine(scratch,"syndicate-removed");reopened.Export(removed);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Abilities.xml")),baseline),"Profit removal restores all graphs exactly");
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Templates.xml")),templateBaseline),"Gang removal restores templates exactly");
        p.Undo();Check(!p.HasChanges,"Combined gang and Profit edit undo exact");
        var beggarCategories=p.Categories(203100);
        Check(p.Apply(203100,p.CurrentEdit(203100) with {Categories=new HashSet<string>(beggarCategories.Where(c=>c!="blindeyes"),StringComparer.OrdinalIgnoreCase)})
            && !p.Categories(203100).Contains("blindeyes"),"Existing gang removable");p.Undo();Check(!p.HasChanges,"Gang removal undo exact");
        var donorChanged=new XDocument(baseline);
        donorChanged.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==202310).Descendants().Single(e=>(int?)e.Attribute("Id")==1).SetAttributeValue("V",9);
        Check(Load(donorChanged).AdditionalProfit(target).Editable,"Changing native donor Profit does not disable additions");
        var corrupt=new XDocument(baseline);corrupt.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==16).SetAttributeValue("NId",9999);
        Check(!Load(corrupt).AdditionalProfit(target).Editable,"Unknown shared Profit implementation blocked");
        return passed;
    }
}
