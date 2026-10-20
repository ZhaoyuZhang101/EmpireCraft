using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GodPowers;
using UnityEngine;
using Rand = System.Random;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战声(仿 EU4)：镜头拉近到正在交战的地方时，听得到那里的厮杀声。音源与 FMOD 通道见 BattleSoundBank(通道是装箱的句柄，没有为 null)。
//   交战处：陆战取 FrontSystem.Battles(本月、上月打过的城，交战标记画在哪就从哪出声)，海战取 NavySystem.Battles；
//   音色：按在那座城攻守的各军兵种(CorpsArmSystem)分时代——步兵、弓兵、车兵、骑兵算冷兵器，火枪兵算火器，
//     步枪兵、装甲车、坦克、飞机算近代；炮兵同场有近代兵种时是近代炮击，不然是前装炮；民兵看本国科技到了哪个时代；
//     找不到在那座城的军(原版小人打的仗等)就按攻方文化的科技定时代。
//     海战：舰种到了远洋帆舰是舰炮(帆舰多舷侧齐射)，楼船时代是箭雨与接舷砍杀；环境声都是海浪。
//   环境声：每个时代一段循环，同时至多响两个时代(取最响的两个)，多处交战同一时代并成一路(处数越多越响，封顶)；
//   单发：按各处音量 × 那处的激烈程度随机挑一处，再按那处的兵种配比挑种类、挑变体(同一段不连放)；
//     每秒至多 3 发、同时至多 6 发、间隔至少 0.18 秒；随机音高 ±6%。
//   音量 = 镜头远近(拉远到看得见大半张图就不响) × 离屏幕中心的距离(出了屏幕边很快没声) × 交战规模与新近程度；
//     声像按左右偏移；镜头越远整组越闷(低通)。
//   暂停(按原版口径，开着窗口也算暂停)与关掉开关时 0.4 秒淡出、停住，恢复时淡入。
//   总音量乘原版的音效音量(与主音量)；显示栏"战声"开关(玩家设置，默认开，见 SwitchBattleSoundButton)。
public static class BattleSound
{
    // 战声整体电平(相对原版音效)
    private const float Level = 0.85f;
    private const float FadeSeconds = 0.4f;
    // 环境声音量跟到目标值要多久(秒)
    private const float AmbientFollow = 0.8f;
    private const float ScanSeconds = 0.5f;
    private const int MaxShots = 6, MaxShotsPerSecond = 3, MaxSpots = 4;
    private const float MinShotGap = 0.18f;
    // 镜头正交尺寸(半个屏幕高多少格)：近于 NearOrtho 满音量，远于 FarOrtho 不响；小地图按最大缩放的比例收紧
    private const float NearOrtho = 45f, FarOrtho = 150f;
    // 离屏幕中心多远开始变轻、多远没声(1 = 屏幕边)
    private const float EdgeStart = 0.35f, EdgeSilent = 1.3f;
    private const float AudibleGain = 0.01f;

    private const int Cold = 0, Fire = 1, Modern = 2, Naval = 3;
    private static readonly string[] EraNames = { "cold", "fire", "modern", "naval" };

    // 单发种类(文件名的「时代_种类」)与音量(与 Tools/SoundForge/battle_forge.py 的 GAIN 一致)
    private static readonly string[] KindFiles =
    {
        "cold_clash", "cold_warcry", "cold_arrows", "cold_hooves", "fire_volley", "fire_cannon", "modern_rifle",
        "modern_mg", "modern_shell", "naval_gun", "naval_broadside", "naval_splash"
    };
    // 箭雨、马蹄是合成的，压低一点，别抢刀剑与喊杀(录音素材)
    private static readonly float[] KindGain = { 0.55f, 0.5f, 0.4f, 0.35f, 0.7f, 0.8f, 0.65f, 0.65f, 0.8f, 0.8f, 0.8f, 0.45f };
    private const float AmbientGain = 0.5f;
    private const int KClash = 0, KWarcry = 1, KArrows = 2, KHooves = 3, KVolley = 4, KCannon = 5, KRifle = 6, KMg = 7,
        KShell = 8, KGun = 9, KBroadside = 10, KSplash = 11;

    // 一处交战：位置、规模(0~1)、环境声时代、激烈程度(满音量时每秒几发单发)、各种类权重
    private sealed class Spot
    {
        public Vector3 Pos;
        public float Weight, Rate;
        public int Ambient;
        public readonly float[] Palette = new float[12];
    }

