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
    public static Sprite _sample_sprite_normal = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_normal");
    public static Sprite _sample_sprite_angry = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_angry");
    public static Sprite _sample_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_surprised");
    public static Sprite _sample_sprite_happy = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_happy");
    public static Sprite _sample_sprite_sad = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_sad");
    public static void init()
    {
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "leaders",
            id_prefab = "p_mapSprite",
            render_map = true,
            selected_city_scale = true,
            draw_call = drawModLayerLeader,
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
            draw_call = drawKing,
            create_object = delegate (QuantumSpriteAsset _, QuantumSprite pQSprite)
            {
                pQSprite.setSharedMat(LibraryMaterials.instance.mat_minis);
            },
            default_amount = 10
        }); 
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "ModLayer_line",
            id_prefab = "p_mapArrow_line",
            base_scale = 0.5f,
            draw_call = drawModLayerLine,
            render_map = true,
            render_gameplay = true,
            color = new Color(0.4f, 0.4f, 1f, 0.9f)
        });
    }


    private static void drawModLayerLeader(QuantumSpriteAsset pAsset)
    {
        if (!PlayerConfig.optionBoolEnabled("map_kings_leaders"))
        {
            return;
        }
        int num = 0;
        foreach (ModLayer pModLayer in ModClass.ModLayer_MANAGER)
        {
            if (num > 2)
            {
                break;
            }
            Actor pModLayerLeader = pModLayer.CoreCity.leader;
            if (!pModLayerLeader.isRekt() && !pModLayerLeader.isInMagnet() && !pModLayerLeader.isKing() && pModLayerLeader.current_zone.visible)
            {
                Vector3 pPos = pModLayerLeader.current_position;
                pPos.y -= 3f;
                Sprite pSprite = pModLayerLeader.has_attack_target 
                    ? _sample_sprite_angry : pModLayerLeader.hasPlot() 
                        ? _sample_sprite_surprised : pModLayerLeader.kingdom.hasEnemies() 
                            ? _sample_sprite_normal : (!pModLayerLeader.isHappy()) 
                                ? _sample_sprite_sad : _sample_sprite_happy;
                if (!pAsset.group_system.is_withing_active_index)
                {
                    num++;
                }
                QuantumSprite quantumSprite = QuantumSpriteLibrary.drawQuantumSprite(pAsset, pPos, null, pModLayer.CoreCity.kingdom, pModLayer.CoreCity);
                Sprite icon = DynamicSprites.getIcon(pSprite, pModLayer.CoreCity.kingdom.getColor());
                quantumSprite.setSprite(icon);
            }
        }
    }

    private static void drawKing(QuantumSpriteAsset pAsset)
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

                pSprite = (king.has_attack_target 
                    ? QuantumSpriteLibrary._king_sprite_angry : (king.hasPlot() 
                    ? QuantumSpriteLibrary._king_sprite_surprised : (kingdom.hasEnemies() 
                        ? QuantumSpriteLibrary._king_sprite_normal : QuantumSpriteLibrary._king_sprite_happy)));
                
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


    private static void drawModLayerLine(QuantumSpriteAsset pAsset)
    {
        if (!InputHelpers.mouseSupported || World.world.isBusyWithUI() || !World.world.isSelectedPower("create_ModLayer"))
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
        QuantumSpriteLibrary.drawArrowQuantumSprite(pAsset, unity_A.getTile()!.posV, mousePos, ref pColor);

    }
}
