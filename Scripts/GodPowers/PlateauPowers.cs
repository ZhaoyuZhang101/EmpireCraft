using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

// 高原画笔(用户 2026-11-10 "高原地形的神力呢""玩家可以自己放的""放的时候边缘也要自动铺设自然山体")：
//   高寒高原、黄土高原、草原高原三支画笔，照原版「高地」画笔(笔刷大小、按住拖动、音效都同原版)，按钮在文化与营建页营建一组后面
//   (用户 2026-11-10 "没看到高原按钮, 建议加在文化与营建这一栏"；以前插在原版造物页没显示出来，见 MainTab)；
//   每画完一笔(松开)，新画出来的高原边上自动起山脊、外面铺坡地、挑一两处山口(见 PlateauPaintLedger、PlateauShaper.ShapePainted)；
//   接着往外画，旧的边自己退掉；铺好的山脊用别的画笔擦掉就是缺口，以后重算照留。原本就是高原的地方换一种高原不起山。
public static class PlateauPowers
{
    public static readonly (string Id, string Tile, string Icon)[] Brushes =
    {
        ("tile_alpine_plateau", PlateauTerrain.AlpineId, "iconTileAlpinePlateau"),
        ("tile_loess_plateau", PlateauTerrain.LoessId, "iconTileLoessPlateau"),
        ("tile_steppe_plateau", PlateauTerrain.SteppeId, "iconTileSteppePlateau"),
    };

    // 松手后等这么久再算(最后一帧的笔刷先落完)
    private const float SettleSeconds = 0.05f;

    private static readonly HashSet<int> Grown = new();
    private static WorldTile _beforeTile;
    private static bool _beforePlateau;
    private static float _lastAt;

    public static void Init()
    {
        try
        {
            if (AssetManager.powers.get("tile_high_soil") == null || PlateauTerrain.Alpine == null) return;
            foreach ((string id, string tile, string icon) in Brushes)
            {
                if (AssetManager.powers.get(id) != null) continue;
                GodPower power = AssetManager.powers.clone(id, "tile_high_soil");
                power.name = id;
                power.tile_type = tile;
                power.path_icon = icon;
                power.cached_tile_type_asset = AssetManager.tiles.get(tile);
                power.cached_top_tile_type_asset = null;
                // 先记下这格画之前是不是高原，原版画完再记新长出来的高原
                power.click_action = (PowerActionWithID)Delegate.Combine(new PowerActionWithID(BeforeDraw), power.click_action,
                    new PowerActionWithID(AfterDraw));
            }
            PlateauPaintLedger.Writer = (tile, main, top) => MapAction.terraformTile(tile, main, top, TerraformLibrary.destroy_no_flash);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 高原画笔初始化失败: {exception.Message}");
        }
    }

    private static bool BeforeDraw(WorldTile tile, string power)
    {
        _beforeTile = tile;
        _beforePlateau = tile != null && PlateauTerrain.IsPlateau(tile.main_type);
        return true;
    }

    private static bool AfterDraw(WorldTile tile, string power)
    {
        if (tile == null || !PlateauTerrain.IsPlateau(tile.main_type)) return true;
        if (ReferenceEquals(tile, _beforeTile) && _beforePlateau) return true;
        Grown.Add(tile.y * MapBox.width + tile.x);
        _lastAt = Time.unscaledTime;
        return true;
    }

    // 每帧：画过高原、已经松手，就把这一笔的外缘铺上
    public static void Tick()
    {
        if (Grown.Count == 0) return;
        if (World.world == null || SmoothLoader.isLoading())
        {
            Grown.Clear();
            return;
        }
        if (Input.GetMouseButton(0) || Input.touchCount > 0 || Time.unscaledTime - _lastAt < SettleSeconds) return;
        var grown = new List<int>(Grown);
        Grown.Clear();
        try
        {
            PlateauPaintLedger.Finish(grown);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 高原画笔铺外缘失败: {exception}");
        }
    }

    public static void ResetWorldState()
    {
        Grown.Clear();
        _beforeTile = null;
        PlateauPaintLedger.Reset();
    }
}
