using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private static bool GigaChoice(int id)=>id is 201709 or 201710;
    private static int GigaGraphMode(XElement? graph)=>graph==null?-1:EffectFingerprint(graph,49,57) switch
    {
        "BC268F11296B7A1754813CC7ECD1753399953B880EAF9B93484F07718AD67DD9"=>0,
        "D092EF580944F283F38B48026E20DEDBE76D6BD4F17A18F55F11A42751793C0C"=>1,_=>-1
    };
    private bool GigaContract()
    {
        foreach(int id in new[]{202515,201709,201710})
            if(!index.ContainsKey(id) || !Card(id).Editable || SharedAbility(id))return false;
        var parent=CurrentAbility(202515);int mode=GigaGraphMode(parent);
        if(mode<0 || GraphValidator.Validate(parent!).ErrorCount>0)return false;
        var condense=CurrentAbility(201709);var spread=CurrentAbility(201710);
        if(condense==null || spread==null
            || EffectFingerprint(condense,1)!="10DDC1FFB5328420A765F8361F7D42C6091917A92B461E04E83B1B13E0623083"
            || EffectFingerprint(spread,1)!="104C367B668F0CD9331B0BF8E7BB92F83DCACF14392A418DD588E6B1B74D047C")return false;
        int? damage=ScalarValue(parent!,49),total=ScalarValue(parent!,57);
        return damage is >=0 and <=9999 && total is >=0 and <=99
            && ScalarValue(condense,1)==damage && ScalarValue(spread,1)==total
            && (mode==1 || damage==1 && total==6);
    }
    private IReadOnlyList<AbilityNumber> GigaNumbers(int id)=>!GigaContract()?[]:
        [new(1,"damage","Damage",ScalarValue(CurrentAbility(id)!,1)!.Value,0,id==201709?9999:99,true,"","Ability",1,true)
            {LabelRole=id==201709?"giga-condense-damage":"giga-spread-total",HelpKey=id==201709?"GigaCondense":"GigaSpread"}];
    private Dictionary<int,XElement> PrepareGiga(int id,IReadOnlyDictionary<int,int>? edits)
    {
        var result=new Dictionary<int,XElement>();if(!GigaChoice(id) || edits==null || edits.Count==0)return result;
        var setting=GigaNumbers(id).SingleOrDefault()??throw new InvalidOperationException("ComputedEffectUnsupported");
        if(edits.Keys.Any(k=>k!=1))throw new InvalidOperationException("ComputedEffectUnsupported");
        int value=edits[1];if(value<setting.Minimum || value>setting.Maximum)throw new InvalidOperationException("AbilityRange");
        if(value==setting.Value)return result;
        var parent=new XElement(CurrentAbility(202515)!);var child=new XElement(CurrentAbility(id)!);
        Scalar(child,1).SetAttributeValue("V",value);Scalar(parent,id==201709?49:57).SetAttributeValue("V",value);
        // The native graph shares damage and counters between both choices. Isolate
        // six focused strikes from the spread total and keep random hits at one.
        if(GigaGraphMode(OriginalAbility(202515))==0 && ScalarValue(parent,49)==1 && ScalarValue(parent,57)==6)
            parent=new XElement(OriginalAbility(202515)!);
        else if(GigaGraphMode(CurrentAbility(202515))==0)
        {
            parent.Element("Nodes")!.Elements().Where(n=>new[]{58,393}.Contains((int?)n.Attribute("Id")??-1)).Remove();
            parent.Element("Connections")!.Elements().Where(c=>new[]{60,395}.Contains((int?)c.Attribute("Id")??-1)).Remove();
            Scalar(parent,22).SetAttributeValue("V",6);Scalar(parent,303).SetAttributeValue("V",1);
        }
        if(GigaGraphMode(parent)<0 || GraphValidator.Validate(parent).ErrorCount>0)throw new InvalidOperationException("ComputedEffectUnsupported");
        result[id]=child;result[202515]=parent;return result;
    }
    private Dictionary<int,XElement> PrepareChoiceEffects(int id,IReadOnlyDictionary<int,int>? edits)=>GigaChoice(id)?PrepareGiga(id,edits):PrepareNature(id,edits);
    private static bool GigaIsolationChanged(int id,XElement? before,XElement? after)=>id==202515
        && GigaGraphMode(before)>=0 && GigaGraphMode(after)>=0 && GigaGraphMode(before)!=GigaGraphMode(after);
}
