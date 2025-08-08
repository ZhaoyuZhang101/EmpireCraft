using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftQuantumSpriteLibrary
{
    public static Sprite _emperor_sprite_normal = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_normal");
    public static Sprite _emperor_sprite_angry = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_angry");
    public static Sprite _emperor_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_surprised");
    public static Sprite _emperor_sprite_happy = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_happy");
    public static Sprite _emperor_sprite_sad = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_sad");

    public static Sprite _officer_sprite_normal = SpriteTextureLoader.getSprite("civ/icons/minimap_officer_normal");
    public static Sprite _officer_sprite_angry = SpriteTextureLoader.getSprite("civ/icons/minimap_officer_angry");
    public static Sprite _officer_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/minimap_officer_surprised");
    public static Sprite _officer_sprite_happy = SpriteTextureLoader.getSprite("civ/icons/minimap_officer_happy");
    public static Sprite _officer_sprite_sad = SpriteTextureLoader.getSprite("civ/icons/minimap_officer_sad");

    public static Sprite _jiedushi_sprite_normal = SpriteTextureLoader.getSprite("civ/icons/minimap_jiedushi_normal");
    public static Sprite _jiedushi_sprite_angry = SpriteTextureLoader.getSprite("civ/icons/minimap_jiedushi_angry");
    public static Sprite _jiedushi_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/minimap_jiedushi_surprised");
    public static Sprite _jiedushi_sprite_happy = SpriteTextureLoader.getSprite("civ/icons/minimap_jiedushi_happy");
    public static Sprite _jiedushi_sprite_sad = SpriteTextureLoader.getSprite("civ/icons/minimap_jiedushi_sad");
    public static void init()
    {
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "officers",
            id_prefab = "p_mapSprite",
            render_map = true,
            selected_city_scale = true,
            draw_call = drawOfficers,
            create_object = delegate (QuantumSpriteAsset _, QuantumSprite pQSprite)
            {
                pQSprite.setSharedMat(LibraryMaterials.instance.mat_minis);
            },
            default_amount = 10
        });
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "kings",
            id_prefab = "p_mapSprite",
            base_scale = 0.3f,
            render_map = true,
            selected_city_scale = true,
            draw_call = drawEmperor,
            create_object = delegate (QuantumSpriteAsset _, QuantumSprite pQSprite)
            {
                pQSprite.setSharedMat(LibraryMaterials.instance.mat_minis);
            },
            default_amount = 10
        }); 
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "city_line",
            id_prefab = "p_mapArrow_line",
            base_scale = 0.5f,
            draw_call = drawCityLine,
            render_map = true,
            render_gameplay = true,
            color = new Color(0.4f, 0.4f, 1f, 0.9f)
        });
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "empire_line",
            id_prefab = "p_mapArrow_line",
            base_scale = 0.5f,
            draw_call = drawKingdomLine,
            render_map = true,
            render_gameplay = true,
            color = new Color(0.4f, 0.4f, 1f, 0.9f)
        });
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "province_line",
            id_prefab = "p_mapArrow_line",
            base_scale = 0.5f,
            draw_call = drawProvinceLine,
            render_map = true,
            render_gameplay = true,
            color = new Color(0.4f, 0.4f, 1f, 0.9f)
        });
    }


    private static void drawOfficers(QuantumSpriteAsset pAsset)
    {
        if (!PlayerConfig.optionBoolEnabled("map_kings_leaders"))
        {
            return;
        }
        int num = 0;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            if (num > 2)
            {
                break;
            }
            List<Province> provinces = empire.ProvinceList;
            for (int i= 0; i < provinces.Count; i++)
            {
                Province province = provinces[i];
                if (province.IsTotalVassaled()) continue;
                Actor officer = province.Officer;
                if (!officer.isRekt() && !officer.isInMagnet() && !officer.isKing() && officer.current_zone.visible)
                {
                    Vector3 pPos = officer.current_position;
                    pPos.y -= 3f;
                    Sprite pSprite = (officer.has_attack_target ? _officer_sprite_angry : (officer.hasPlot() ? _officer_sprite_surprised : (empire.empire.hasEnemies() ? _officer_sprite_normal : ((!officer.isHappy()) ? _officer_sprite_sad : _officer_sprite_happy))));
                    if (!pAsset.group_system.is_withing_active_index)
                    {
                        num++;
                    }
                    QuantumSprite quantumSprite = QuantumSpriteLibrary.drawQuantumSprite(pAsset, pPos, null, empire.empire, empire.empire.capital);
                    Sprite icon = DynamicSprites.getIcon(pSprite, empire.empire.getColor());
                    quantumSprite.setSprite(icon);
                }
            }
        }
    }

    private static void drawEmperor(QuantumSpriteAsset pAsset)
    {
        if (!PlayerConfig.optionBoolEnabled("map_kings_leaders"))
        {
            return;
        }
        int num = 0;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (num > 2)
            {
                break;
            }
            Actor king = kingdom.king;
            if (!king.isRekt() && !king.isInMagnet() && king.current_zone.visible)
            {
                Vector3 pPos = king.current_position;
                pPos.y -= 3f;
                Sprite pSprite;
                if (king.isEmperor())
                {
                    pSprite = (king.has_attack_target ? _emperor_sprite_angry : (king.hasPlot() ? _emperor_sprite_surprised : (kingdom.hasEnemies() ? _emperor_sprite_normal : _emperor_sprite_happy)));
                } else if (kingdom.GetCountryLevel()==Enums.countryLevel.countrylevel_2 && kingdom.isInEmpire())
                {
                    pSprite = (king.has_attack_target ? _jiedushi_sprite_angry : (king.hasPlot() ? _jiedushi_sprite_surprised : (kingdom.hasEnemies() ? _jiedushi_sprite_normal : _jiedushi_sprite_happy)));
                }
                else
                {
                    pSprite = (king.has_attack_target ? QuantumSpriteLibrary._king_sprite_angry : (king.hasPlot() ? QuantumSpriteLibrary._king_sprite_surprised : (kingdom.hasEnemies() ? QuantumSpriteLibrary._king_sprite_normal : QuantumSpriteLibrary._king_sprite_happy)));
                }
                
                if (!pAsset.group_system.is_withing_active_index)
                {
                    num++;
                }
                QuantumSprite quantumSprite = QuantumSpriteLibrary.drawQuantumSprite(pAsset, pPos, null, kingdom, king.city);
                Sprite icon = DynamicSprites.getIcon(pSprite, kingdom.getColor());
                quantumSprite.setSprite(icon);

            }
        }

    }

    private static void drawCityLine(QuantumSpriteAsset pAsset)
    {
        if (!InputHelpers.mouseSupported || World.world.isBusyWithUI() || !World.world.isSelectedPower("add_title"))
        {
            return;
        }
        City unity_A = ConfigData.selected_cityA;
        if (unity_A == null)
        {
            return;
        }
        Vector2 mousePos = World.world.getMousePos();
        Color pColor = unity_A.getColor().getColorMain2();
        QuantumSpriteLibrary.drawArrowQuantumSprite(pAsset, unity_A.getTile().posV, mousePos, ref pColor);
    }


    private static void drawKingdomLine(QuantumSpriteAsset pAsset)
    {
        if (!InputHelpers.mouseSupported || World.world.isBusyWithUI() || !World.world.isSelectedPower("create_empire"))
        {
            return;
        }
        Kingdom unity_A = Config.unity_A;
        if (unity_A == null)
        {
            return;
        }
        Vector2 mousePos = World.world.getMousePos();
        foreach (City city in unity_A.cities)
        {
            Color pColor = city.getColor().getColorMain2();
            QuantumSpriteLibrary.drawArrowQuantumSprite(pAsset, city.getTile().posV, mousePos, ref pColor);
        }

    }


    private static void drawProvinceLine(QuantumSpriteAsset pAsset)
    {
        if (!InputHelpers.mouseSupported || World.world.isBusyWithUI() || !World.world.isSelectedPower("create_province"))
        {
            return;
        }
        City unity_A = ConfigData.selected_cityA;
        if (unity_A == null)
        {
            return;
        }
        Vector2 mousePos = World.world.getMousePos();
        Color pColor = unity_A.getColor().getColorMain2();
        QuantumSpriteLibrary.drawArrowQuantumSprite(pAsset, unity_A.getTile().posV, mousePos, ref pColor);

    }
}
