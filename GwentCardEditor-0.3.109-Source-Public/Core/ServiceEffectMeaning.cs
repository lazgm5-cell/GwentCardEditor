using System.Xml.Linq;
using static GwentCardEditor.GraphMeaningReader;

namespace GwentCardEditor;

internal static class ServiceEffectMeaning
{
    public static string? LockedReason(XElement graph, XElement effect)
    {
        if (Type(effect) is not ("ChangePowerNode" or "ChangeArmorNode" or "RevealCardsNode" or "SpawnCardNode")) return null;
        var r = new GraphMeaningReader(graph);
        bool LeaderReplacement(XElement spawn)
        {
            if(Type(spawn)!="SpawnCardNode")return false;
            var position=r.Producer(spawn.Element("Position"));
            if(Type(position)!="MakeCardPositionNode" || !r.Fixed(position!.Element("Location"),"64")
                || !r.Fixed(position.Element("Index"),"0") || !r.Linked(position.Element("FlowOut"),spawn.Element("FlowIn")))return false;
            var banish=r.Producer(position.Element("FlowIn"));var definition=r.Producer(spawn.Element("Definition"));
            if(Type(banish)!="BanishCardsNode")return false;
            if(Type(definition)=="GetCardDefinitionNode")return true;
            var store=r.Producer(banish!.Element("FlowIn"));
            return Type(definition)=="GetVarNode" && Type(store)=="SetVarNode"
                && (int?)definition!.Attribute("VarId") is int variable && (int?)store!.Attribute("VarId")==variable
                && Type(r.Producer(store.Element("In")))=="GetCardDefinitionNode"
                && !r.Nodes.Any(n=>n!=store && Type(n)!="GetVarNode" && (int?)n.Attribute("VarId")==variable);
        }
        if(Type(effect)=="SpawnCardNode" && LeaderReplacement(effect))return "LeaderEntityReplacement";
        if(Type(effect)=="ChangePowerNode" && r.Fixed(effect.Element("Power"),"0") && r.Fixed(effect.Element("Operation"),"0")
            && r.Fixed(effect.Element("TargetedPower"),"0") && r.Owner(effect.Element("Attacker"))=="Self")
        {
            var previous=r.Producer(effect.Element("FlowIn"));
            if(previous!=null && LeaderReplacement(previous) && r.Owner(effect.Element("Targets"))=="Self")return "LeaderResetSignal";
            if(Type(previous)=="ChangeTapCardChargesNode" && r.Fixed(previous!.Element("Operation"),"1")
                && r.Linked(r.Source(previous.Element("Targets")),effect.Element("Targets")))return "ChargeActivationSignal";
        }
        if (Type(effect) == "RevealCardsNode" && r.Fixed(effect.Element("Num"), "1")
            && r.Owner(effect.Element("ValidCards")) == "Self" && r.Fixed(effect.Element("Random"), "False")
            && Type(r.Producer(effect.Element("PlayerId"))) == "GetOwnerPlayerIdNode")
            return "SelfRevealPresentation";
        if (Type(effect) == "ChangePowerNode" && r.Fixed(effect.Element("Power"), "0") && r.Fixed(effect.Element("Operation"), "0")
            && r.Fixed(effect.Element("TargetedPower"), "0") && r.Owner(effect.Element("Targets")) == "Self" && r.Owner(effect.Element("Attacker")) == "Self")
        {
            var charge = r.Producer(effect.Element("FlowIn"));
            if (Type(charge) == "IntBranchNode" && r.Fixed(charge!.Element("B"), "0")
                && r.Linked(charge.Element("FlowOutBigger"), effect.Element("FlowIn")))
            {
                var parent = r.Producer(charge.Element("FlowIn"));
                var checkedValue = r.Producer(charge.Element("A"));
                var amount = r.Producer(parent?.Element("Value"));
                if (Type(checkedValue) == "GetVarNode" && Type(amount) == "GetVarNode"
                    && (int?)checkedValue!.Attribute("VarId") == (int?)amount!.Attribute("VarId")) charge = parent;
            }
            var leaders = r.Producer(charge?.Element("Targets"));
            if (Type(charge) == "ChangeTapCardChargesNode" && r.Fixed(charge!.Element("Operation"), "1")
                && (r.Owner(charge.Element("Targets")) == "Self" || Type(leaders) == "GetCardsNode" && r.Fixed(leaders!.Element("LocationId"), "64")
                && r.Fixed(leaders.Element("CardTypes"), "1") && r.Fixed(leaders.Element("CardTiers"), "1")
                && Type(r.Producer(leaders.Element("PlayerId"))) == "GetOwnerPlayerIdNode")
                && r.Owner(charge.Element("Attacker")) == "Self") return "ChargeActivationSignal";
            var arm = r.Producer(effect.Element("FlowIn"));
            if (Type(arm) != "SetVarNode" || !r.Fixed(arm!.Element("In"), "True") || Type(r.Producer(arm.Element("FlowIn"))) != "TappedTrigger") return null;
            int? flag = (int?)arm.Attribute("VarId");
            foreach (var force in r.Nodes.Where(n => Type(n) == "ForceCardTriggerNode"))
            {
                var disarm = r.Producer(force.Element("FlowIn"));
                var gate = r.Producer(disarm?.Element("FlowIn"));
                var branch = gate;
                var predicate = r.Producer(gate?.Element("Condition"));
                if (Type(predicate) == "CardHasOnPlayedAbilityNode" && r.Source(predicate!.Element("DS")) is { } checkedCard
                    && r.Linked(checkedCard, force.Element("Card")))
                {
                    branch = r.Producer(gate!.Element("FlowIn"));
                    if (Type(branch) != "BranchNode" || !r.Linked(branch!.Element("FlowOutTrue"), gate.Element("FlowIn"))) continue;
                }
                var getter = r.Producer(branch?.Element("Condition"));
                if (flag != null && Type(disarm) == "SetVarNode" && (int?)disarm!.Attribute("VarId") == flag
                    && r.Fixed(disarm.Element("In"), "False") && Type(branch) == "BranchNode" && Type(gate) == "BranchNode"
                    && r.Linked(gate!.Element("FlowOutTrue"), disarm.Element("FlowIn"))
                    && Type(getter) == "GetVarNode" && (int?)getter!.Attribute("VarId") == flag)
                    return "OrderActivationSignal";
            }
        }
        if (Type(effect) == "ChangePowerNode" && r.Fixed(effect.Element("Power"), "0") && r.Fixed(effect.Element("Operation"), "2")
            && r.Fixed(effect.Element("TargetedPower"), "3") && r.Owner(effect.Element("Targets")) == "Self")
        {
            var store = r.Producer(effect.Element("FlowIn"));
            var armor = r.Producer(store?.Element("In"));
            if (Type(store) == "SetVarNode" && Type(armor) == "GetCardArmorNode" && r.Owner(armor!.Element("DS")) == "Self"
                && Type(r.Producer(store!.Element("FlowIn"))) == "TappedTrigger")
                foreach (var boost in r.Nodes.Where(n => Type(n) == "ChangePowerNode" && r.Linked(effect.Element("FlowOut"), n.Element("FlowIn"))))
                {
                    var saved = r.Producer(boost.Element("Power"));
                    if (r.Fixed(boost.Element("Operation"), "0") && r.Fixed(boost.Element("TargetedPower"), "0")
                        && r.Owner(boost.Element("Targets")) == "Self" && Type(saved) == "GetVarNode"
                        && (int?)saved!.Attribute("VarId") == (int?)store.Attribute("VarId")) return "ArmorConversionReset";
                }
        }
        if (Type(effect) == "ChangeArmorNode" && r.Fixed(effect.Element("Armor"), "0") && r.Fixed(effect.Element("Operation"), "2")
            && r.Owner(effect.Element("Targets")) == "Self")
        {
            var armor = r.Producer(effect.Element("FlowIn"));
            var negative = r.Producer(armor?.Element("FlowIn"));
            var remove = r.Producer(negative?.Element("FlowIn"));
            var trigger = r.Producer(remove?.Element("FlowIn"));
            var readArmor = r.Producer(armor?.Element("A"));
            var raw = r.Producer(negative?.Element("A"));
            if (Type(armor) == "IntBranchNode" && r.Linked(armor!.Element("FlowOutBigger"), effect.Element("FlowIn")) && r.Fixed(armor.Element("B"), "0")
                && Type(readArmor) == "GetCardArmorNode" && r.Owner(readArmor!.Element("DS")) == "Self"
                && Type(negative) == "IntBranchNode" && r.Linked(negative!.Element("FlowOutSmaller"), armor.Element("FlowIn")) && r.Fixed(negative.Element("B"), "0")
                && Type(remove) == "CardAttackRemoveTargetNode" && r.Owner(remove!.Element("Cards")) == "Self"
                && Type(trigger) == "BeforeChangePowerTrigger" && r.Fixed(trigger!.Element("TargetCard"), "1") && Type(raw) == "CardPowerAttackGetAttackRawValueNode"
                && r.Linked(trigger!.Element("CardPowerAttack"), raw!.Element("DS")) && r.Linked(trigger.Element("CardPowerAttack"), remove.Element("Attack")))
                return "ImmunityArmorBookkeeping";
        }
        return null;
    }
}
