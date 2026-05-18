using System.Collections;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
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
            draw_call = DrawEmperor,
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
            draw_call = DrawCityLine,
            render_map = true,
            render_gameplay = true,
            color = new Color(0.4f, 0.4f, 1f, 0.9f)
        });
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "empire_line",
            id_prefab = "p_mapArrow_line",
            base_scale = 0.5f,
            draw_call = DrawKingdomLine,
            render_map = true,
            render_gameplay = true,
            color = new Color(0.4f, 0.4f, 1f, 0.9f)
        });
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "province_line",
            id_prefab = "p_mapArrow_line",
            base_scale = 0.5f,
            draw_call = DrawProvinceLine,
            render_map = true,
            render_gameplay = true,
            color = new Color(0.4f, 0.4f, 1f, 0.9f)
        });
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "capturing_zones",
            id_prefab = "p_mapZone_lines",
            base_scale = 1f,
            draw_call = DrawCapturingZones,
            create_object = delegate(QuantumSpriteAsset _, QuantumSprite pQSprite)
            {
                pQSprite.sprite_renderer.sortingLayerID = SortingLayer.NameToID("EffectsBack");
                pQSprite.sprite_renderer.sortingOrder = 0;
            },
            render_map = true,
            add_camera_zoom_multiplier = false
        });
    }
    private static void DrawCapturingZones(QuantumSpriteAsset pAsset)
    {
        if (!Zones.showKingdomZones() && 
            !Zones.showCityZones() && 
            !Zones.showAllianceZones() && 
            !ShowEmpireZones())
        {
            return;
        }

        // 如果占领模式开启，使用 EmpireCraft 自定义占领区渲染
        if (EmpireCraftWorldLawLibrary.empirecraft_law_switch_occupy_mode.isEnabled())
        {
            DrawEmpireCraftOccupiedZones(pAsset);
            return;
        }

        // 否则使用原版占领区渲染
        DrawVanillaCapturingZones(pAsset);
    }
    private static void DrawVanillaCapturingZones(QuantumSpriteAsset pAsset)
    {
        using ListPool<TileZone> listPool = new ListPool<TileZone>();

        foreach (City city in World.world.cities)
        {
            if (!city.being_captured_by.isRekt() && city.hasZones())
            {
                float num = (float)city.last_visual_capture_ticks / 100f * (float)city.zones.Count;

                if (num > (float)city.zones.Count)
                {
                    num = city.zones.Count;
                }

                CapturingZonesCalculator.getListToDraw(city, (int)num, listPool);

                for (int i = 0; i < ((ICollection)listPool).Count; i++)
                {
                    TileZone tileZone = ((IList<TileZone>)listPool)[i];

                    if (tileZone == null)
                    {
                        continue;
                    }

                    QuantumSprite quantumSprite = QuantumSpriteLibrary.drawQuantumSprite(
                        pAsset, 
                        tileZone.centerTile, 
                        pTileTarget: null
                    );

                    Color pColor = city.being_captured_by.getColor().getColorBorderOut_capture();
                    quantumSprite.setColor(ref pColor);
                }
            }
        }
    }
