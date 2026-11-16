using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 幕府的倒幕、南北朝与战国(群友 2026-11-16 的六件事，用户选做"日本倒幕到南北朝")：
//   幕府政体本有三派——幕府派(中央)、尊王派(尊王)、大名派(自治)，以前三派的消长只走派系失衡那一套，
//   开幕之后要么将军家更替引出应仁之乱，要么等明治维新大政奉还，中间没有戏。现在按三派占比每年判一次：
//     ① 开幕：将军就任那年幕府派添势(开幕的记史照旧，见 MilitaryRegency)；
//     ② 天皇起兵倒幕：尊王派压过幕府派，天皇成年，上回起兵已过 RisingCooldownYears 年——每年 RisingChance。
//        胜负按两派占比之差与正统判(不给选项，照模组的规矩自己判)：
//          · 败：幕府派再添势，天命 -5；
//          · 胜：幕府瓦解、天皇亲政(不换政体：换了一两年内会被制度树改回去，这里只是不立将军，见 CourtRules)，尊王派添势；
//     ③ 公家新政：天皇亲政期间偏袒公卿，大名派每年添势；满 KenmuYears 年而大名派不弱于尊王派——
//     ④ 南北朝分立：武士在离京城远的一头拥立皇族另一位(万世一系不破)，另立一朝、当场立将军，两朝交战
//        (见 EmpirePartitionSystem.SplitRivalCourt；方位照两都的相对位置自动定，不写死南北)。
//        分不出来(没有别的城、宗室无人、不许自立)时武家直接再开幕府；
//     ⑤ 南北朝合一：两朝并立满 ReuniteYears 年，武家一朝里幕府派压过尊王派——每年 ReuniteChance，幕府说和，
//        朝廷一朝并入武家一朝，大名各保其地；
//     ⑥ 战国：有将军在位，大名派 ≥ 幕府派 × SengokuRatio 且成员国(大名) ≥ SengokuDaimyo 家——每年 SengokuChance，
//        诸国一齐脱离，相邻的互相开战(每家至多两处)。受不许自立的法则与大分裂冷却管。
//   三派缺一(玩家改了派系、开了党禁)就不判。状态存 EmpireData.shogunate_*；南北朝并立看 partition_kind。
public static class ShogunateDramaSystem
{
    private const string Tag = "[EmpireCraft][幕府]";

    public const int StageNormal = 0, StageCourt = 1;

    private const int OpeningBoost = 15, RisingLostBoost = 12, RisingWonBoost = 20, KenmuDrift = 4, SplitBoost = 15,
        ReuniteBoost = 5, SengokuBoost = 20;
    private const int RisingCooldownYears = 25, KenmuYears = 3, ReuniteYears = 8, SengokuDaimyo = 10, SengokuWarsEach = 2;
    private const float RisingChance = 0.2f, ReuniteChance = 0.2f, SengokuChance = 0.25f, SengokuRatio = 1.5f;

    // 天皇亲政期间不立将军(见 MilitaryRegency.Wanted)
    public static bool CourtRules(Empire empire) => empire?.data != null && empire.data.shogunate_stage == StageCourt;

    // 天皇起兵的胜算：尊王派比幕府派每多一成占比 +0.1，正统每高出 50 十点 +0.05，一成五到七成五之间
    public static float RisingOdds(int loyalist, int shogunal, int legitimacy) =>
        Math.Max(0.15f, Math.Min(0.75f, 0.35f + (loyalist - shogunal) / 100f + (legitimacy - 50) / 200f));

    // 该不该进战国：大名派是幕府派的 SengokuRatio 倍以上(幕府派为零时大名派有势即算)，大名够数
    public static bool SengokuDue(int daimyoFaction, int shogunal, int daimyoRealms) =>
        daimyoRealms >= SengokuDaimyo && daimyoFaction > 0 && daimyoFaction >= shogunal * SengokuRatio;

    private static FixedFaction Faction(Empire empire, FactionType type)
    {
        foreach (FixedFaction faction in CourtSystem.Factions(empire))
            if (faction != null && faction.Type == type) return faction;
        return null;
    }

    private static void Boost(Empire empire, FixedFaction faction, int amount)
    {
        if (faction != null && amount > 0) empire.CoreKingdom.TryIncreaseFactionRatio(faction, amount);
    }

    private static bool Chance(float chance) => UnityEngine.Random.value < chance;

