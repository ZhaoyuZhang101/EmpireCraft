using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftLoyaltyLibrary
{
    public static void init()
    {
        var lib = AssetManager.loyalty_library;
        lib.add(new LoyaltyAsset()
        {
            id = "low_mandate",
            translation_key = "low_mandate",
            calc = delegate(City pCity)
            {
                int result = 0;
                if (!pCity.kingdom.IsInEmpire())
                {
                    return result;
                }
                var empire = pCity.kingdom.GetEmpire();
                if (empire == null || EmpireCraft.Scripts.GeneralSystems.ModernLegitimacy.Applies(empire))
                {
                    return result;
                }

                // 正统 0~100 对应忠诚 -30~+30，与原版各项忠诚(±30 量级)同级。
                // 旧版 (正统-50)*100 一跌破 50 就是几千点负忠诚，全帝国城市同时叛离，开国即崩。
                // 开国气象期间(Empire.InFoundingGrace)正统不足的惩罚减半
                float value = (empire.Mandate - 50) * 0.6f;
                if (value < 0 && empire.InFoundingGrace) value *= 0.5f;
                result = (int)UnityEngine.Mathf.Clamp(value, -30f, 30f);
                return result;
            }
        });
        // 现代国家不看正统，看宪政合法性：同样 0~100 对应忠诚 -30~+30
        lib.add(new LoyaltyAsset()
        {
            id = "constitutional_legitimacy",
            translation_key = "constitutional_legitimacy_loyalty",
            calc = delegate(City pCity)
            {
                EmpireCraft.Scripts.Layer.Empire empire = pCity?.kingdom != null && pCity.kingdom.IsInEmpire() ? pCity.kingdom.GetEmpire() : null;
                if (empire == null || !EmpireCraft.Scripts.GeneralSystems.ModernLegitimacy.Applies(empire)) return 0;
                return (int)UnityEngine.Mathf.Clamp((empire.Legitimacy - 50) * 0.6f, -30f, 30f);
            }
        });
        // 地方政治：执政联盟执政的行政区 +3，在野党执政的行政区抵制中央 -5(见 ProvincialPoliticsSystem)
        lib.add(new LoyaltyAsset()
        {
            id = "local_politics",
            translation_key = "local_politics_loyalty",
            calc = delegate(City pCity)
            {
                Kingdom province = pCity?.kingdom;
                EmpireCraft.Scripts.Layer.Empire empire = province != null && province.IsInEmpire() ? province.GetEmpire() : null;
                if (empire == null || EmpireCraft.Scripts.GeneralSystems.ProvincialPoliticsSystem.GetGoverningParty(empire, province) == null)
                    return 0;
                return EmpireCraft.Scripts.GeneralSystems.ProvincialPoliticsSystem.IsOppositionHeld(empire, province)
                    ? EmpireCraft.Scripts.GeneralSystems.ProvincialPoliticsSystem.OppositionLoyalty
                    : EmpireCraft.Scripts.GeneralSystems.ProvincialPoliticsSystem.GoverningLoyalty;
            }
        });
        // 苛政：前现代帝国的城市忠诚随苛政下降(见 HarshRuleSystem；现代国家改为街头抗争)
        lib.add(new LoyaltyAsset()
        {
            id = "harsh_rule",
            translation_key = "harsh_rule_loyalty",
            calc = pCity => EmpireCraft.Scripts.GeneralSystems.HarshRuleSystem.LoyaltyPenalty(pCity)
        });
        // 屠城之恨：屠戮过本城平民的国家统治这里时忠诚大减(见 MassacreSystem)
        lib.add(new LoyaltyAsset()
        {
            id = "massacre_memory",
            translation_key = "massacre_memory_loyalty",
            calc = pCity => EmpireCraft.Scripts.GeneralSystems.MassacreSystem.LoyaltyPenalty(pCity)
        });
        // 现代国家治理能力：城市忠诚加成(见 ModernStability)
        lib.add(new LoyaltyAsset()
        {
            id = "modern_state_capacity",
            translation_key = "modern_state_capacity_loyalty",
            calc = pCity => pCity?.kingdom != null &&
                            EmpireCraft.Scripts.GeneralSystems.ModernStability.IsModern(pCity.kingdom)
                ? EmpireCraft.Scripts.GeneralSystems.ModernStability.Loyalty
                : 0
        });
        // 开国雄主(或中兴雄主)在位：帝国各城忠诚加成
        lib.add(new LoyaltyAsset()
        {
            id = "founder_ruler",
            translation_key = "founder_ruler_loyalty",
            calc = pCity => pCity?.kingdom != null &&
                            EmpireCraft.Scripts.GeneralSystems.RulerTraitSystem.FounderReigns(pCity.kingdom)
                ? EmpireCraft.Scripts.GeneralSystems.RulerTraitSystem.FounderLoyalty
                : 0
        });
        // 民意：抵制 -15，革命浪潮 -30(异见和满意不影响)
        lib.add(new LoyaltyAsset()
        {
            id = "public_opinion",
            translation_key = "public_opinion_loyalty",
            calc = delegate(City pCity)
            {
                if (pCity?.kingdom == null || !pCity.kingdom.IsInEmpire()) return 0;
                int level = EmpireCraft.Scripts.GeneralSystems.PublicOpinionSystem.GetLevel(pCity.kingdom.GetEmpire());
                return level switch { 2 => -15, 3 => -30, _ => 0 };
            }
        });
    }
}