    private sealed class AmbientVoice
    {
        public object Channel;
        public float Volume, Target, Pan, TargetPan;
    }

    // 在一座城攻守的各军按兵种合计的兵力
    private sealed class Tally
    {
        public float Cold, Fire, Modern, Artillery, Archers, Riders, Armor, Total;

        public void Clear() => Cold = Fire = Modern = Artillery = Archers = Riders = Armor = Total = 0f;
    }

    private static readonly List<Spot> Spots = new();
    private static readonly List<Spot> SpotPool = new();
    private static readonly Dictionary<long, Tally> Tallies = new();
    private static readonly List<Tally> TallyPool = new();
    private static readonly Dictionary<long, int> KingdomEras = new();
    private static readonly AmbientVoice[] Ambients = { new(), new(), new(), new() };
    private static readonly List<object> Shots = new();
    private static readonly Queue<float> RecentShots = new();
    private static readonly int ArmCount = Enum.GetValues(typeof(CorpsArmSystem.Arm)).Length;
    private static readonly Rand Rng = new();

    // 本帧听得见的几处(按音量从大到小)
    private static readonly Spot[] Heard = new Spot[MaxSpots];
    private static readonly float[] HeardGain = new float[MaxSpots];
    private static readonly float[] HeardPan = new float[MaxSpots];
    private static int _heardCount;

    private static readonly float[] EraTarget = new float[4], EraQuiet = new float[4], EraPanSum = new float[4],
        EraPanWeight = new float[4];

    private static GameObject _host;
    private static float _fade, _scanAt = -10f, _nextShotAt;
    private static string _lastShot;
    private static bool _active, _announced, _crashed;

    internal static void EnsureHost()
    {
        if (_host != null) return;
        _host = new GameObject("EmpireCraftBattleSound", typeof(BattleSoundHost));
        UnityEngine.Object.DontDestroyOnLoad(_host);
    }

    internal static void Tick()
    {
        if (_crashed) return;
        try
        {
            Update(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
        }
        catch (Exception error)
        {
            // 出错就整局停掉，免得每帧刷日志
            _crashed = true;
            BattleSoundBank.Report("tick", $"更新出错，本局不再播放：{error}");
            try
            {
                BattleSoundBank.StopAll();
            }
            catch (Exception)
            {
                // 已经在报错路径上
            }
        }
    }

    private static void Update(float dt)
    {
        bool want = SwitchBattleSoundButton.On && MusicBox.sounds_on && World.world != null && Config.game_loaded &&
                    !SmoothLoader.isLoading() && World.world.camera != null;
        if (!want)
        {
            if (_active) FadeOutAndStop(dt);
            return;
        }
        if (!BattleSoundBank.EnsureLoaded()) return;
        _active = true;
        bool paused = World.world.isPaused();
        _fade = Mathf.MoveTowards(_fade, paused ? 0f : 1f, dt / FadeSeconds);

        float now = Time.unscaledTime;
        if (now - _scanAt >= ScanSeconds || now < _scanAt)
        {
            _scanAt = now;
            Scan();
        }

        Camera camera = World.world.camera;
        float zoom = ZoomGain(camera, out float farness);
        Listen(camera, zoom);

        float sfx = Mathf.Clamp01(PlayerConfig.getIntValue("volume_sound_effects") / 100f);
        float volume = sfx * BattleSoundBank.MasterFactor() * Level * _fade;
        BattleSoundBank.SetGroup(volume, 20000f * Mathf.Pow(0.12f, farness), paused && _fade <= 0f);
        if (paused && _fade <= 0f) return;

        UpdateAmbients(dt);
        if (!paused) UpdateShots(now);
    }

    // 关开关、退出世界、读档时：淡出后全部停掉
    private static void FadeOutAndStop(float dt)
    {
        _fade = Mathf.MoveTowards(_fade, 0f, dt / FadeSeconds);
        float sfx = PlayerConfig.dict.ContainsKey("volume_sound_effects")
            ? Mathf.Clamp01(PlayerConfig.getIntValue("volume_sound_effects") / 100f) : 1f;
        BattleSoundBank.SetGroup(sfx * BattleSoundBank.MasterFactor() * Level * _fade, 20000f, false);
        if (_fade > 0f) return;
        BattleSoundBank.StopAll();
        foreach (AmbientVoice voice in Ambients)
        {
            voice.Channel = null;
            voice.Volume = voice.Target = 0f;
        }
        Shots.Clear();
        RecentShots.Clear();
        Spots.Clear();
        _heardCount = 0;
        _scanAt = -10f;
        _active = false;
    }

    #region 交战处

    private static void Scan()
    {
        foreach (Spot spot in Spots) SpotPool.Add(spot);
        Spots.Clear();
        foreach (Tally tally in Tallies.Values) TallyPool.Add(tally);
        Tallies.Clear();
        KingdomEras.Clear();

        if (FrontSystem.Battles.Count > 0)
        {
            foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All())
            {
                if (corps.sector_city_id < 0 || corps.task != "attack" && corps.task != "defend") continue;
                if (!FrontSystem.Battles.ContainsKey(corps.sector_city_id)) continue;
                if (!Tallies.TryGetValue(corps.sector_city_id, out Tally tally))
                {
                    tally = TakeTally();
                    Tallies[corps.sector_city_id] = tally;
                }
                Count(tally, kingdom, corps);
            }
            foreach (FrontSystem.BattleView battle in FrontSystem.Battles.Values)
            {
                TileZone zone = CorpsSystem.ZoneById(battle.zone_id);
                if (zone?.centerTile == null) continue;
                Spot spot = TakeSpot(zone.centerTile.posV);
                Land(spot, battle);
                Spots.Add(spot);
            }
        }
        foreach (NavySystem.BattleView battle in NavySystem.Battles.Values)
        {
            TileZone zone = CorpsSystem.ZoneById(battle.zone_id);
            if (zone?.centerTile == null) continue;
            Spot spot = TakeSpot(zone.centerTile.posV);
            Sea(spot, battle);
            Spots.Add(spot);
        }
    }

