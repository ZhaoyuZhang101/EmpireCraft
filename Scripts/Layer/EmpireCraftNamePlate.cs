using db;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.Layer;
public static class EmpireCraftNamePlateLibrary
{
    public static Dictionary<EmpireCraftMapMode, NameplateAsset>  map_modes_nameplates = new Dictionary<EmpireCraftMapMode, NameplateAsset>();
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
                        pManager.prepareNext(AssetManager.nameplates_library.plate_kingdom).showTextKingdom(kingdom);
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
                        pManager.prepareNext(AssetManager.nameplates_library.plate_city).showTextCity(city);
                    }

                }
            },
        };
        map_modes_nameplates.Add(EmpireCraftMapMode.Title, asset2);
    }


    public static bool isWithinCamera(Vector2 pVector)
    {
        return World.world.move_camera.isWithinCameraViewNotPowerBar(pVector);
    }
    public static NameplateText prepareNext(NameplateManager __instance, NameplateAsset pAsset, float left=0, float bottom = 0, float right = 0, float top= 0)
    {
        NameplateText nameplateText;
        if (__instance.active.Count > __instance._usedIndex)
        {
            nameplateText = __instance.active[__instance._usedIndex];
        }
        else
        {
            nameplateText = ((__instance.pool.Count != 0) ? __instance.pool.Pop() : __instance.createNew());
            __instance.active.Add(nameplateText);
        }
        nameplateText.reset();
        nameplateText.setShowing(pVal: true);
        Sprite sprite = SpriteTextureLoader.getSprite(pAsset.path_sprite);
        var text = sprite.texture;
        var rect = sprite.rect;
        var pivot = sprite.pivot;
        float ppu = sprite.pixelsPerUnit;
        var sliced = Sprite.Create(text, rect, pivot, ppu,0, SpriteMeshType.FullRect, new Vector4(left, bottom, right, top));
        var img = nameplateText.background_image;
        img.sprite = sliced;
        img.type = Image.Type.Sliced;
        nameplateText.layout_group.padding.left = pAsset.padding_left;
        nameplateText.layout_group.padding.right = pAsset.padding_right;
        nameplateText.layout_group.padding.top = pAsset.padding_top;
        __instance._usedIndex++;
        return nameplateText;
    }

    public static void showTextEmpire(NameplateText plateText, Kingdom pMetaObject)
    {
        if (pMetaObject == null) return;
        if (!pMetaObject.isAlive()) return;
        Empire empire = pMetaObject.GetEmpire();
        if (empire == null) return;
        plateText.setupMeta(pMetaObject.data, pMetaObject.getColor());
        string text = empire.data.name + "  " + empire.countPopulation();
        if (DebugConfig.isOn(DebugOption.ShowWarriorsCityText))
        {
            text = text + " | " + pMetaObject.countTotalWarriors() + "/" + pMetaObject.countWarriorsMax();
        }
        if (DebugConfig.isOn(DebugOption.ShowCityWeaponsText))
        {
            text = text + " | w" + pMetaObject.countWeapons();
        }
        if (empire.isAllowToMakeYearName())
        {
            if (empire.hasYearName())
            {
                text = empire.data.name+ " " + empire.getYearNameWithTime() + "  " + empire.countPopulation();
            }
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
        plateText.nano_object = empire.empire;
    }

    public static void showTextTitle(NameplateText plateText, City capital)
    {
        if (capital == null) return;
        if (!capital.hasTitle()) return;
        plateText.setupMeta(capital.data, capital.GetTitle().getColor());
        string text = capital.GetTitle().data.name;
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
}
