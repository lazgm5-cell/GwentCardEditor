using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private bool ReviewBranchVerified(int id,XElement graph)
    {
        foreach(var spec in new[]{ProfitSpec,EchoSpec}.Where(s=>OwnedTrigger(graph,s)))
        {
            graph=new XElement(graph);
            if(!TriggerOwnedParts(graph,spec,out int saved,out var nodes,out var connections))return false;
            foreach(var variable in TriggerVariables(graph,spec))variable.Remove();
            foreach(var part in nodes.Concat(connections))part.Remove();
            graph.SetAttributeValue("NId",saved);
        }
        var profile=id switch
        {
            202295=>(Fields:new[]{1,249,250,259,260},Hash:"86BFD588603367E775718AC2F3E98C370CAA4064FB853BBE5AE6EAE3F6925BA7"),
            202906=>(Fields:new[]{11,12,85,86,166,167},Hash:"7E360ABD46DBBD36F7CD43FFF86D9F6F3506D8089F5EB37431253769252F7977"),
            203209=>(Fields:new[]{56,57,58,59,60,77,78,121,122},Hash:"0FB42734B5FF8E2D686150EC5055A0F067FBE9FDF8451DC168C68D0D2D4737ED"),
            _=>(Fields:Array.Empty<int>(),Hash:"")
        };
        return profile.Fields.Length>0 && EffectFingerprint(graph,profile.Fields)==profile.Hash
            && (id!=202295 || VerifiedEffectSub(18,"CAAEFEB91A9EA936E9905CD1D73AD53D63D475A40697301D53E3C302963304CB"));
    }
    private static string? ReviewBranchLabel(int id,int field)=>(id,field) switch
    {
        (202295,249 or 250)=>"tribute-targets",
        (202295,259 or 260)=>"without-tribute-targets",
        (202906,11 or 12)=>"first-banish-choices",
        (202906,166 or 167)=>"extra-bronze-banish-choices",
        (203209,57)=>"fee-single-damage",
        (203209,58)=>"berserk-all-damage",
        _=>null
    };
    private List<AbilityNumber> ReviewBranchNumbers(int id,XElement graph,List<AbilityNumber> values)
    {
        if(id is not (202295 or 202906 or 203209))return values;
        bool verified=ReviewBranchVerified(id,graph);
        return values.Select(n=>
        {
            string? label=ReviewBranchLabel(id,n.Id);
            if(label==null)return n;
            return verified?n with {LabelRole=label}:n with {Editable=false,Reason="InitialValueLocked"};
        }).ToList();
    }
}
