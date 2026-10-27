using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 整层快照(图层瞬切，用户 2026-10-27："图层能够瞬间切换且没有残留，而不是一个国家一个国家的慢慢变")：
// 以前每次换图层都从头分帧算全图两万多块地的颜色，镜头里算齐(至多 4 秒)才翻，没算完的画好一块翻一块，看着一国一国地变。
// 现在换走一个图层时把整张图连同逐格的归属、边框、底色和各块的重画指纹存下(最近 LayerImageMax 层)，再换回来整张拷回，
// 当帧就是完整的图。存下以后真有变动的块照常重画：
//   · 归属、颜色、所属帝国变了的：重画指纹对不上(见 Fingerprint)，下一轮巡检自己重画；
//   · 战线、兼属斜纹、水陆、飞地补色这类在别处要求重画的：存下以后被要求重画过的块(重画记号，见 Stamp)拷回时清掉指纹、照常重画。
// 头一回换到的图层、还没算齐就换走的图层照旧走全图换色(RepaintAll)。归属编号表重排、平滑国界开关、换世界时快照作废
public static partial class SmoothBorders
{
    private const int LayerImageMax = 4;

    private sealed class LayerImage
    {
        public MetaTypeAsset layer;
        public int option, epoch, serial;
        public object world;
        public bool corps;
        public float savedAt, usedAt;
        // 有底色的格存底色(没提亮的)，其余存屏上的样子
        public Color32[] face;
        public ushort[] owner;
        // 1 有底色、2 边框、4 归别人
        public byte[] flags;
        public int[] drawnId, drawnHash, drawnColor;
        public sbyte[] animated;
        public readonly Dictionary<int, (object meta, int color)> lastMeta = new();
    }

    private static readonly List<LayerImage> LayerImages = new();
    // 归属编号表重排、清空的次数：快照里记的归属编号随之作废
    private static int _ownerEpoch;
    // 各块上次被要求重画(重画记号)的序号；快照记下存时的序号，拷回时比它新的块要重画
    private static int[] _resetStamp = Array.Empty<int>();
    private static int _resetSerial;
    // 成批清指纹(换图层、全图重画、原版巡检里的延后与清块、拷回快照本身)不算重画记号：那是眼下这一层的事，别的层的快照照用
    internal static int QuietResets;
    private static bool _layerRestored;

    // 一块地被要求重画(TileZone.resetRenderHelpers 的后缀，见 ZonePaintBudget)
    internal static void Stamp(TileZone zone)
    {
        if (QuietResets > 0 || zone == null) return;
        // 整图画布：记成要重画的块(下一帧画)
        CanvasMark(zone);
        if (LayerImages.Count == 0) return;
        int id = zone.id;
        if (id < 0) return;
        if (id >= _resetStamp.Length)
            Array.Resize(ref _resetStamp, Mathf.Max(id + 1, World.world?.zone_calculator?.zones?.Count ?? 0));
        _resetStamp[id] = ++_resetSerial;
    }

    // 这次换图层是不是从快照拷回的(问过就清)：拷回的不再让全图各块重画(见 ZonePaintBudget.BeforeClearAll)
    internal static bool TakeLayerRestored()
    {
        bool restored = _layerRestored;
        _layerRestored = false;
        return restored;
    }

    private static void DropLayerImages()
    {
        LayerImages.Clear();
        _resetStamp = Array.Empty<int>();
        _resetSerial = 0;
        _layerRestored = false;
    }

    private static LayerImage FindImage(MetaTypeAsset layer, int option)
    {
        foreach (LayerImage image in LayerImages)
            if (ReferenceEquals(image.layer, layer) && image.option == option) return image;
        return null;
    }

