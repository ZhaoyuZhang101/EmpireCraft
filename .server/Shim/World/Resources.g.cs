// 生成的：tools/gen_resources.py 从原版资源表抽的名字和数(只有数，没有原版代码)。别手改，重跑生成。
// 服务器里原版资源表是空的：模组问"面包是不是吃的"问不出来，城里存着粮也当没有，人就饿死了
internal static partial class ShimResources
{
    internal static void Init()
    {
        Add(new ResourceAsset { id = "wheat", food = true, restore_happiness = 5, restore_health = 0.05f, restore_mana = 5, restore_nutrition = 50, restore_stamina = 5, supply_bound_give = 100, supply_bound_take = 20, type = ResType.Ingredient_Food });
        Add(new ResourceAsset { id = "bread", food = true, maximum = 999, restore_happiness = 10, restore_health = 0.1f, restore_mana = 10, restore_nutrition = 100, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "sushi", food = true, restore_happiness = 20, restore_health = 0.2f, restore_mana = 20, restore_nutrition = 70, restore_stamina = 20, type = ResType.Food });
        Add(new ResourceAsset { id = "jam", food = true, restore_happiness = 25, restore_health = 0.4f, restore_mana = 40, restore_nutrition = 100, restore_stamina = 40, type = ResType.Food });
        Add(new ResourceAsset { id = "cider", food = true, restore_happiness = 20, restore_health = 0.25f, restore_mana = 25, restore_nutrition = 55, restore_stamina = 25, type = ResType.Food });
        Add(new ResourceAsset { id = "ale", food = true, restore_happiness = 20, restore_health = 0.2f, restore_mana = 20, restore_nutrition = 45, restore_stamina = 20, type = ResType.Food });
        Add(new ResourceAsset { id = "burger", food = true, restore_happiness = 25, restore_health = 0.4f, restore_mana = 25, restore_nutrition = 100, restore_stamina = 35, type = ResType.Food });
        Add(new ResourceAsset { id = "pie", food = true, restore_happiness = 25, restore_health = 0.4f, restore_mana = 35, restore_nutrition = 100, restore_stamina = 25, type = ResType.Food });
        Add(new ResourceAsset { id = "tea", food = true, restore_happiness = 10, restore_health = 0.25f, restore_mana = 40, restore_nutrition = 20, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "berries", food = true, restore_happiness = 12, restore_health = 0.35f, restore_mana = 15, restore_nutrition = 30, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "bananas", food = true, restore_happiness = 10, restore_health = 0.4f, restore_mana = 15, restore_nutrition = 25, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "coconut", food = true, restore_happiness = 15, restore_health = 0.45f, restore_mana = 15, restore_nutrition = 30, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "crystal_salt", food = true, restore_happiness = 10, restore_health = 0.2f, restore_mana = 20, restore_nutrition = 20, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "desert_berries", food = true, restore_happiness = 12, restore_health = 0.35f, restore_mana = 15, restore_nutrition = 25, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "evil_beets", food = true, restore_happiness = 15, restore_health = 0.4f, restore_mana = 15, restore_nutrition = 25, restore_stamina = 25, type = ResType.Food });
        Add(new ResourceAsset { id = "mushrooms", food = true, restore_happiness = 15, restore_health = 0.4f, restore_mana = 15, restore_nutrition = 20, restore_stamina = 15, type = ResType.Food });
        Add(new ResourceAsset { id = "peppers", food = true, restore_happiness = 20, restore_health = 0.4f, restore_mana = 25, restore_nutrition = 30, restore_stamina = 15, type = ResType.Food });
        Add(new ResourceAsset { id = "herbs", food = true, restore_happiness = 5, restore_health = 0.5f, restore_mana = 25, restore_nutrition = 25, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "fish", food = true, restore_happiness = 15, restore_health = 0.3f, restore_mana = 25, restore_nutrition = 50, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "candy", food = true, restore_happiness = 40, restore_health = 0.4f, restore_mana = 25, restore_nutrition = 40, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "worms", food = true, restore_happiness = -5, restore_health = 0.2f, restore_mana = 10, restore_nutrition = 10, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "snow_cucumbers", food = true, restore_happiness = 10, restore_health = 0.35f, restore_mana = 25, restore_nutrition = 30, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "celestial_avocado", food = true, restore_happiness = 50, restore_health = 0.55f, restore_mana = 50, restore_nutrition = 100, restore_stamina = 50, type = ResType.Food });
        Add(new ResourceAsset { id = "pine_cones", food = true, restore_happiness = 10, restore_health = 0.15f, restore_mana = 10, restore_nutrition = 15, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "lemons", food = true, restore_happiness = 10, restore_health = 0.3f, restore_mana = 10, restore_nutrition = 20, restore_stamina = 10, type = ResType.Food });
        Add(new ResourceAsset { id = "meat", food = true, restore_happiness = 20, restore_health = 0.5f, restore_mana = 10, restore_nutrition = 60, restore_stamina = 25, type = ResType.Food });
        Add(new ResourceAsset { id = "honey", food = true, restore_happiness = 35, restore_health = 0.45f, restore_mana = 10, restore_nutrition = 15, restore_stamina = 25, type = ResType.Food });
        Add(new ResourceAsset { id = "wood", restore_happiness = 10, restore_health = 0.45f, restore_mana = 15, restore_nutrition = 50, restore_stamina = 15, type = ResType.Strategic });
        Add(new ResourceAsset { id = "stone", restore_happiness = 5, restore_health = 0.2f, restore_mana = 10, restore_nutrition = 20, restore_stamina = 10, type = ResType.Strategic });
        Add(new ResourceAsset { id = "silver", restore_happiness = 20, restore_health = 0.3f, restore_mana = 25, restore_nutrition = 35, restore_stamina = 15, type = ResType.Strategic });
        Add(new ResourceAsset { id = "mythril", restore_happiness = 25, restore_health = 0.4f, restore_mana = 30, restore_nutrition = 45, restore_stamina = 25, type = ResType.Strategic });
        Add(new ResourceAsset { id = "adamantine", restore_happiness = 35, restore_health = 0.5f, restore_mana = 45, restore_nutrition = 55, restore_stamina = 40, type = ResType.Strategic });
        Add(new ResourceAsset { id = "dragon_scales", type = ResType.Strategic });
        Add(new ResourceAsset { id = "common_metals", restore_happiness = 10, restore_health = 0.3f, restore_mana = 15, restore_nutrition = 35, restore_stamina = 15, type = ResType.Strategic });
        Add(new ResourceAsset { id = "bones", type = ResType.Strategic });
        Add(new ResourceAsset { id = "leather", type = ResType.Strategic });
        Add(new ResourceAsset { id = "gems", restore_happiness = 15, restore_health = 0.3f, restore_mana = 15, restore_nutrition = 35, restore_stamina = 25, type = ResType.Strategic });
        Add(new ResourceAsset { id = "fertilizer", type = ResType.Strategic });
        Add(new ResourceAsset { id = "gold", maximum = 999, supply_bound_give = 600, supply_bound_take = 10, supply_give = 100, type = ResType.Currency });
    }
}