    private static Spot TakeSpot(Vector3 pos)
    {
        Spot spot;
        if (SpotPool.Count > 0)
        {
            spot = SpotPool[SpotPool.Count - 1];
            SpotPool.RemoveAt(SpotPool.Count - 1);
        }
        else spot = new Spot();
        spot.Pos = pos;
        Array.Clear(spot.Palette, 0, spot.Palette.Length);
        return spot;
    }

    private static Tally TakeTally()
    {
        Tally tally;
        if (TallyPool.Count > 0)
        {
            tally = TallyPool[TallyPool.Count - 1];
            TallyPool.RemoveAt(TallyPool.Count - 1);
        }
        else tally = new Tally();
        tally.Clear();
        return tally;
    }

    // 只读兵种，不替还没定兵种的军定下来(那是 CorpsArmSystem.Of 的事)
    private static void Count(Tally tally, Kingdom kingdom, CorpsData corps)
    {
        float w = Mathf.Max(1f, corps.strength);
        tally.Total += w;
        if (corps.militia || corps.arm < 0 || corps.arm >= ArmCount)
        {
            // 民兵(与还没定兵种的军)：拿本国科技到了哪个时代的家伙
            switch (TechEra(kingdom))
            {
                case Modern: tally.Modern += w; break;
                case Fire: tally.Fire += w; break;
                default: tally.Cold += w; break;
            }
            return;
        }
        switch ((CorpsArmSystem.Arm)corps.arm)
        {
            case CorpsArmSystem.Arm.Archer:
                tally.Cold += w;
                tally.Archers += w;
                break;
            case CorpsArmSystem.Arm.Chariot:
            case CorpsArmSystem.Arm.Cavalry:
                tally.Cold += w;
                tally.Riders += w;
                break;
            case CorpsArmSystem.Arm.Musket:
                tally.Fire += w;
                break;
            case CorpsArmSystem.Arm.Artillery:
            case CorpsArmSystem.Arm.HeavyArtillery:
                tally.Artillery += w;
                break;
            case CorpsArmSystem.Arm.Rifle:
                tally.Modern += w;
                break;
            case CorpsArmSystem.Arm.Mechanized:
            case CorpsArmSystem.Arm.Tank:
                tally.Modern += w;
                tally.Armor += w;
                break;
            case CorpsArmSystem.Arm.Helicopter:
            case CorpsArmSystem.Arm.Fighter:
            case CorpsArmSystem.Arm.Bomber:
                tally.Modern += w;
                tally.Armor += w * 0.5f;
                break;
            default:
                tally.Cold += w;
                break;
        }
    }

