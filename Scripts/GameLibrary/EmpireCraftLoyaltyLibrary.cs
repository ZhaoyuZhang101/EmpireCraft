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
    }
}