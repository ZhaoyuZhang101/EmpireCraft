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
    public static Sprite _LvLing_emperor_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_emperor_normal");
    public static Sprite _LvLing_emperor_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_emperor_angry");
    public static Sprite _LvLing_emperor_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_emperor_surprised");
    public static Sprite _LvLing_emperor_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_emperor_happy");
    public static Sprite _LvLing_emperor_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_emperor_sad");

    public static Sprite _LvLing_officer_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_officer_normal");
    public static Sprite _LvLing_officer_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_officer_angry");
    public static Sprite _LvLing_officer_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_officer_surprised");
    public static Sprite _LvLing_officer_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_officer_happy");
    public static Sprite _LvLing_officer_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_officer_sad");

    public static Sprite _LvLing_jiedushi_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_jiedushi_normal");
    public static Sprite _LvLing_jiedushi_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_jiedushi_angry");
    public static Sprite _LvLing_jiedushi_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_jiedushi_surprised");
    public static Sprite _LvLing_jiedushi_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_jiedushi_happy");
    public static Sprite _LvLing_jiedushi_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_jiedushi_sad");

    public static Sprite _LvLing_king_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_king_normal");
    public static Sprite _LvLing_king_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_king_angry");
    public static Sprite _LvLing_king_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_king_surprised");
    public static Sprite _LvLing_king_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_king_happy");
    public static Sprite _LvLing_king_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_king_sad");

    public static Sprite _Feudalism_Western_emperor_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/Feudalism/Western/minimap_emperor_normal");
    public static Sprite _Feudalism_Western_emperor_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/Western/minimap_emperor_angry");
    public static Sprite _Feudalism_Western_emperor_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/Feudalism/Western/minimap_emperor_surprised");
    public static Sprite _Feudalism_Western_emperor_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/Western/minimap_emperor_happy");
    public static Sprite _Feudalism_Western_emperor_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/Feudalism/Western/minimap_emperor_sad");

    public static Sprite _Feudalism_Western_king_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/Feudalism/Western/minimap_king_normal");
    public static Sprite _Feudalism_Western_king_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/Western/minimap_king_angry");
    public static Sprite _Feudalism_Western_king_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/Feudalism/Western/minimap_king_surprised");
    public static Sprite _Feudalism_Western_king_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/Western/minimap_king_happy");
    public static Sprite _Feudalism_Western_king_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/Feudalism/Western/minimap_king_sad");

    public static Sprite _Feudalism_Eastern_emperor_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/Feudalism/Eastern/minimap_emperor_normal");
    public static Sprite _Feudalism_Eastern_emperor_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/Eastern/minimap_emperor_angry");
    public static Sprite _Feudalism_Eastern_emperor_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/Feudalism/Eastern/minimap_emperor_surprised");
    public static Sprite _Feudalism_Eastern_emperor_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/Eastern/minimap_emperor_happy");
    public static Sprite _Feudalism_Eastern_emperor_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/Feudalism/Eastern/minimap_emperor_sad");

    public static Sprite _Feudalism_Eastern_king_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/Feudalism/Eastern/minimap_king_normal");
    public static Sprite _Feudalism_Eastern_king_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/Eastern/minimap_king_angry");
    public static Sprite _Feudalism_Eastern_king_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/Feudalism/Eastern/minimap_king_surprised");
    public static Sprite _Feudalism_Eastern_king_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/Eastern/minimap_king_happy");
    public static Sprite _Feudalism_Eastern_king_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/Feudalism/Eastern/minimap_king_sad");

    public static Sprite _Feudalism_MidEastern_emperor_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/Feudalism/MidEastern/minimap_emperor_normal");
    public static Sprite _Feudalism_MidEastern_emperor_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/MidEastern/minimap_emperor_angry");
    public static Sprite _Feudalism_MidEastern_emperor_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/Feudalism/MidEastern/minimap_emperor_surprised");
    public static Sprite _Feudalism_MidEastern_emperor_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/MidEastern/minimap_emperor_happy");
    public static Sprite _Feudalism_MidEastern_emperor_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/Feudalism/MidEastern/minimap_emperor_sad");

    public static Sprite _Feudalism_MidEastern_king_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/Feudalism/MidEastern/minimap_king_normal");
    public static Sprite _Feudalism_MidEastern_king_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/MidEastern/minimap_king_angry");
    public static Sprite _Feudalism_MidEastern_king_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/Feudalism/MidEastern/minimap_king_surprised");
    public static Sprite _Feudalism_MidEastern_king_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/Feudalism/MidEastern/minimap_king_happy");
    public static Sprite _Feudalism_MidEastern_king_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/Feudalism/MidEastern/minimap_king_sad");

    public static Sprite _Republic_president_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/Republic/minimap_emperor_normal");
    public static Sprite _Republic_president_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/Republic/minimap_emperor_angry");
    public static Sprite _Republic_president_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/Republic/minimap_emperor_surprised");
    public static Sprite _Republic_president_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/Republic/minimap_emperor_happy");
    public static Sprite _Republic_president_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/Republic/minimap_emperor_sad");

    public static Sprite _Republic_officer_sprite_normal =    SpriteTextureLoader.getSprite("civ/icons/Republic/minimap_king_normal");
    public static Sprite _Republic_officer_sprite_angry =     SpriteTextureLoader.getSprite("civ/icons/Republic/minimap_king_angry");
    public static Sprite _Republic_officer_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/Republic/minimap_king_surprised");
    public static Sprite _Republic_officer_sprite_happy =     SpriteTextureLoader.getSprite("civ/icons/Republic/minimap_king_happy");
    public static Sprite _Republic_officer_sprite_sad =       SpriteTextureLoader.getSprite("civ/icons/Republic/minimap_king_sad");
    public static void init()
    {
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

    private static void drawEmperor(QuantumSpriteAsset pAsset)
    {
        if (!PlayerConfig.optionBoolEnabled("map_kings_leaders"))
        {
            return;
        }
        int num = 0;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom.isRekt()) continue;
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
                if (kingdom.IsEmpire())
                {
                    pSprite = (king.has_attack_target ?  _LvLing_emperor_sprite_angry: (king.hasPlot() ? _LvLing_emperor_sprite_surprised : (kingdom.hasEnemies() ? _LvLing_emperor_sprite_normal : _LvLing_emperor_sprite_happy)));
                }
                else
                {
                    pSprite = (king.has_attack_target ? QuantumSpriteLibrary._king_sprite_angry : (king.hasPlot() ? QuantumSpriteLibrary._king_sprite_surprised : (kingdom.hasEnemies() ? QuantumSpriteLibrary._king_sprite_normal : QuantumSpriteLibrary._king_sprite_happy)));  
                }
                if (!pAsset.group_system.is_within_active_index)
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
        Color pColor = unity_A.getColor().getColorMain();
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
            Color pColor = city.getColor().getColorMain();
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
        Color pColor = unity_A.getColor().getColorMain();
        QuantumSpriteLibrary.drawArrowQuantumSprite(pAsset, unity_A.getTile().posV, mousePos, ref pColor);

    }
}