    // 本国科技到了哪个时代：膛线 → 近代，火绳枪或火药 → 火器，否则冷兵器(本次扫描内按国缓存)
    private static int TechEra(Kingdom kingdom)
    {
        if (kingdom?.data == null) return Cold;
        if (KingdomEras.TryGetValue(kingdom.id, out int era)) return era;
        era = Cold;
        string culture = TechnologySystem.GetCultureOf(kingdom);
        if (!string.IsNullOrEmpty(culture) && TechnologySystem.IsActiveFor(culture))
        {
            if (TechnologySystem.HasTech(culture, "rifling")) era = Modern;
            else if (TechnologySystem.HasTech(culture, "matchlock") || TechnologySystem.HasTech(culture, "gunpowder")) era = Fire;
        }
        KingdomEras[kingdom.id] = era;
        return era;
    }

    private static Kingdom KingdomOf(long id) => id < 0 ? null : World.world?.kingdoms?.get(id);

    private static void Land(Spot spot, FrontSystem.BattleView battle)
    {
        float cold, fire, modern, art, archers, riders, armor, size;
        if (Tallies.TryGetValue(battle.city_id, out Tally tally) && tally.Total > 0f)
        {
            cold = tally.Cold;
            fire = tally.Fire;
            modern = tally.Modern;
            art = tally.Artillery;
            archers = cold > 0f ? tally.Archers / cold : 0f;
            riders = cold > 0f ? tally.Riders / cold : 0f;
            armor = modern > 0f ? tally.Armor / modern : 0f;
            // 兵力 3 千约 0.7、3 万约 0.85、30 万满
            size = Mathf.Clamp(0.55f + 0.15f * Mathf.Log10(1f + tally.Total / 300f), 0.55f, 1f);
        }
        else
        {
            // 没找到在那座城攻守的军：按攻方(没有就守方)的科技定时代，兵种按常见配比
            int era = TechEra(KingdomOf(battle.attacker_id) ?? KingdomOf(battle.defender_id));
            cold = era == Cold ? 1f : 0f;
            fire = era == Fire ? 0.88f : 0f;
            modern = era == Modern ? 0.88f : 0f;
            art = era == Cold ? 0f : 0.12f;
            archers = 0.25f;
            riders = 0.15f;
            armor = 0.1f;
            size = 0.75f;
        }
        // 炮兵归哪个时代：同场有近代兵种就是近代炮击，不然是前装炮
        float artFire = modern > 0f ? 0f : art, artModern = modern > 0f ? art : 0f;
        float total = Mathf.Max(1e-4f, cold + fire + modern + art);
        float c = cold / total, f = (fire + artFire) / total, m = (modern + artModern) / total;
        float fireArt = fire + artFire > 0f ? artFire / (fire + artFire) : 0f;
        float modernArt = modern + artModern > 0f ? artModern / (modern + artModern) : 0f;

        float[] p = spot.Palette;
        p[KClash] = c * 5f + f * 1f;
        p[KWarcry] = c * 1.2f + f * 0.8f;
        p[KArrows] = c * (0.4f + 4f * archers);
        p[KHooves] = c * 4f * riders;
        p[KVolley] = f * 5f;
        p[KCannon] = f * (0.8f + 5f * fireArt);
        p[KRifle] = m * 4f;
        p[KMg] = m * 2.5f;
        p[KShell] = m * (1.2f + 4f * modernArt + 2f * armor);
        spot.Rate = c * 1.5f + f * 1.2f + m * 1.6f;
        spot.Ambient = m >= 0.25f ? Modern : f >= 0.25f ? Fire : Cold;
        // 本月还在打的满音量，上个月打过、这个月还没再结算的轻一些
        float recency = Date.getMonthsSince(battle.time) <= 0 ? 1f : 0.7f;
        spot.Weight = size * recency;
    }

