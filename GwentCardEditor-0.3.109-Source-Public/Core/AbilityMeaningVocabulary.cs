namespace GwentCardEditor;

public static class AbilityMeaningVocabulary
{
    public static string? Help(string role) => role switch
    {
        "damage" or "base-damage" or "alternate-damage" => "Damage",
        "boost" or "base-boost" or "alternate-boost" or "intimidate-boost" => "Boost",
        "heal" => "Heal", "strengthen" => "Strengthen", "base-power-loss" => "BaseLoss",
        "set-power" => "SetPower", "set-base-power" => "SetBasePower",
        "armor-gain" => "ArmorGain", "armor-loss" => "ArmorLoss", "set-armor" => "SetArmor", "armor-change" => "ArmorChange",
        "min-targets" or "min-choices" or "min-template-choices" => "SelectionMin",
        "max-targets" or "max-choices" or "max-template-choices" => "SelectionMax",
        "targets" or "choices" or "template-choices" => "SelectionCount",
        "spawn-count" => "Spawn", "copy-count" => "Copy", "draw-count" => "Draw", "reveal-count" => "Reveal",
        "profit" or "base-profit" => "Profit", "coin-gain" => "Coins", "fee-cost" => "Fee", "tribute-cost" => "Tribute",
        "bleeding-duration" => "Bleeding", "vitality-duration" => "Vitality",
        "initial-charges" => "InitialCharges", "charge-refill" => "ChargeRefill", "charge-change" => "ChargeChange",
        "initial-patience" => "InitialPatience",
        "first-target-damage" => "FirstTargetDamage", "random-series-start" => "RandomDamageSeries",
        "linked-elephant-boost" => "LinkedElephantBoost",
        "spring-total-damage" => "SpringTotalDamage",
        "gord-boost-cap" => "GordBoostCap",
        "initial-timer" => "InitialTimer", "cooldown" => "Cooldown", "cooldown-reduction" => "CooldownReduction",
        "adrenaline-threshold" => "Adrenaline", "berserk-threshold" => "Berserk", "bloodthirst" => "Bloodthirst",
        "power-limit" => "PowerFilter", "provision-limit" or "definition-provision-limit" => "ProvisionFilter",
        "charges-limit" => "ChargesFilter", "power-level" => "PowerRanking",
        "min-row-targets" or "max-row-targets" or "row-targets" => "Rows", _ => null
    };
}
