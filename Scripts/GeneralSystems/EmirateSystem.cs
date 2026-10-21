using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 埃米尔坐大(用户 2026-10-17 转群友"游牧太强了几乎不会分封""还有阿拉伯"，选"埃米尔行省世袭坐大")：
//   · 阿拉伯政体的帝国打下的法理设为行省，由朝廷派的埃米尔治理(考选、不世袭、不通外交，见 Empire.update 的领土分封)；
//   · 哈里发正统不到 65 时(以前 50：正常的哈里发正统七八十，行省从不坐大；用户 2026-10-21 选"平常年景也坐大")，
//     每个行省每年有 (65 − 正统)/2 % 的机会埃米尔世袭其位(同日选"哈里发能衰也能复兴"减半：以前 (65 − 正统)%，
//     正统 50 时每省每年 15%，十年坐大八成)：法理归他所有(没有行政法理的行省也会坐大)，改为世袭、通外交、
//     不再支援中央军——正统再掉到 40 以下就能拥兵自立为苏丹国(见 kingdom_allow_independent)；
//     印度(阿拉伯制度线、法教传统)没有哈里发受册封的正统 +8，门槛对它减 8(见 InvestitureGap)；
//   · 鞭长莫及(用户 2026-10-19 选"鞭长莫及也会坐大""按帝国大小算")：正统再高，边远的行省也会坐大——各行省按都城离
//     帝国都城的远近排位，最远的三成才有份：排在七成处为 0，越往外越高，最远的到顶；顶多少看帝国多大，
//     8 城以下每年 4%，40 城以上每年 10%，中间平滑过渡。与正统那一项取大的。帝国越大越先从边远处裂开；
//   · 正统 StrongLegitimacy(75)以上才能削藩把世袭的埃米尔收回(见 TempFac_削藩；以前 60，与坐大的 65 以下重叠，
//     一边坐大一边削藩；用户 2026-10-21 选"埃米尔能收回"：提到 75，ClaimRules.json 里苏丹官僚、哈里发神权两条路线都放行)。
// 埃米尔割据(用户 2026-10-20 选"阿拉伯也照游牧那样修"；以前世袭的埃米尔照旧缴两成税，"拥兵自立"又要正统 40 以下、
//   都城不稳、对朝廷没有好感——同帝国 +100、受封五十年内 +100、圣城 +200 起，正统掉到个位数都自立不了，哈里发国从不分裂)：
//   · 世袭的埃米尔(阿拉伯政体、世袭、通外交的成员)只向中央缴 HereditaryCentralPercent 的税(见 TreasurySystem.TaxShares)；
//     行省缴 ProvinceCentralPercent(以前与世袭诸侯同为两成、本省留五成五，行省既听调遣又养得起兵，打起仗来比律令的道多出一两倍兵；
//     2026-10-21 由四成再提到五成，本省留两成五)；
//   · 边远的行省(远近排位在 DistantPercentile 以外)不奉诏远征，仗打到家门口才就近应战(见 DistantProvinces、WarScopeSystem.OrderMembers)；
//   · 世袭满 BreakawayTenureYears 年的，每年按 边远(鞭长莫及那一项)×0.5 + (55 − 正统)×0.5 + 每丢一成城 3 掷一次(%)，
//     兵力占帝国一成半以上再 ×1.5，至多 15%；每个帝国每年至多一家，苏丹(权臣)自己的国、开国气象里不割据；
//   · 正统 65 以上：和平脱离——名义上仍奉哈里发，改为朝贡国(还没有朝贡制度的改为附庸，再不行就自立)；
//     正统不到 65(以前 50；用户 2026-10-21 选"割据真丢地")：举兵自立，与朝廷开战(地方独立，同汗位更替之乱，见 NomadCycleSystem.DeclareIndependence)。
// 苏丹架空哈里发(用户 2026-10-19 选"补……苏丹架空哈里发""改用权臣机制")：本文化施行"苏丹官僚"后，哈里发照旧在位、
// 成为傀儡，苏丹以权臣掌实权、在本家承袭(同幕府的征夷大将军，见 MilitaryRegency)；离开阿拉伯政体时不再有苏丹。
public static class EmirateSystem
{
    // 坐大的正统门槛、每年机会占 (门槛 − 正统) 的几成；削藩的正统门槛(高于坐大区间，见文件头)
    public const int WeakLegitimacy = 65, StrongLegitimacy = 75;
    public const float WeakChanceShare = 0.5f;
    // 鞭长莫及：远近排位在 DistantPercentile 以外的行省有份；最远的每年机会(%)按帝国城数从 SmallMax 平滑升到 LargeMax
    public const float DistantPercentile = 0.7f;
    public const float DistantSmallMax = 4f, DistantLargeMax = 10f;
    public const int DistantSmallCities = 8, DistantLargeCities = 40;
    // 央地分成(见 TreasurySystem.TaxShares)：行省向中央缴五成(本省留两成五，略多于律令的道)，世袭的埃米尔缴两成(同别的世袭封国)
    // (用户 2026-10-20 定：以前只缴半成，哈里发的钱袋被世袭埃米尔掏空，阿拉伯中央财政四系最薄，提到一成二；
    //  2026-10-21 用户选"行省少留钱养兵"：行省四成 → 五成，兵额按本省财力算，行省的军约少三成；
    //  同日选"财政不断崖"：世袭一成二 → 两成——行省缴五成，一坐大这一省上缴的就少掉近四成，中央财政随坐大断崖式下跌)
    public const int ProvinceCentralPercent = 50, HereditaryCentralPercent = 20;
    // 割据：世袭满几年才有份；正统到多少以上和平脱离、改为朝贡；各项系数
    public const int BreakawayTenureYears = 15, PeacefulLegitimacy = 65, BreakawayWeakLegitimacy = 55;
    public const float BreakawayDistantShare = 0.5f, BreakawayWeakShare = 0.5f, BreakawayPerLostTenth = 3f,
        BreakawayStrongShare = 0.15f, BreakawayStrongFactor = 1.5f, BreakawayMax = 15f;
    public const string SecessionReason = "rebellion_reason_emirate_secession";
    // 各帝国上次结算的时间(不存档：读档、换世界时清掉，重新计满一年)
    private static readonly Dictionary<long, double> LastYear = new();