    private static void Sea(Spot spot, NavySystem.BattleView battle)
    {
        float tier = Mathf.Max(NavySystem.Tier(KingdomOf(battle.attacker_id)), NavySystem.Tier(KingdomOf(battle.defender_id)));
        float[] p = spot.Palette;
        if (tier >= 1.5f)
        {
            // 帆舰时代多舷侧齐射，蒸汽铁甲以后多是单炮、双联装
            p[KGun] = 4f;
            p[KBroadside] = tier < 2.5f ? 2.5f : 0.6f;
            p[KSplash] = 1.5f;
            spot.Rate = 1f;
        }
        else
        {
            // 楼船：箭雨、接舷砍杀
            p[KArrows] = 3f;
            p[KClash] = 3f;
            p[KWarcry] = 1f;
            p[KSplash] = 1.5f;
            spot.Rate = 1.2f;
        }
        spot.Ambient = Naval;
        float size = Mathf.Clamp(0.6f + 0.4f * (battle.attacker_ships + battle.defender_ships) / 20f, 0.6f, 1f);
        spot.Weight = size * (Date.getMonthsSince(battle.time) <= 0 ? 1f : 0.7f);
    }

    #endregion

    #region 听

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    // 镜头远近：0 不响、1 满；farness 0 近、1 远(给低通用)
    private static float ZoomGain(Camera camera, out float farness)
    {
        float ortho = camera.orthographicSize;
        float max = World.world.move_camera != null ? World.world.move_camera.orthographic_size_max : 130f;
        float far = Mathf.Min(FarOrtho, Mathf.Max(30f, max * 0.6f));
        float near = Mathf.Min(NearOrtho, far * 0.4f);
        farness = Mathf.InverseLerp(near, far, ortho);
        return 1f - Smooth(farness);
    }

    // 算出本帧听得见的几处(最响的 MaxSpots 处)
    private static void Listen(Camera camera, float zoom)
    {
        _heardCount = 0;
        if (zoom <= AudibleGain || Spots.Count == 0) return;
        Vector3 cam = camera.transform.position;
        float ortho = Mathf.Max(1f, camera.orthographicSize);
        float aspect = Mathf.Max(0.5f, camera.aspect);
        foreach (Spot spot in Spots)
        {
            float dx = (spot.Pos.x - cam.x) / (ortho * aspect), dy = (spot.Pos.y - cam.y) / ortho;
            float edge = 1f - Smooth(Mathf.InverseLerp(EdgeStart, EdgeSilent, Mathf.Sqrt(dx * dx + dy * dy)));
            float gain = zoom * edge * spot.Weight;
            if (gain <= AudibleGain) continue;
            // 插进从大到小的前 MaxSpots 名
            int at = _heardCount;
            while (at > 0 && HeardGain[at - 1] < gain) at--;
            if (at >= MaxSpots) continue;
            int last = Mathf.Min(_heardCount, MaxSpots - 1);
            for (int i = last; i > at; i--)
            {
                Heard[i] = Heard[i - 1];
                HeardGain[i] = HeardGain[i - 1];
                HeardPan[i] = HeardPan[i - 1];
            }
            Heard[at] = spot;
            HeardGain[at] = gain;
            HeardPan[at] = Mathf.Clamp(dx, -1f, 1f) * 0.65f;
            if (_heardCount < MaxSpots) _heardCount++;
        }
    }

    #endregion

    #region 环境声

    private static void UpdateAmbients(float dt)
    {
        // 同一时代的几处并成一路：1 - Π(1 - 各处音量)，处数越多越响、封顶 1
        for (int e = 0; e < 4; e++)
        {
            EraQuiet[e] = 1f;
            EraPanSum[e] = EraPanWeight[e] = 0f;
        }
        for (int i = 0; i < _heardCount; i++)
        {
            int e = Heard[i].Ambient;
            EraQuiet[e] *= 1f - Mathf.Clamp01(HeardGain[i]);
            EraPanSum[e] += HeardPan[i] * HeardGain[i];
            EraPanWeight[e] += HeardGain[i];
        }
        for (int e = 0; e < 4; e++) EraTarget[e] = 1f - EraQuiet[e];
        // 至多两个时代同时响
        int first = -1, second = -1;
        for (int e = 0; e < 4; e++)
        {
            if (EraTarget[e] <= 0f) continue;
            if (first < 0 || EraTarget[e] > EraTarget[first])
            {
                second = first;
                first = e;
            }
            else if (second < 0 || EraTarget[e] > EraTarget[second]) second = e;
        }
        for (int e = 0; e < 4; e++)
        {
            AmbientVoice voice = Ambients[e];
            bool keep = (e == first || e == second) && BattleSoundBank.HasAmbient(EraNames[e]);
            voice.Target = keep ? EraTarget[e] * AmbientGain : 0f;
            if (EraPanWeight[e] > 0f) voice.TargetPan = EraPanSum[e] / EraPanWeight[e];
            voice.Volume = Mathf.MoveTowards(voice.Volume, voice.Target, dt / AmbientFollow * AmbientGain);
            voice.Pan = Mathf.MoveTowards(voice.Pan, voice.TargetPan, dt * 1.5f);
            if (voice.Volume > 0.0005f)
            {
                if (!BattleSoundBank.Playing(voice.Channel))
                {
                    // 从循环里随机一处开始，免得每次都是同一句开头
                    voice.Channel = BattleSoundBank.Play(BattleSoundBank.Ambient(EraNames[e]), voice.Volume, voice.Pan, 1f,
                        (float)Rng.NextDouble());
                    if (!_announced && voice.Channel != null)
                    {
                        _announced = true;
                        NeoModLoader.services.LogService.LogInfo(
                            $"[EmpireCraft][战声] 开始出声：{Spots.Count} 处交战，听得见 {_heardCount} 处，环境声 {EraNames[e]}");
                    }
                }
                else BattleSoundBank.SetVolume(voice.Channel, voice.Volume, voice.Pan);
            }
            else if (voice.Channel != null) BattleSoundBank.Stop(ref voice.Channel);
        }
    }

