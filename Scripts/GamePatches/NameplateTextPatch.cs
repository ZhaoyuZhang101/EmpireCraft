using System;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.GamePatches;

public class NameplateTextPatch:GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(prepare)).Patch(AccessTools.Method(typeof(NameplateText), nameof(NameplateText.prepare)),
            postfix: new HarmonyMethod(GetType(), nameof(prepare)));
    }

    public static void prepare(NameplateText __instance, NameplateAsset pAsset, NanoObject pMeta, float pGlobalScale,
        NameplateRenderingType pNameplateMode, bool pNanoObjectSet, NanoObject pSelectedNanoObject)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pMeta)) return;
        __instance._banner_kingdoms.transform.localScale = Vector3.one;
        __instance._text_name.fontStyle = FontStyle.Normal;
        __instance._text_name.transform.localScale = Vector3.one;
        __instance._text_name.enabled = true;
        __instance._text_name.gameObject.SetActive(true);
        __instance._background_image.transform.localPosition = Vector3.zero;
        __instance._background_image.transform.localScale = Vector3.one;
        __instance._background_image.type = Image.Type.Sliced;
        var outline = __instance._text_name.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = false;
        }
        __instance.setShowing(true);
        if (pAsset != null)
        {
            switch (pAsset.map_mode)
            {
                case MetaType.Kingdom:
                    __instance._show_banner_kingdom = true;
                    __instance._banner_kingdoms.enabled = true;
                    __instance._banner_kingdoms.gameObject.SetActive(true);
                    try
                    {
                        __instance._banner_kingdoms.load(pMeta);
                    }
                    catch
                    {
                        // ignored
                    }
                    break;
                case MetaType.City:
                    City city = pMeta as City;
                    if (city == null || city.isRekt())
                    {
                        __instance.setShowing(false);
                        break;
                    }
                    __instance._show_banner_city = true;
                    __instance._banner_city.enabled = true;
                    __instance._banner_city.gameObject.SetActive(true);
                    __instance._banner_city.load(city);
                    break;
                case MetaTypeExtension.KingdomTitle:
                    City capital = pMeta as City;
                    if (capital == null && pMeta is KingdomTitle title)
                    {
                        capital = title.title_capital;
                    }
                    if (capital == null || capital.isRekt() || !capital.hasTitle())
                    {
                        __instance.setShowing(false);
                        break;
                    }

                    KingdomTitle kingdomTitle = capital.GetTitle();
                    if (kingdomTitle == null)
                    {
                        __instance.setShowing(false);
                        break;
                    }

                    __instance.setupMeta(capital.data, kingdomTitle.getColor());
                    __instance._text_name.fontStyle = FontStyle.Bold;
                    __instance._text_name.transform.localPosition = Vector3.zero;
                    __instance._text_name.transform.localScale = Vector3.one * 1.5f;
                    __instance._text_name.color = Color.white;
            
                    // 给文字加蓝色边框（描边）
                    if (outline != null)
                    {
                        outline.enabled = false;
                    }
            
                    __instance._banner_kingdoms.dead_image.gameObject.SetActive(value: false);
                    __instance._banner_kingdoms.left_image.gameObject.SetActive(value: false);
                    __instance._banner_kingdoms.winner_image.gameObject.SetActive(value: false);
                    __instance._banner_kingdoms.loser_image.gameObject.SetActive(value: false);
                    __instance._show_banner_city = false;
                    __instance._show_banner_clan = false;
                    if (EmpireCraftMetaTypeLibrary.kingdomTitle.getZoneOptionState() == 0)
                    {
                        __instance._show_banner_kingdom = false;
                    }
                    else
                    {
                        __instance._show_banner_kingdom = true;
                    }
                    __instance._banner_kingdoms.background.sprite = kingdomTitle.getElementBackground();
                    __instance._banner_kingdoms.icon.sprite = kingdomTitle.getElementIcon();
                    var color = kingdomTitle.kingdomColor.getColorBanner();
                    color = new Color(color.r, color.g, color.b, 0.5f);
                    __instance._banner_kingdoms.background.color = color;
                    __instance._banner_kingdoms.icon.color = color;
                    __instance._banner_kingdoms.gameObject.transform.localPosition = Vector3.zero;
                    __instance._banner_kingdoms.gameObject.transform.localScale = Vector3.one*1.5f;
                    __instance._background_image.enabled = false;
                    __instance._show_banner_culture = false;
                    break;
                case MetaTypeExtension.Empire:
                    __instance._show_banner_city = false;
                    __instance._show_banner_clan = false;
                    __instance._banner_kingdoms.dead_image.gameObject.SetActive(value: false);
                    __instance._banner_kingdoms.left_image.gameObject.SetActive(value: false);
                    __instance._banner_kingdoms.winner_image.gameObject.SetActive(value: false);
                    __instance._banner_kingdoms.loser_image.gameObject.SetActive(value: false);
                    __instance._show_banner_kingdom = false;
                    __instance._show_banner_culture = false;
                    __instance._background_image.enabled = true;
                    break;
            }
        }
    }
}
