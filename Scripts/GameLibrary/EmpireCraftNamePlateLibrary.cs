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
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftNamePlateLibrary
{
    public static string additionNum => ModClass.REAL_NUM_SWITCH ? "k" : "";
    public static void init()
    {
        NameplateAsset asset = new NameplateAsset
        {
            id = "plate_empire",
            path_sprite = "ui/nameplates/nameplate_empire",
            map_mode = MetaTypeExtension.Empire,
            padding_left = 26,
            padding_right = 26,
            padding_top = -2,
            action_main = delegate (NameplateManager pManager, NameplateAsset pAsset)
            {
                
                int num = 0;
                foreach (Empire empire in ModClass.EMPIRE_MANAGER)
                {
                    if (empire != null)
                    {
                        if (empire.CoreKingdom != null && isWithinCamera(empire.GetEmpireCenter()))
                        {
                            NameplateText npt = prepareNext(pManager, pAsset, empire, 37, 12, 39, 11);
                            showTextEmpire(npt, empire.CoreKingdom);
                        }
                    }
                }
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom.hasCapital() && !kingdom.IsInEmpire() && isWithinCamera(kingdom.capital.city_center))
                    {
                        NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                        showTextKingdom(nameplateText, kingdom);
                    }
                }

                switch (EmpireCraftMetaTypeLibrary.empire.getZoneOptionState())
                {
                    case 1:
                        foreach (Kingdom kingdom in World.world.kingdoms)
                        {
                            if (kingdom.hasCapital() && kingdom.IsInEmpire() && isWithinCamera(kingdom.capital.city_center))
                            {
                                NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                                showTextKingdomNoBack(nameplateText, kingdom);
                            }
                        }
                        break;
                }
            }
        };
        AssetManager.nameplates_library.add(asset);

        NameplateAsset asset2 = new NameplateAsset
        {
            id = "plate_kingdomTitle",
            map_mode = MetaTypeExtension.KingdomTitle,
            path_sprite = "ui/nameplates/nameplate_kingdomTitle",
            padding_left = 26,
            padding_right = 26,
            padding_top = -2,
            action_main = delegate (NameplateManager pManager, NameplateAsset pAsset)
            {
                foreach (KingdomTitle kingdomTitle in ModClass.KINGDOM_TITLE_MANAGER)
                {
                    if (!kingdomTitle.isRekt()&&!kingdomTitle.title_capital.isRekt() && isWithinCamera(kingdomTitle.title_capital.city_center))
                    {
                        NameplateText npt = prepareNext(pManager, pAsset, kingdomTitle, 37, 12, 39, 11);
                        showTextTitle(npt, kingdomTitle.title_capital);
                    }
                }
                foreach (City city in World.world.cities)
                {
                    if (!city.hasTitle() && isWithinCamera(city.city_center))
                    {
                        NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_city, city);
                        showTextCity(nameplateText, city, city.city_center);
                    }
                }
            },
        };
        AssetManager.nameplates_library.add(asset2);
        
        
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
                int num = 0;
                using ListPool<City> listPool = new ListPool<City>(World.world.cities.list);
                listPool.Sort(sortByMembers);
                if (MetaTypeLibrary.city.getZoneOptionState() == 0)
                {
                    foreach (ref City item in listPool)
                    {
                        City current = item;
                        if (num >= _.max_nameplate_count)
                        {
                            break;
                        }
                        if (isWithinCamera(current.city_center))
                        {
                            NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_city, current);
                            showTextCity(nameplateText, current, current.city_center);
                            num++;
                        }
                    }
                    return;
                }
                foreach (City city in World.world.cities)
                {
                    if (num >= _.max_nameplate_count)
                    {
                        break;
                    }
                    Actor pForceActor = null;
                    if (city.hasLeader() && !city.leader.isRekt() && city.leader.is_visible)
                    {
                        pForceActor = city.leader;
                    }
                    if (getPositionForMeta(city, out var pPosition, pForceActor))
                    {
                        pManager.prepareNext(_, city).showTextCity(city, pPosition);
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
                        NameplateText  nameplateText = pManager.prepareNext(pAsset, kingdom);
                        showTextKingdom(nameplateText, kingdom);
                    }
                }
                if (WildKingdomsManager.neutral.cities.Count > 0)
                {
                    foreach (City city in WildKingdomsManager.neutral.cities)
                    {
                        pManager.prepareNext(AssetManager.nameplates_library._plate_city, city).showTextCity(city, city.city_center);
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
    public static void showTextKingdomNoBack(NameplateText npt, Kingdom pMetaObject)
    {
        npt.setupMeta(pMetaObject.data, pMetaObject.getColor());
        string pNewText = $"{pMetaObject.name}  {pMetaObject.getPopulationPeople().ToString()+additionNum}";
        npt.setText(pNewText, pMetaObject.capital.city_center);
        npt._background_image.enabled = false;
        npt.priority_population = pMetaObject.units.Count;
        npt.showSpecies(pMetaObject.getSpriteIcon());
        npt._show_banner_kingdom = false;
        npt._show_banner_clan = false;
        npt.nano_object = pMetaObject;
    }	
    public static bool getPositionForMeta(IMetaObject pMetaObject, out Vector3 pPosition, Actor pForceActor = null)
    {
        if (!pMetaObject.isAlive() || !pMetaObject.hasUnits())
        {
            pPosition = Vector3.zero;
            return false;
        }
        var actor = pForceActor ?? pMetaObject.getOldestVisibleUnitForNameplatesCached();
        if (actor == null)
        {
            pPosition = Vector3.zero;
            return false;
        }
        Vector3 vector = actor.current_position;
        vector.y += actor.getHeight();
        vector.y += -2f;
        pPosition = vector;
        return true;
    }
    public static void showTextCity(NameplateText npt, City pMetaObject, Vector2 pPosition)
    {
        npt.setupMeta(pMetaObject.data, pMetaObject.kingdom.getColor());
        if (pMetaObject.isCapitalCity())
        {
            npt.setNameplateSprite("ui/nameplates/nameplate_city_capital");
        }
        else
        {
            npt.setNameplateSprite("ui/nameplates/nameplate_city");
        }
        int populationPeople = pMetaObject.getPopulationPeople();
        string text = npt.getStringForNameplate(pMetaObject.name, populationPeople) + additionNum;
        if (npt.is_full)
        {
            if (DebugConfig.isOn(DebugOption.ShowWarriorsCityText))
            {
                text = text + " | " + pMetaObject.countWarriors() + "/" + pMetaObject.getMaxWarriors();
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
        }
        npt.setText(text, pPosition);
        if (pMetaObject.getMainSubspecies() != null)
        {
            npt.showSpecies(pMetaObject.getMainSubspecies().getActorAsset().getSpriteIcon());
        }
        if (pMetaObject.last_visual_capture_ticks != 0)
        {
            npt._show_capture_counter = true;
            npt._active_check_dirty = true;
            if (pMetaObject.being_captured_by != null && pMetaObject.being_captured_by.isAlive())
            {
                npt._conquer_text.color = pMetaObject.being_captured_by.getColor().getColorText();
            }
            npt._conquer_text.text = pMetaObject.last_visual_capture_ticks + "%";
        }
        else
        {
            npt._show_capture_counter = false;
            npt._active_check_dirty = true;
        }
        if (npt._show_capture_counter)
        {
            Vector2 anchoredPosition = ((!npt.is_full) ? new Vector2(3f, -25f) : new Vector2(0f, -1f));
            npt._container_capture.anchoredPosition = anchoredPosition;
        }
        npt._show_banner_city = true;
        npt._banner_city.load(pMetaObject);
        npt.priority_capital = pMetaObject.isCapitalCity();
        npt.setPriority(populationPeople);
    }

    public static bool isWithinCamera(Vector2 pVector)
    {
        return World.world.move_camera.isWithinCameraViewNotPowerBar(pVector);
    }
    public static NameplateText prepareNext(NameplateManager __instance, NameplateAsset pAsset, NanoObject pMeta, float left = 0, float bottom = 0, float right = 0, float top = 0)
    {
        NameplateText nameplateText;
        if (__instance._active.Count > __instance._next_index)
        {
            nameplateText = __instance._active[__instance._next_index];
        }
        else
        {
            nameplateText = __instance._pool.Count != 0 ? __instance._pool.Pop() : __instance.createNew();
            __instance._active.Add(nameplateText);
        }
        Sprite sprite = SpriteTextureLoader.getSprite(pAsset.path_sprite);
        var text = sprite.texture;
        var rect = sprite.rect;
        var pivot = sprite.pivot;
        float ppu = sprite.pixelsPerUnit;
        var sliced = Sprite.Create(text, rect, pivot, ppu, 0, SpriteMeshType.FullRect, new Vector4(left, bottom, right, top));
        var img = nameplateText._background_image;
        img.sprite = sliced;
        img.type = Image.Type.Sliced;
        nameplateText.layout_group.padding.left = pAsset.padding_left;
        nameplateText.layout_group.padding.right = pAsset.padding_right;
        nameplateText.layout_group.padding.top = pAsset.padding_top;
        __instance._next_index++;
        prepare(nameplateText, pAsset, pMeta, __instance._tween_scale, __instance._nameplate_mode, __instance._nano_object_set, __instance._selected_nano_object);
        return nameplateText;
    }
    public static void prepare(NameplateText nameplateText, NameplateAsset pAsset, NanoObject pMeta, float pGlobalScale, NameplateRenderingType pNameplateMode, bool pNanoObjectSet, NanoObject pSelectedNanoObject)
    {
        if (pNanoObjectSet)
        {
            pNameplateMode = ((pSelectedNanoObject == pMeta) ? NameplateRenderingType.Full : NameplateRenderingType.BannerOnly);
        }
        if (pNameplateMode != nameplateText._last_mode)
        {
            nameplateText.clearCaches();
            nameplateText._active_check_dirty = true;
            nameplateText._last_mode = pNameplateMode;
            switch (nameplateText._last_mode)
            {
                case NameplateRenderingType.Full:
                    nameplateText._background_image.transform.localScale = new Vector3(1f, 1f, 1f);
                    nameplateText._background_image.enabled = true;
                    nameplateText._banner_kingdoms.gameObject.transform.localScale = Vector3.one;
                    nameplateText._text_name.fontStyle = FontStyle.Normal;
                    nameplateText._text_name.transform.localScale = Vector3.one;
                    break;
                case NameplateRenderingType.BannerOnly:
                    if (pMeta.meta_type == MetaTypeExtension.KingdomTitle)
                    {
                        nameplateText._background_image.transform.localScale = Vector3.one;
                        nameplateText._background_image.enabled = false;
                        nameplateText._banner_kingdoms.enabled = false;
                    }
                    else
                    {
                        nameplateText._text_name.fontStyle = FontStyle.Normal;
                        nameplateText._text_name.transform.localScale = Vector3.one;
                        nameplateText._background_image.transform.localScale = new Vector3(pAsset.banner_only_mode_scale, pAsset.banner_only_mode_scale, 1f);
                        nameplateText._background_image.enabled = false;
                    }
                    break;
            }
        }
        nameplateText.updateScale(pMeta, pGlobalScale, pNanoObjectSet, pSelectedNanoObject);
        nameplateText.resetElements();
        nameplateText.setShowing(pVal: true);
        nameplateText.setAssetAndMeta(pAsset, pMeta);
        if (((IFavoriteable)pMeta).isFavorite())
        {
            nameplateText.showFavoriteIcon();
        }
        else
        {
            nameplateText._show_icon_favorite = false;
        }
        nameplateText.checkSetActive(nameplateText._icon_favorite, nameplateText._show_icon_favorite);
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
        if (empire.IsAllowToMakeYearName())
        {
            if (empire.HasYearName())
            {
                text = empire.data.name + "\u200A" + empire.GetYearNameWithTime() + "\u200A" + empire.countPopulation();
            }
        }
        
        text = text + " | " + empire.countWarriors() + $"{additionNum}/" + empire.countWarriorsMax()+additionNum;
        FixedFaction faction = empire.CoreKingdom.GetRegime().GetDominateFaction();
        if (faction != null)
        {
            var tf = faction.GetAnyTFactionRuns();
            text += $"\n主导: {faction.Name} | " +
                    $"诉求：{(faction.IsAnyTFactionRuns()?tf.type:TemporaryFactionType.无)} " +
                    (faction.IsAnyTFactionRuns()?$"({(int)(tf.progress/60.0f*100.0f)}/100)":"");
        }
        
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
        plateText.nano_object = empire.CoreKingdom;
    }

    public static void showTextTitle(NameplateText plateText, City capital)
    {
        if (ModClass.IS_CLEAR) return;
        if (capital == null) return;
        if (!capital.hasTitle()) return;
        try
        {
            plateText.setupMeta(capital.data, capital.GetTitle().getColor());
            string text = capital.GetTitle().data.name;
            plateText.setText(text, capital.city_center);
            plateText._text_name.fontStyle = FontStyle.Bold;
            plateText._text_name.transform.localPosition = Vector3.zero;
            plateText._text_name.transform.localScale = Vector3.one * 1.5f;
            plateText._text_name.color = Color.white;
            plateText._banner_kingdoms.dead_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.left_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.winner_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.loser_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.background.sprite = capital.GetTitle()?.getElementBackground();
            plateText._banner_kingdoms.icon.sprite = capital.GetTitle()?.getElementIcon();
            var color = capital.GetTitle().kingdomColor.getColorBanner();
            color = new Color(color.r, color.g, color.b, 0.5f);
            plateText._banner_kingdoms.background.color = color;
            plateText._banner_kingdoms.icon.color = color;
            plateText._banner_kingdoms.gameObject.transform.localPosition = Vector3.zero;
            plateText._banner_kingdoms.gameObject.transform.localScale = Vector3.one*1.5f;
            plateText._show_banner_kingdom = true;
            plateText._background_image.enabled = false;
            plateText.nano_object = capital;
            
        }
        catch (Exception e)
        {
            LogService.LogInfo(e.ToString());
        }

    }
}
