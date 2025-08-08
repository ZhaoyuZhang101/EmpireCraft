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
    public static Dictionary<ModMapMode, NameplateAsset> map_modes_nameplates = new Dictionary<ModMapMode, NameplateAsset>();
    public static void init()
    {
        //todo: 自定义层级铭牌的例子，可自行修改或删除
        NameplateAsset asset = new NameplateAsset
        {
            id = "plate_ModLayer",
            path_sprite = "ui/nameplates/nameplate_empire",
            padding_left = 26,
            padding_right = 26,
            padding_top = -2,
            action_main = delegate (NameplateManager pManager, NameplateAsset pAsset)
            {
                foreach (ModLayer pModLayer in ModClass.ModLayer_MANAGER)
                {
                    if (pModLayer != null)
                    {
                        if (pModLayer.CoreCity != null && isWithinCamera(pModLayer.GetCenter()))
                        {
                            NameplateText npt = prepareNext(pManager, pAsset, 37, 12, 39, 11);
                            ShowTextModLayer(npt, pModLayer.CoreCity);
                        }
                    }
                }
                foreach (City city in World.world.cities)
                {
                    if (!city.IsInModLayer() && isWithinCamera(city.city_center))
                    {
                        NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library.plate_city);
                        showTextCity(nameplateText, city);
                    }

                }
            }
        };
        map_modes_nameplates.Add(ModMapMode.ModLayer, asset);
        

        //todo: 覆盖原版铭牌的例子，可删除
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
    }
    public static void showTextCity(NameplateText npt, City pMetaObject)
    {
        npt.setupMeta(pMetaObject.data, pMetaObject.kingdom.getColor());
        string text = pMetaObject.name + "  " + pMetaObject.getPopulationPeople();
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

    public static void ShowTextModLayer(NameplateText plateText, City pcity)
    {
        if (ModClass.IS_CLEAR) return;
        if (pcity == null) return;
        if (!pcity.isAlive()) return;
        ModLayer pModLayer = pcity.GetModLayer();
        if (pModLayer == null) return;
        plateText.setupMeta(pcity.data, pcity.getColor());
        // todo: 修改铭牌的显示的文字
        string text = pModLayer.data.name;

        text = text + "|" + "示例";
        plateText.setText(text, pcity.city_center);
        plateText.showSpecies(pcity.getSpriteIcon());
        plateText._show_banner_kingdom = true;
        plateText._banner_kingdoms.load(pcity.kingdom);
        Clan kingClan = pcity.getRoyalClan();
        if (kingClan != null)
        {
            plateText._show_banner_clan = true;
            plateText._banner_clan.load(kingClan);
        }
        plateText.nano_object = pcity;
    }
}
