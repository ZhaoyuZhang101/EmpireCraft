using System;
using System.Collections.Generic;
using System.IO;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 世界地图的高原外缘(用户 2026-11-10)：铺完地表、大河之后，按 WorldMaps/plateau_passes.json 的史实山口(青藏的唐蕃古道、川藏、滇藏、
// 泥婆罗、拉达克、叶城、葱岭；黄土高原的乌鞘岭、湟水、萧关道、陇关、雁门、蒲津、潼关、太行三陉)铺山脊、坡地、开山口(PlateauShaper)。
// 两头都在图外很远的路线不开；导入的地图(经纬度不是地球的)只按自动山口；剧本编辑器刷出来的高原同样围山，没路线经过的自动开口。
// 种子固定：同一范围同一大小每次铺得一样
public static class PlateauPasses
{
    private const int Seed = 20261110;
    private const string FileName = "plateau_passes.json";

    private sealed class Pass
    {
        public string Id, Name;
        public Vector2 From, To;
        // 路线朝高原那头要接进高原(默认)；关中东出潼关这类只是别被坡地堵上的写 "enter": false
        public bool Enter = true;
    }

    private static List<Pass> _passes;

    private static List<Pass> Load()
    {
        if (_passes != null) return _passes;
        _passes = new List<Pass>();
        string path = Path.Combine(WorldMapData.Folder, FileName);
        try
        {
            if (!File.Exists(path)) return _passes;
            JObject root = JObject.Parse(File.ReadAllText(path));
            foreach (JToken item in root["passes"] as JArray ?? new JArray())
            {
                JArray from = item["from"] as JArray, to = item["to"] as JArray;
                if (from == null || to == null || from.Count < 2 || to.Count < 2) continue;
                _passes.Add(new Pass
                {
                    Id = (string)item["id"] ?? "",
                    Name = WorldMapData.Pick((string)item["zh"], (string)item["zh"], (string)item["en"]),
                    From = new Vector2((float)from[0], (float)from[1]),
                    To = new Vector2((float)to[0], (float)to[1]),
                    Enter = (bool?)item["enter"] ?? true
                });
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][高原] 山口表读不出来 {path}: {exception.Message}");
        }
        return _passes;
    }

    public static void Shape(WorldMapFrame frame, Vector2Int size)
    {
        var routes = new List<PlateauShaper.Route>();
        if (!EarthTerrain.Imported && frame != null)
        {
            frame.Bounds(size, out float bottom, out float degrees);
            int w = MapBox.width, h = MapBox.height;
            foreach (Pass pass in Load())
            {
                Vector2Int a = Tile(pass.From), b = Tile(pass.To);
                if (!Inside(a) && !Inside(b)) continue;
                routes.Add(new PlateauShaper.Route(pass.Name, a, b, pass.Enter));
            }

            Vector2Int Tile(Vector2 lonLat) => new(Mathf.RoundToInt((lonLat.x - frame.west) / degrees - 0.5f),
                Mathf.RoundToInt((lonLat.y - bottom) / degrees - 0.5f));

            bool Inside(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < w && p.y < h;
        }
        PlateauShaper.Shape(routes, Seed);
    }
}