    // 换走眼下的图层之前存下整张图。还在算色(镜头里没算齐)的不存：屏上还是上一层的颜色
    private static void SaveLayerImage(ZoneCalculator calc)
    {
        MetaTypeAsset layer = _sweepLayer;
        Color32[] pixels = calc?.pixels;
        List<TileZone> zones = calc?.zones;
        if (!Enabled || layer == null || !Smoothable(layer) || pixels == null || zones == null || pixels.Length != _base.Length ||
            _layerFlip != null && _layerFlip.Computing || CanvasHandles(layer, _sweepOption)) return;
        // 正翻到一半的块按眼下那一面写回不压暗的样子(这些块拷回时照样重画)
        SettleFaces();
        LayerImage image = FindImage(layer, _sweepOption);
        if (image == null)
        {
            if (LayerImages.Count < LayerImageMax) LayerImages.Add(image = new LayerImage());
            else
            {
                image = LayerImages[0];
                foreach (LayerImage other in LayerImages)
                    if (other.usedAt < image.usedAt) image = other;
                image.lastMeta.Clear();
            }
        }
        int count = pixels.Length;
        if (image.face == null || image.face.Length != count)
        {
            image.face = new Color32[count];
            image.owner = new ushort[count];
            image.flags = new byte[count];
        }
        for (int id = 0; id < count; id++)
        {
            bool has = _hasBase[id];
            image.face[id] = has ? _base[id] : pixels[id];
            image.owner[id] = (ushort)_owner[id];
            image.flags[id] = (byte)((has ? 1 : 0) | (_outline[id] ? 2 : 0) | (_foreign[id] ? 4 : 0));
        }
        int zoneCount = zones.Count;
        if (image.drawnId == null || image.drawnId.Length != zoneCount)
        {
            image.drawnId = new int[zoneCount];
            image.drawnHash = new int[zoneCount];
            image.drawnColor = new int[zoneCount];
            image.animated = new sbyte[zoneCount];
        }
        foreach (TileZone zone in zones)
        {
            int id = zone?.id ?? -1;
            if (id < 0 || id >= zoneCount) continue;
            // 正翻到一半的块：屏上不一定是新样子，拷回时重画
            bool flipping = Transitions.ContainsKey(id);
            image.drawnId[id] = flipping ? 0 : zone.last_drawn_id;
            image.drawnHash[id] = flipping ? 0 : zone.last_drawn_hashcode;
            image.drawnColor[id] = flipping ? 0 : zone.last_color_asset_id;
            image.animated[id] = flipping || zone._last_animated == null ? (sbyte)-1 : zone._last_animated.Value ? (sbyte)1 : (sbyte)0;
        }
        image.lastMeta.Clear();
        foreach (KeyValuePair<int, (object meta, int color)> pair in LastMeta) image.lastMeta[pair.Key] = pair.Value;
        image.layer = layer;
        image.option = _sweepOption;
        image.epoch = _ownerEpoch;
        image.serial = _resetSerial;
        image.world = World.world;
        image.corps = CorpsSystem.Enabled;
        image.savedAt = image.usedAt = Time.unscaledTime;
    }

    // 换到存过快照的图层：整张拷回，按眼下的悬停、选中提亮；存下以后被要求重画过的块清掉指纹，下一轮照常重画
    private static bool RestoreLayerImage(ZoneCalculator calc, MetaTypeAsset layer, int option)
    {
        LayerImage image = FindImage(layer, option);
        Color32[] pixels = calc?.pixels;
        List<TileZone> zones = calc?.zones;
        if (image == null || pixels == null || zones == null) return false;
        if (!ReferenceEquals(image.world, World.world) || image.epoch != _ownerEpoch || image.corps != CorpsSystem.Enabled ||
            image.face.Length != pixels.Length || pixels.Length != _base.Length || image.drawnId.Length != zones.Count)
        {
            LayerImages.Remove(image);
            return false;
        }
        long started = global::System.Diagnostics.Stopwatch.GetTimestamp();
        ResetTransitions();
        ZoneFlipOverlay.Hide();
        ZonePaintBudget.ResetLayerPending();
        _sweepLayer = layer;
        _sweepOption = option;
        BeginLift();
        int count = pixels.Length;
        for (int id = 0; id < count; id++)
        {
            byte flags = image.flags[id];
            bool has = (flags & 1) != 0;
            _hasBase[id] = has;
            _outline[id] = (flags & 2) != 0;
            _foreign[id] = (flags & 4) != 0;
            _owner[id] = image.owner[id];
            if (!has)
            {
                pixels[id] = image.face[id];
                continue;
            }
            _base[id] = image.face[id];
            pixels[id] = Lifted(id);
        }
        int stale = 0;
        QuietResets++;
        try
        {
            foreach (TileZone zone in zones)
            {
                int id = zone?.id ?? -1;
                if (id < 0 || id >= image.drawnId.Length) continue;
                if (id < _resetStamp.Length && _resetStamp[id] > image.serial)
                {
                    zone.resetRenderHelpers();
                    stale++;
                    continue;
                }
                zone.last_drawn_id = image.drawnId[id];
                zone.last_drawn_hashcode = image.drawnHash[id];
                zone.last_color_asset_id = image.drawnColor[id];
                zone._last_animated = image.animated[id] < 0 ? null : image.animated[id] == 1;
            }
        }
        finally
        {
            QuietResets--;
        }
        LastMeta.Clear();
        foreach (KeyValuePair<int, (object meta, int color)> pair in image.lastMeta) LastMeta[pair.Key] = pair.Value;
        image.usedAt = Time.unscaledTime;
        calc.setDirty();
        calc.clearAllRedrawTimers();
        _layerRestored = true;
        double ms = (global::System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / global::System.Diagnostics.Stopwatch.Frequency;
        LogService.LogInfo($"[EmpireCraft][全图换色] {layer.id}(换图层)：从 {image.usedAt - image.savedAt:0} 秒前存的整层快照拷回，" +
                           $"用了 {ms:0} 毫秒；存下以后被要求重画的 {stale} 块，其余归属、颜色变了的按指纹重画");
        return true;
    }
}
