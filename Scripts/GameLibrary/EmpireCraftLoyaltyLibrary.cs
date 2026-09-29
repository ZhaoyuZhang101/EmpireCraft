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
                if (empire == null)
                {
                    return result;
                }

                result = (empire.Mandate - 50) * 100;
                return result;
            }
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