    public static void ResetWorldState() => LastYear.Clear();

    public static bool Applies(Empire empire) => empire?.CoreKingdom?.GetRegime()?.type == RegimeType.Arabic;

    public static bool IsSultanate(Empire empire) =>
        Applies(empire) && MilitaryRegency.KindOf(empire) == MilitaryRegency.Sultan;

    // 印度这类阿拉伯政体却不奉哈里发的(法教传统，见 PapacySystem.IsCaliphal)：没有受册封的正统 +8，正统常年低约 8 点，
    // 坐大、削藩、苏丹承袭之乱的正统门槛按它减(用户 2026-10-21 定"印度门槛减 8")；奉哈里发的为 0
    public static int InvestitureGap(Empire empire) =>
        Applies(empire) && !PapacySystem.IsCaliphal(empire) ? PapacySystem.CrownedLegitimacy : 0;

    // 世袭的埃米尔：哈里发国里阿拉伯政体、世袭、通外交的成员(坐大的行省，或建国时就带着领地入伙的)
    public static bool IsHereditaryEmir(Empire empire, Kingdom kingdom)
    {
        if (empire?.data == null || kingdom?.data == null || kingdom.isRekt() || kingdom == empire.CoreKingdom ||
            kingdom.GetEmpire() != empire || !Applies(empire)) return false;
        Regime regime = kingdom.GetRegime();
        return regime?.type == RegimeType.Arabic && regime.IsAllowDiplomacy() &&
               regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession;
    }

    // 离开阿拉伯政体：不再有苏丹、哈里发之分
    public static void OnRegimeChanged(Empire empire, RegimeType? previous, RegimeType current)
    {
        if (empire?.data == null || previous != RegimeType.Arabic || current == RegimeType.Arabic) return;
        if (MilitaryRegency.KindOf(empire) == MilitaryRegency.Sultan) MilitaryRegency.Dissolve(empire, null);
    }