    // 每年(见 ConstitutionalEconomySystem.Update 的年度段，排在派系失衡之后)
    public static void Yearly(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null) return;
        EmpireData data = empire.data;
        if (!BakufuSystem.IsBakufu(empire))
        {
            // 离了幕府政体(大政奉还、改制)：这一段戏收场
            data.shogunate_stage = StageNormal;
            return;
        }
        FixedFaction shogunal = Faction(empire, FactionType.中央), loyalist = Faction(empire, FactionType.尊王),
            daimyo = Faction(empire, FactionType.自治);
        if (shogunal == null || loyalist == null || daimyo == null) return;
        try
        {
            Empire twin = EmpirePartitionSystem.Twin(empire);
            bool divided = twin != null && empire.data.partition_kind == EmpirePartitionSystem.KindShogunate;
            if (data.shogunate_stage == StageCourt)
            {
                if (!divided) Kenmu(empire, loyalist, daimyo);
                return;
            }
            if (MilitaryRegency.KindOf(empire) != MilitaryRegency.Shogun) return;
            Opening(empire, shogunal);
            if (divided)
            {
                Reunite(empire, twin, shogunal, loyalist);
                return;
            }
            if (twin != null) return;
            if (Sengoku(empire, shogunal, daimyo)) return;
            Rising(empire, shogunal, loyalist);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{Tag} {empire.GetEmpireName()} 年度结算失败: {exception.Message}");
        }
    }

    // ① 开幕：这一家将军头一回就任，幕府派添势(已经最大的不再添；旧档里早已开幕的头一年也照此补一次)
    private static void Opening(Empire empire, FixedFaction shogunal)
    {
        EmpireData data = empire.data;
        if (data.regent_clan_id < 0 || data.shogunate_opened_clan_id >= 0) return;
        data.shogunate_opened_clan_id = data.regent_clan_id;
        if (empire.CoreKingdom.GetHighestFactionRatio() != shogunal) Boost(empire, shogunal, OpeningBoost);
    }

    // ② 天皇起兵倒幕
    private static void Rising(Empire empire, FixedFaction shogunal, FixedFaction loyalist)
    {
        EmpireData data = empire.data;
        Actor emperor = empire.Emperor, shogun = MilitaryRegency.Regent(empire);
        if (emperor?.data == null || !emperor.isAlive() || !emperor.isAdult() || shogun == null) return;
        if (loyalist.CentralRatio <= shogunal.CentralRatio) return;
        if (data.shogunate_rising_at >= 0d && Date.getYearsSince(data.shogunate_rising_at) < RisingCooldownYears) return;
        if (!Chance(RisingChance)) return;
        data.shogunate_rising_at = World.world.getCurWorldTime();
        string name = empire.GetEmpireName(), emperorName = emperor.getName(), shogunName = shogun.getName();
        float odds = RisingOdds(loyalist.CentralRatio, shogunal.CentralRatio, empire.Legitimacy);
        if (!Chance(odds))
        {
            Boost(empire, shogunal, RisingLostBoost);
            empire.AddMandate(-5);
            Record(empire, string.Format(LM.Get("shogunate_rising_failed_history"), name, emperorName, shogunName), major: false);
            LogService.LogInfo($"{Tag} {name} 天皇{emperorName}起兵倒幕失败(胜算 {odds:0.00})");
            return;
        }
        data.shogunate_stage = StageCourt;
        data.shogunate_stage_at = World.world.getCurWorldTime();
        data.shogunate_opened_clan_id = -1L;
        MilitaryRegency.Dissolve(empire, null);
        Boost(empire, loyalist, RisingWonBoost);
        empire.AddMandate(5);
        Record(empire, string.Format(LM.Get("shogunate_rising_won_history"), name, emperorName, shogunName), major: true);
        LogService.LogInfo($"{Tag} {name} 天皇{emperorName}倒幕成功(胜算 {odds:0.00})，天皇亲政");
    }

    // ③ 公家新政 → ④ 南北朝分立(分不出来就再开幕府)
    private static void Kenmu(Empire empire, FixedFaction loyalist, FixedFaction daimyo)
    {
        EmpireData data = empire.data;
        Boost(empire, daimyo, KenmuDrift);
        if (data.shogunate_stage_at >= 0d && Date.getYearsSince(data.shogunate_stage_at) < KenmuYears) return;
        if (daimyo.CentralRatio < loyalist.CentralRatio) return;
        string name = empire.GetEmpireName(), emperorName = empire.Emperor?.getName() ?? "";
        Empire rival = UpheavalGuard.Blocked(empire, out _) ? null : EmpirePartitionSystem.SplitRivalCourt(empire);
        if (rival == null)
        {
            // 另立不成：武家直接再开幕府(下个月由 MilitaryRegency 择一家将军)
            data.shogunate_stage = StageNormal;
            data.shogunate_stage_at = World.world.getCurWorldTime();
            MilitaryRegency.Refresh(empire);
            Record(empire, string.Format(LM.Get("shogunate_reopened_history"), name), major: false);
            LogService.LogInfo($"{Tag} {name} 公家新政失武士之心，另立不成，再开幕府");
            return;
        }
        UpheavalGuard.Mark(empire, "南北朝分立");
        // 朝廷一朝仍由尊王派主导、照旧不立将军；武家一朝以大名派为核心，当场立将军
        Boost(empire, loyalist, SplitBoost / 2);
        FixedFaction rivalDaimyo = Faction(rival, FactionType.自治);
        if (rivalDaimyo != null) Boost(rival, rivalDaimyo, SplitBoost);
        MilitaryRegency.EstablishNow(rival);
        string text = string.Format(LM.Get("shogunate_split_history"), name, emperorName, rival.Emperor?.getName() ?? "",
            rival.CoreKingdom?.capital?.GetCityName() ?? "");
        Record(empire, text, major: true);
        rival.RecordHistory(directContent: text, kingdomId: rival.CoreKingdom?.id ?? -1L);
        LogService.LogInfo($"{Tag} {name} 南北朝分立：{rival.GetEmpireFullName()} 立于 {rival.CoreKingdom?.capital?.GetCityName()}");
    }

    // ⑤ 南北朝合一：由有将军的武家一朝来判(empire 是武家一朝，court 是天皇亲政的那一朝)
    private static void Reunite(Empire empire, Empire court, FixedFaction shogunal, FixedFaction loyalist)
    {
        if (court?.data == null || court.isRekt() || court.IsArchived() || !CourtRules(court)) return;
        if (empire.data.partition_time >= 0d && Date.getYearsSince(empire.data.partition_time) < ReuniteYears) return;
        if (shogunal.CentralRatio <= loyalist.CentralRatio || !Chance(ReuniteChance)) return;
        string name = empire.GetEmpireFullName(), courtName = court.GetEmpireFullName();
        if (!EmpirePartitionSystem.Reunite(court, empire, "shogunate_reunite_history", major: true)) return;
        empire.data.shogunate_stage = StageNormal;
        Boost(empire, shogunal, ReuniteBoost);
        LogService.LogInfo($"{Tag} 南北朝合一：{courtName} 并入 {name}");
    }

    // ⑥ 战国：诸国大名一齐脱离，相邻的互相开战
    private static bool Sengoku(Empire empire, FixedFaction shogunal, FixedFaction daimyo)
    {
        Kingdom core = empire.CoreKingdom;
        List<Kingdom> realms = empire.kingdoms_list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom != core &&
                                                                     kingdom.hasCities()).ToList();
        if (!SengokuDue(daimyo.CentralRatio, shogunal.CentralRatio, realms.Count)) return false;
        if (!Chance(SengokuChance) || UpheavalGuard.Blocked(empire, out _) || SecessionGate.Forbids()) return false;
        string name = empire.GetEmpireName();
        var left = new List<Kingdom>();
        foreach (Kingdom realm in realms)
        {
            empire.leave(realm, pRecalc: true, isLeave: true);
            if (realm.GetEmpire() == empire) continue;
            EmpireLaw.EmpireLawSystem.NoteSecession(realm);
            left.Add(realm);
        }
        if (left.Count == 0) return false;
        Boost(empire, daimyo, SengokuBoost);
        UpheavalGuard.Mark(empire, "战国", detail: $"{left.Count} 家大名自立");
        int wars = Feuds(left);
        Record(empire, string.Format(LM.Get("shogunate_sengoku_history"), name, left.Count), major: true);
        LogService.LogInfo($"{Tag} {name} 进入战国：{left.Count} 家大名自立，开战 {wars} 处");
        return true;
    }

    // 自立的大名之间：城挨着城的互相开战，每家至多 SengokuWarsEach 处(家数多时两两全开要几十上百场)
    private static int Feuds(List<Kingdom> realms)
    {
        var set = new HashSet<Kingdom>(realms);
        var count = new Dictionary<Kingdom, int>();
        int wars = 0;
        foreach (Kingdom realm in realms)
        {
            if (realm.isRekt() || realm.cities == null) continue;
            foreach (City city in realm.cities.ToList())
            {
                if (city?.neighbours_cities == null) continue;
                foreach (City next in city.neighbours_cities)
                {
                    Kingdom other = next?.kingdom;
                    if (other == null || other == realm || !set.Contains(other) || other.isRekt() || realm.isInWarWith(other)) continue;
                    count.TryGetValue(realm, out int mine);
                    count.TryGetValue(other, out int theirs);
                    if (mine >= SengokuWarsEach || theirs >= SengokuWarsEach) continue;
                    try
                    {
                        World.world.diplomacy.startWar(realm, other, WarTypeLibrary.normal);
                    }
                    catch (Exception exception)
                    {
                        LogService.LogWarning($"{Tag} 战国开战失败: {exception.Message}");
                        continue;
                    }
                    if (!realm.isInWarWith(other)) continue;
                    count[realm] = mine + 1;
                    count[other] = theirs + 1;
                    wars++;
                }
            }
        }
        return wars;
    }

    // 记进史书；大事不论哪国都上屏(见 TranslateHelper.LogMajor)，其余照寻常的世界消息
    private static void Record(Empire empire, string text, bool major)
    {
        if (!major)
        {
            EventRecorder.Record(empire, text);
            return;
        }
        empire.RecordHistory(directContent: text, kingdomId: empire.CoreKingdom?.id ?? -1L);
        TranslateHelper.LogMajor(text, empire.CoreKingdom);
    }
}