private static void DrawEmpireCraftOccupiedZones(QuantumSpriteAsset pAsset)
{
    using ListPool<TileZone> listPool = new ListPool<TileZone>();

    foreach (City city in World.world.cities)
    {
        if (city == null)
        {
            continue;
        }

        if (!city.hasZones())
        {
            continue;
        }

        Dictionary<Kingdom, List<TileZone>> occupiedStatus = city.GetOccupiedStatus();

        if (occupiedStatus == null || occupiedStatus.Count == 0)
        {
            continue;
        }

        List<Kingdom> emptyOrInvalidOccupiers = null;

        foreach (var pair in occupiedStatus)
        {
            Kingdom occupier = pair.Key;
            List<TileZone> occupiedZones = pair.Value;

            if (occupier == null || occupier.isRekt())
            {
                if (occupier != null)
                {
                    emptyOrInvalidOccupiers ??= new List<Kingdom>();
                    emptyOrInvalidOccupiers.Add(occupier);
                }
                continue;
            }

            if (occupiedZones == null || occupiedZones.Count == 0)
            {
                emptyOrInvalidOccupiers ??= new List<Kingdom>();
                emptyOrInvalidOccupiers.Add(occupier);
                continue;
            }

            if (city.kingdom == occupier || city.kingdom == null || occupier.isInWarOnSameSide(city.kingdom))
            {
                occupiedZones.Clear();
                emptyOrInvalidOccupiers ??= new List<Kingdom>();
                emptyOrInvalidOccupiers.Add(occupier);
                continue;
            }

            ((IList<TileZone>)listPool).Clear();
            List<TileZone> staleZones = null;

            foreach (TileZone tileZone in occupiedZones)
            {
                if (tileZone == null)
                {
                    staleZones ??= new List<TileZone>();
                    staleZones.Add(tileZone);
                    continue;
                }

                if (tileZone.centerTile == null)
                {
                    staleZones ??= new List<TileZone>();
                    staleZones.Add(tileZone);
                    continue;
                }

                // 防止脏数据：只绘制这个 city 自己的 zone
                if (tileZone.city != city)
                {
                    staleZones ??= new List<TileZone>();
                    staleZones.Add(tileZone);
                    continue;
                }

                Kingdom currentOccupier = city.GetTileZoneOccupier(tileZone);
                if (currentOccupier != occupier)
                {
                    staleZones ??= new List<TileZone>();
                    staleZones.Add(tileZone);
                    continue;
                }

                ((IList<TileZone>)listPool).Add(tileZone);
            }

            if (staleZones != null)
            {
                for (int i = 0; i < staleZones.Count; i++)
                {
                    occupiedZones.Remove(staleZones[i]);
                }
            }

            if (((ICollection)listPool).Count == 0)
            {
                emptyOrInvalidOccupiers ??= new List<Kingdom>();
                emptyOrInvalidOccupiers.Add(occupier);
                continue;
            }

            Color pColor = occupier.getColor().getColorBorderOut_capture();

            for (int i = 0; i < ((ICollection)listPool).Count; i++)
            {
                TileZone tileZone = ((IList<TileZone>)listPool)[i];

                if (tileZone == null)
                {
                    continue;
                }

                if (tileZone.centerTile == null)
                {
                    continue;
                }

                QuantumSprite quantumSprite = QuantumSpriteLibrary.drawQuantumSprite(
                    pAsset,
                    tileZone.centerTile,
                    pTileTarget: null
                );

                quantumSprite.setColor(ref pColor);
            }
        }

        if (emptyOrInvalidOccupiers != null)
        {
            for (int i = 0; i < emptyOrInvalidOccupiers.Count; i++)
            {
                occupiedStatus.Remove(emptyOrInvalidOccupiers[i]);
            }
        }
    }
}
    public static bool ShowEmpireZones(bool pCheckOnlyOption = false)
    {
        return EmpireCraftMetaTypeLibrary.empire.isActive(pCheckOnlyOption);
    }
    private static void DrawEmperor(QuantumSpriteAsset pAsset)
    {
        if (!PlayerConfig.optionBoolEnabled("map_kings_leaders"))
        {
            return;
        }
        int num = 0;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom.isRekt()) continue;
            var regime =  kingdom.GetRegime();
            if (regime == null) continue;
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
                switch (kingdom.GetKingdomType())
                {
                    case KingdomType.ZhouFeudalism_empire:
                    case KingdomType.LvLing_centre:
                        pSprite = king.has_attack_target ?  _LvLing_emperor_sprite_angry: king.hasPlot() ? _LvLing_emperor_sprite_surprised : kingdom.hasEnemies() ? _LvLing_emperor_sprite_normal : _LvLing_emperor_sprite_happy;
                        break;
                    case KingdomType.LvLing_jiedushi:
                    case KingdomType.LvLing_jimizhou:
                    case KingdomType.LvLing_kingdom:
                        pSprite = king.has_attack_target ?  _LvLing_jiedushi_sprite_angry: king.hasPlot() ? _LvLing_jiedushi_sprite_surprised : kingdom.hasEnemies() ? _LvLing_jiedushi_sprite_normal : _LvLing_jiedushi_sprite_happy;
                        break;
                    case KingdomType.LvLing_province:
                        pSprite = king.has_attack_target ?  _LvLing_officer_sprite_angry: king.hasPlot() ? _LvLing_officer_sprite_surprised : kingdom.hasEnemies() ? _LvLing_officer_sprite_normal : _LvLing_officer_sprite_happy;
                        break;
                    default:
                        pSprite = (king.has_attack_target ? QuantumSpriteLibrary._king_sprite_angry : (king.hasPlot() ? QuantumSpriteLibrary._king_sprite_surprised : (kingdom.hasEnemies() ? QuantumSpriteLibrary._king_sprite_normal : QuantumSpriteLibrary._king_sprite_happy))); 
                        break;
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

    private static void DrawCityLine(QuantumSpriteAsset pAsset)
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
        QuantumSpriteLibrary.drawArrowQuantumSprite(pAsset, unity_A.getTile()!.posV, mousePos, ref pColor);
    }


    private static void DrawKingdomLine(QuantumSpriteAsset pAsset)
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


    private static void DrawProvinceLine(QuantumSpriteAsset pAsset)
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