    // 每年一次(由 Empire.update 调用)
    public static void Tick(Empire empire)
    {
        if (empire?.data == null || World.world == null || !Applies(empire)) return;
        double now = World.world.getCurWorldTime();
        if (LastYear.TryGetValue(empire.id, out double last) && now >= last && Date.getYearsSince(last) < 1) return;
        LastYear[empire.id] = now;
        int legitimacy = empire.Legitimacy;
        float weakChance = global::System.Math.Max(0, WeakLegitimacy - InvestitureGap(empire) - legitimacy) * WeakChanceShare;
        Dictionary<long, float> distantChances = DistantChances(empire);
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == empire.CoreKingdom || kingdom.king == null) continue;
            Regime regime = kingdom.GetRegime();
            KingdomTitle title = kingdom.GetAdministrativeTitle();
            // 没有行政法理的行省(飞地拆出来的)以前永远不会坐大；已世袭的不再重复
            if (regime?.type != RegimeType.Arabic || kingdom.GetMainTitle() != null || IsHereditaryEmir(empire, kingdom) ||
                title == null && kingdom.GetKingdomType() != KingdomType.Arabic_province) continue;
            distantChances.TryGetValue(kingdom.id, out float distantChance);
            if (UnityEngine.Random.value * 100f >= global::System.Math.Max(weakChance, distantChance)) continue;
            // 史书按主因写：远在边陲写鞭长莫及，否则写朝廷衰微
            bool distant = distantChance > weakChance;
            try
            {
                if (title != null)
                {
                    kingdom.king.AddOwnedTitle(title);
                    kingdom.SetMainTitle(title);
                }
                regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
                regime.SetAllowDiplomacy(true);
                regime.SetAllowSupportCenterArmy(false);
                kingdom.GetOrCreate().emirate_hereditary_at = now;
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
                string content = string.Format(LM.Get(distant ? "emirate_hereditary_distant" : "emirate_hereditary"),
                    kingdom.GetKingdomName(), kingdom.king.getName(), empire.GetEmpireName());
                empire.RecordHistory(directContent: content, kingdomId: kingdom.id);
                LogService.LogInfo("[EmpireCraft][埃米尔] " + content);
            }
            catch (global::System.Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][埃米尔] {kingdom.GetKingdomName()} 世袭失败: {exception.Message}");
            }
        }
        Breakaway(empire, legitimacy, distantChances, now);
    }

    #region 割据

    // 世袭满年限的埃米尔各掷一次，机会大的先掷；每个帝国每年至多脱离一家
    private static void Breakaway(Empire empire, int legitimacy, Dictionary<long, float> distantChances, double now)
    {
        Kingdom core = empire.CoreKingdom;
        Kingdom regentSeat = MilitaryRegency.Regent(empire)?.kingdom;
        float weak = global::System.Math.Max(0, BreakawayWeakLegitimacy - legitimacy) * BreakawayWeakShare;
        float defeat = BreakawayPerLostTenth * UnityEngine.Mathf.FloorToInt(NomadCycleSystem.LostShare(core) * 10f + 0.0001f);
        float total = -1f;
        var candidates = new List<(Kingdom kingdom, float chance)>();
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == core) continue;
            var data = kingdom.GetOrCreate();
            if (!IsHereditaryEmir(empire, kingdom))
            {
                data.emirate_hereditary_at = -1d;
                continue;
            }
            // 旧档里已世袭的、建国时就带着领地入伙的：从第一次看到起算
            if (data.emirate_hereditary_at < 0d || now < data.emirate_hereditary_at)
            {
                data.emirate_hereditary_at = now;
                continue;
            }
            if (Date.getYearsSince(data.emirate_hereditary_at) < BreakawayTenureYears) continue;
            if (kingdom == regentSeat || kingdom.king == null || kingdom.capital == null || kingdom.IsFactionRebelling() ||
                kingdom.IsLocalRebelling() || kingdom.isInWarWith(core) || !RebellionSystem.CanAttempt(kingdom)) continue;
            distantChances.TryGetValue(kingdom.id, out float distant);
            float chance = distant * BreakawayDistantShare + weak + defeat;
            if (chance <= 0f) continue;
            if (total < 0f) total = empire.kingdoms_list.Sum(Might);
            if (total > 0f && Might(kingdom) >= total * BreakawayStrongShare) chance *= BreakawayStrongFactor;
            candidates.Add((kingdom, UnityEngine.Mathf.Min(BreakawayMax, chance)));
        }
        foreach ((Kingdom kingdom, float chance) in candidates.OrderByDescending(entry => entry.chance))
        {
            if (UnityEngine.Random.value * 100f >= chance) continue;
            if (Secede(empire, kingdom, legitimacy, chance)) return;
        }
    }

    private static float Might(Kingdom kingdom) =>
        kingdom?.data == null || kingdom.isRekt() ? 0f
            : kingdom.countTotalWarriors() + (kingdom.cities ?? new List<City>()).Sum(city => CorpsSystem.EquivalentAt(city));

    // 正统 PeacefulLegitimacy(65)以上和平脱离(朝贡、附庸或自立)，不到 65 举兵自立
    private static bool Secede(Empire empire, Kingdom kingdom, int legitimacy, float chance)
    {
        string name = kingdom.GetKingdomName(), emir = kingdom.king?.getName() ?? "", realm = empire.GetEmpireName();
        Kingdom core = empire.CoreKingdom;
        try
        {
            string key;
            if (legitimacy < PeacefulLegitimacy)
            {
                if (!NomadCycleSystem.DeclareIndependence(kingdom, empire, SecessionReason)) return false;
                key = "emirate_breakaway_war";
            }
            else
            {
                // 不记作叛离(isLeave: false)，否则收不成朝贡国
                kingdom.FinishedSelfPlot();
                empire.leave(kingdom, true, false);
                if (kingdom.IsInEmpire()) return false;
                kingdom.GetOrCreate().emirate_hereditary_at = -1d;
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
                if (!empire.isRekt() && !empire.IsArchived()) kingdom.JoinTakenAlliance(empire, pForce: true);
                key = kingdom.HasTakenAlliance() ? "emirate_breakaway_tributary"
                    : core != null && !core.isRekt() && FeudalVassalService.ForceBind(core, kingdom) ? "emirate_breakaway_vassal"
                    : "emirate_breakaway_independent";
            }
            string content = string.Format(LM.Get(key), name, emir, realm);
            empire.RecordHistory(directContent: content, kingdomId: kingdom.id);
            TranslateHelper.LogEventMessage(content, kingdom);
            LogService.LogInfo($"[EmpireCraft][埃米尔] {content}(正统{legitimacy}，机会{chance:0.0}%)");
            return true;
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][埃米尔] {name} 割据失败: {exception.Message}");
            return false;
        }
    }

    #endregion

    // 鞭长莫及：各行省(含已世袭的埃米尔国，好让"边远"始终按版图算)按都城离帝国都城的直线距离排位，
    // 排位 = 第几近 / (行省数 − 1)，过了 DistantPercentile 才有份，线性升到最远的 DistantMax。只有一个行省时谈不上边远
    private static Dictionary<long, float> DistantChances(Empire empire)
    {
        var result = new Dictionary<long, float>();
        UnityEngine.Vector2? seat = SeatOf(empire.CoreKingdom);
        if (seat == null) return result;
        var ranked = new List<(Kingdom kingdom, float distance)>();
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == empire.CoreKingdom ||
                kingdom.GetRegime()?.type != RegimeType.Arabic) continue;
            UnityEngine.Vector2? position = SeatOf(kingdom);
            // 只比远近，按平均地形折算的系数(见 AdministrativeControlSystem.BeyondReach)不影响排位，这里不乘
            if (position != null) ranked.Add((kingdom, UnityEngine.Vector2.Distance(seat.Value, position.Value)));
        }
        if (ranked.Count < 2) return result;
        ranked.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : a.kingdom.id.CompareTo(b.kingdom.id));
        float max = DistantMax(empire.countCities());
        for (int i = 0; i < ranked.Count; i++)
        {
            float percentile = (float)i / (ranked.Count - 1);
            if (percentile <= DistantPercentile) continue;
            result[ranked[i].kingdom.id] = max * (percentile - DistantPercentile) / (1f - DistantPercentile);
        }
        return result;
    }

    // 边远的行省：远近排位在 DistantPercentile 以外的阿拉伯政体成员，不奉诏远征(见 WarScopeSystem.OrderMembers)
    public static HashSet<long> DistantProvinces(Empire empire) =>
        empire?.data == null || empire.CoreKingdom == null || !Applies(empire)
            ? new HashSet<long>()
            : new HashSet<long>(DistantChances(empire).Keys);

    // 最远行省每年的机会(%)：城数从 DistantSmallCities 到 DistantLargeCities 按 smoothstep 由 SmallMax 升到 LargeMax
    public static float DistantMax(int cities)
    {
        float t = UnityEngine.Mathf.Clamp01((float)(cities - DistantSmallCities) / (DistantLargeCities - DistantSmallCities));
        return DistantSmallMax + (DistantLargeMax - DistantSmallMax) * t * t * (3f - 2f * t);
    }

    // 治所位置(城心所在区块的格坐标，同行政控制力)
    private static UnityEngine.Vector2? SeatOf(Kingdom kingdom)
    {
        City capital = kingdom?.capital;
        if (capital?.data == null || capital.isRekt() || capital.zones == null || capital.zones.Count == 0) return null;
        TileZone zone = AdministrativeControlSystem.CenterZone(capital);
        return zone == null ? null : new UnityEngine.Vector2(zone.x, zone.y);
    }
}