    #endregion

    #region 单发

    private static void UpdateShots(float now)
    {
        for (int i = Shots.Count - 1; i >= 0; i--)
            if (!BattleSoundBank.Playing(Shots[i])) Shots.RemoveAt(i);
        while (RecentShots.Count > 0 && now - RecentShots.Peek() > 1f) RecentShots.Dequeue();
        if (now < _nextShotAt) return;

        float total = 0f;
        for (int i = 0; i < _heardCount; i++) total += HeardGain[i] * Heard[i].Rate;
        if (total <= 0.01f)
        {
            _nextShotAt = now + 0.25f;
            return;
        }
        // 下一发：泊松间隔(按各处合计的激烈程度)，不短于 MinShotGap
        _nextShotAt = now + Mathf.Max(MinShotGap, -Mathf.Log(1f - (float)Rng.NextDouble() * 0.999f) / total);
        if (Shots.Count >= MaxShots || RecentShots.Count >= MaxShotsPerSecond) return;

        float roll = (float)Rng.NextDouble() * total;
        int pick = _heardCount - 1;
        for (int i = 0; i < _heardCount; i++)
        {
            roll -= HeardGain[i] * Heard[i].Rate;
            if (roll <= 0f)
            {
                pick = i;
                break;
            }
        }
        Spot spot = Heard[pick];
        int kind = PickKind(spot);
        if (kind < 0) return;
        string key = KindFiles[kind];
        int variants = BattleSoundBank.Variants(key);
        int index = Rng.Next(variants);
        if (variants > 1 && key + index == _lastShot) index = (index + 1 + Rng.Next(variants - 1)) % variants;
        float volume = HeardGain[pick] * KindGain[kind] * (0.75f + 0.25f * (float)Rng.NextDouble());
        float pitch = 0.94f + 0.12f * (float)Rng.NextDouble();
        object channel = BattleSoundBank.Play(BattleSoundBank.Shot(key, index), volume, HeardPan[pick], pitch);
        if (channel == null) return;
        Shots.Add(channel);
        RecentShots.Enqueue(now);
        _lastShot = key + index;
    }

    // 按这一处的种类权重挑(没有素材的种类不挑)
    private static int PickKind(Spot spot)
    {
        float sum = 0f;
        for (int k = 0; k < KindFiles.Length; k++)
            if (spot.Palette[k] > 0f && BattleSoundBank.Variants(KindFiles[k]) > 0) sum += spot.Palette[k];
        if (sum <= 0f) return -1;
        float roll = (float)Rng.NextDouble() * sum;
        int fallback = -1;
        for (int k = 0; k < KindFiles.Length; k++)
        {
            if (spot.Palette[k] <= 0f || BattleSoundBank.Variants(KindFiles[k]) == 0) continue;
            fallback = k;
            roll -= spot.Palette[k];
            if (roll <= 0f) return k;
        }
        return fallback;
    }

    #endregion
}

// 战声每帧更新(不挂在模组主循环上：暂停时那边直接返回，这里要照常淡出)
public sealed class BattleSoundHost : MonoBehaviour
{
    private void Update() => BattleSound.Tick();
}
