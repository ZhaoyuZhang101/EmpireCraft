using db;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftNamePlateLibrary
{
    public static string additionNum => ModClass.REAL_NUM_SWITCH ? ",000" : "";
    public static Dictionary<EmpireCraftMapMode, NameplateAsset> map_modes_nameplates = new Dictionary<EmpireCraftMapMode, NameplateAsset>();
    public static void init()
    {
        NameplateAsset asset = new NameplateAsset
        {
            id = "plate_empire",
            path_sprite = "ui/nameplates/nameplate_empire",
            padding_left = 26,
            padding_right = 26,
            padding_top = -2,
            action_main = delegate (NameplateManager pManager, NameplateAsset pAsset)
            {
                foreach (Empire empire in ModClass.EMPIRE_MANAGER)
                {
                    if (empire != null)
                    {
                        if (empire.empire != null && isWithinCamera(empire.GetEmpireCenter()))
                        {
                            NameplateText npt = prepareNext(pManager, pAsset, 37, 12, 39, 11);
                            showTextEmpire(npt, empire.empire);
                        }
                    }
                }
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom.hasCapital() && !kingdom.isInEmpire() && isWithinCamera(kingdom.capital.city_center))
                    {
                        NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library.plate_kingdom);
                        showTextKingdom(nameplateText, kingdom);
                    }

                }
            }
        };
        map_modes_nameplates.Add(EmpireCraftMapMode.Empire, asset);

        NameplateAsset asset2 = new NameplateAsset
        {
            id = "plate_title",
            path_sprite = "ui/nameplates/nameplate_city",
            padding_left = 26,
            padding_right = 26,
            padding_top = -2,
            action_main = delegate (NameplateManager pManager, NameplateAsset pAsset)
            {
                foreach (KingdomTitle kingdomTitle in ModClass.KINGDOM_TITLE_MANAGER)
                {
                    if (kingdomTitle != null && isWithinCamera(kingdomTitle.GetCenter()))
                    {
                        NameplateText npt = prepareNext(pManager, pAsset, 37, 12, 39, 11);
                        showTextTitle(npt, kingdomTitle.title_capital);
                    }
                }
                foreach (City city in World.world.cities)
                {
                    if (!city.hasTitle() && isWithinCamera(city.city_center))
                    {
                        NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library.plate_city);
                        showTextCity(nameplateText, city);
                    }
                }
            },
        };
        map_modes_nameplates.Add(EmpireCraftMapMode.Title, asset2);

        NameplateAsset asset3 = new NameplateAsset
        {
            id = "plate_province",
            path_sprite = "ui/nameplates/nameplate_province",
            padding_left = 26,
            padding_right = 26,
            padding_top = -2,
            action_main = delegate (NameplateManager pManager, NameplateAsset pAsset)
            {
                foreach (Province province in ModClass.PROVINCE_MANAGER)
                {
                    if (province != null && isWithinCamera(province.GetCenter())&&!province.data.is_set_to_country)
                    {
                        NameplateText npt = prepareNext(pManager, pAsset, 37, 12, 39, 11);
                        showTextProvince(npt, province.province_capital);
                    }
                }
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom != null)
                    {
                        if (kingdom.hasCapital() && !kingdom.isEmpire() && isWithinCamera(kingdom.capital.city_center))
                        {
                            NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library.plate_kingdom);
                            showTextKingdom(nameplateText, kingdom);
                        }
                    }
                }
            },
        };
        map_modes_nameplates.Add(EmpireCraftMapMode.Province, asset3);

        NameplateAsset asset4 = new NameplateAsset
        {
            id = "plate_culture",
            path_sprite = "ui/nameplates/nameplate_culture",
            padding_left = 11,
            padding_right = 13,
            map_mode = MetaType.Culture,
            action_main = delegate (NameplateManager pManager, NameplateAsset pAsset)
            {
                ListPool<Culture> c = new ListPool<Culture>();
                foreach(City city in World.world.cities)
                {
                    if (city.hasCulture())
                    {
                        if (!c.Contains(city.culture))
                        {
                            c.Add(city.culture);
                        }
                    }
                }
                foreach(Culture culture in c) 
                {
                    if (culture.cities.Count>0)
                    {
                        NameplateText nameplateText = pManager.prepareNext(pAsset);
                        nameplateText.showTextCulture(culture, culture.cities[0].city_center);
                    }
                }
            }
        };
        AssetManager.nameplates_library.dict.Remove("Culture");
        AssetManager.nameplates_library.map_modes_nameplates[asset4.map_mode] = asset4;
        AssetManager.nameplates_library.dict["Culture"] = asset4;
        
        
        NameplateAsset asset5 = new NameplateAsset
        {
            id = "plate_city",
            path_sprite = "ui/nameplates/nameplate_city",
            map_mode = MetaType.City,
            padding_left = 6,
            padding_right = 7,
            padding_top = -2,
            action_main = delegate(NameplateManager pManager, NameplateAsset _)
            {
                using ListPool<City> listPool = new ListPool<City>(World.world.cities.list);
                listPool.Sort(sortByMembers);
                int num = 0;
                foreach (ref City item2 in listPool)
                {
                    City current = item2;
                    if (num >= 100)
                    {
                        break;
                    }
                    if (isWithinCamera(current.city_center))
                    {
                        NameplateText nameplateText = 
                            !current.isCapitalCity() ? 
                            pManager.prepareNext(AssetManager.nameplates_library.plate_city) : 
                            pManager.prepareNext(AssetManager.nameplates_library.plate_city_capital);
                        showTextCity(nameplateText, current);
                        num++;
                    }
                }
            }
        };
        AssetManager.nameplates_library.dict.Remove("City");
        AssetManager.nameplates_library.map_modes_nameplates[asset5.map_mode] = asset5;
        AssetManager.nameplates_library.dict["City"] = asset5;
        
        
        NameplateAsset asset6 = new NameplateAsset
        {
            id = "plate_kingdom",
            path_sprite = "ui/nameplates/nameplate_kingdom",
            padding_left = 26,
            padding_right = 26,
            padding_top = -2,
            map_mode = MetaType.Kingdom,
            action_main = delegate(NameplateManager pManager, NameplateAsset pAsset)
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom.hasCapital() && isWithinCamera(kingdom.capital.city_center))
                    {
                        NameplateText  nameplateText = pManager.prepareNext(pAsset);
                        showTextKingdom(nameplateText, kingdom);
                    }
                }
                if (WildKingdomsManager.neutral.cities.Count > 0)
                {
                    foreach (City city in WildKingdomsManager.neutral.cities)
                    {
                        pManager.prepareNext(AssetManager.nameplates_library.plate_city).showTextCity(city);
                    }
                }
            }
        };
        AssetManager.nameplates_library.dict.Remove("Kingdom");
        AssetManager.nameplates_library.map_modes_nameplates[asset6.map_mode] = asset6;
        AssetManager.nameplates_library.dict["Kingdom"] = asset6;
    }
    public static void showTextKingdom(NameplateText npt, Kingdom pMetaObject)
    {
        npt.setupMeta((MetaObjectData) pMetaObject.data, pMetaObject.kingdomColor);
        string pNewText = $"{pMetaObject.name}  {pMetaObject.getPopulationPeople().ToString()+additionNum}";
        int num;
        if (DebugConfig.isOn(DebugOption.ShowWarriorsCityText))
        {
            string[] strArray = new string[5]
            {
                pNewText,
                " | ",
                null,
                null,
                null
            };
            num = pMetaObject.countTotalWarriors();
            strArray[2] = num.ToString();
            strArray[3] = "/";
            num = pMetaObject.countWarriorsMax();
            strArray[4] = num.ToString();
            pNewText = string.Concat(strArray);
        }
        if (DebugConfig.isOn(DebugOption.ShowCityWeaponsText))
        {
            string str1 = pNewText;
            num = pMetaObject.countWeapons();
            string str2 = num.ToString();
            pNewText = $"{str1} | w{str2}";
        }
        npt.setText(pNewText, (Vector3) pMetaObject.capital.city_center);
        npt.priority_population = pMetaObject.units.Count;
        npt.showSpecies(pMetaObject.getSpriteIcon());
        npt._show_banner_kingdom = true;
        npt._banner_kingdoms.load((NanoObject) pMetaObject);
        Clan kingClan = pMetaObject.getKingClan();
        if (kingClan != null)
        {
            npt._show_banner_clan = true;
            npt._banner_clan.load((NanoObject) kingClan);
        }
        npt.nano_object = (NanoObject) pMetaObject;
    }
    public static void showTextCity(NameplateText npt, City pMetaObject)
    {
        npt.setupMeta(pMetaObject.data, pMetaObject.kingdom.getColor());
        string text = pMetaObject.name + "  " + pMetaObject.getPopulationPeople()+additionNum;
        if (DebugConfig.isOn(DebugOption.ShowWarriorsCityText))
        {
            text = text + " | " + pMetaObject.countWarriors() + $"{additionNum}/" + pMetaObject.getMaxWarriors()+additionNum;
            if (Config.isEditor)
            {
                string text2 = "  :  " + (int)(pMetaObject.getArmyMaxMultiplier() * 100f) + "%";
                text += text2;
            }
        }
        if (DebugConfig.isOn(DebugOption.ShowCityWeaponsText))
        {
            text = text + " | w" + pMetaObject.countWeapons();
        }
        if (DebugConfig.isOn(DebugOption.ShowFoodCityText))
        {
            text = text + " | F" + pMetaObject.getTotalFood();
        }
        npt.setText(text, pMetaObject.city_center);
        if (pMetaObject.isCapitalCity())
        {
            npt.showSpecial("ui/Icons/iconKingdom");
        }
        if (pMetaObject.getMainSubspecies() != null)
        {
            npt.showSpecies(pMetaObject.getMainSubspecies().getActorAsset().getSpriteIcon());
        }
        if (pMetaObject.last_ticks != 0)
        {
            npt._show_conquest = true;
            if (pMetaObject.being_captured_by != null && pMetaObject.being_captured_by.isAlive())
            {
                npt.conquerText.color = pMetaObject.being_captured_by.kingdomColor.getColorText();
            }
            npt.conquerText.text = pMetaObject.last_ticks + "%";
        }
        Clan royalClan = pMetaObject.getRoyalClan();
        if (royalClan != null)
        {
            npt._show_banner_clan = true;
            npt._banner_clan.load(royalClan);
        }
        npt.priority_capital = pMetaObject.isCapitalCity();
        npt.priority_population = pMetaObject.status.population;
        npt.nano_object = pMetaObject;
    }
    public static bool isWithinCamera(Vector2 pVector)
    {
        return World.world.move_camera.isWithinCameraViewNotPowerBar(pVector);
    }
    public static NameplateText prepareNext(NameplateManager __instance, NameplateAsset pAsset, float left = 0, float bottom = 0, float right = 0, float top = 0)
    {
        NameplateText nameplateText;
        if (__instance.active.Count > __instance._usedIndex)
        {
            nameplateText = __instance.active[__instance._usedIndex];
        }
        else
        {
            nameplateText = __instance.pool.Count != 0 ? __instance.pool.Pop() : __instance.createNew();
            __instance.active.Add(nameplateText);
        }
        nameplateText.reset();
        nameplateText.setShowing(pVal: true);
        Sprite sprite = SpriteTextureLoader.getSprite(pAsset.path_sprite);
        var text = sprite.texture;
        var rect = sprite.rect;
        var pivot = sprite.pivot;
        float ppu = sprite.pixelsPerUnit;
        var sliced = Sprite.Create(text, rect, pivot, ppu, 0, SpriteMeshType.FullRect, new Vector4(left, bottom, right, top));
        var img = nameplateText.background_image;
        img.sprite = sliced;
        img.type = Image.Type.Sliced;
        nameplateText.layout_group.padding.left = pAsset.padding_left;
        nameplateText.layout_group.padding.right = pAsset.padding_right;
        nameplateText.layout_group.padding.top = pAsset.padding_top;
        __instance._usedIndex++;
        return nameplateText;
    }
    public static int sortByMembers(City pObject1, City pObject2)
    {
        if (pObject1.isFavorite() && !pObject2.isFavorite())
        {
            return -1;
        }
        if (!pObject1.isFavorite() && pObject2.isFavorite())
        {
            return 1;
        }
        return pObject2.units.Count.CompareTo(pObject1.units.Count);
    }

    public static void showTextEmpire(NameplateText plateText, Kingdom pMetaObject)
    {
        if (ModClass.IS_CLEAR) return;
        if (pMetaObject == null) return;
        if (!pMetaObject.isAlive()) return;
        Empire empire = pMetaObject.GetEmpire();
        if (empire == null) return;
        plateText.setupMeta(pMetaObject.data, pMetaObject.getColor());
        string text = empire.data.name + "  " + empire.countPopulation()+additionNum;
        if (empire.isAllowToMakeYearName())
        {
            if (empire.hasYearName())
            {
                text = empire.data.name + "\u200A" + empire.getYearNameWithTime() + "\u200A" + empire.countPopulation();
            }
        }


        text = text + " | " + pMetaObject.countTotalWarriors() + $"{additionNum}/" + pMetaObject.countWarriorsMax()+additionNum;
        plateText.setText(text, pMetaObject.GetEmpire().GetEmpireCenter());
        plateText.priority_population = pMetaObject.units.Count;
        plateText.showSpecies(pMetaObject.getSpriteIcon());
        plateText._show_banner_kingdom = true;
        plateText._banner_kingdoms.load(pMetaObject);
        Clan kingClan = pMetaObject.getKingClan();
        if (kingClan != null)
        {
            plateText._show_banner_clan = true;
            plateText._banner_clan.load(kingClan);
        }
        plateText.nano_object = empire.empire;
    }

    public static void showTextTitle(NameplateText plateText, City capital)
    {
        if (ModClass.IS_CLEAR) return;
        if (capital == null) return;
        if (!capital.hasTitle()) return;
        try
        {
            plateText.setupMeta(capital.data, capital.GetTitle().getColor());
            string text = capital.GetTitle().data.name + " | " + capital.GetTitle().data.province_name;
            plateText.setText(text, capital.GetTitle().GetCenter());
            plateText._banner_kingdoms.dead_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.left_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.winner_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.loser_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.part_background.sprite = capital.GetTitle().getElementBackground();
            plateText._banner_kingdoms.part_icon.sprite = capital.GetTitle().getElementIcon();
            plateText._banner_kingdoms.part_background.color = capital.GetTitle().kingdomColor.getColorMain2();
            plateText._banner_kingdoms.part_icon.color = capital.GetTitle().kingdomColor.getColorBanner();
            plateText._show_banner_kingdom = true;
            plateText.nano_object = capital;
        }
        catch (Exception e)
        {
            return;
        }

    }

    public static void showTextProvince(NameplateText plateText, City capital)
    {
        if (ModClass.IS_CLEAR) return;
        if (capital == null) return;
        if (!capital.hasProvince()) return;
        try
        {
            plateText.setupMeta(capital.data, capital.kingdom.getColor());
            Province province = capital.GetProvince();
            string text = province.data.name+"|"+province.empire.GetEmpireName();
            if (province.IsTotalVassaled())
            {
                text = LM.Get("provinceVassaled") + "|" + text;
            }
            plateText.setText(text, capital.city_center);
            plateText._banner_kingdoms.dead_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.left_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.winner_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.loser_image.gameObject.SetActive(value: false);
            plateText._show_banner_kingdom = false;
            plateText._show_banner_clan = false;
            plateText.nano_object = capital;
        }
        catch (Exception e)
        {
            LogService.LogInfo(e.ToString());
            return;
        }
    }
}
