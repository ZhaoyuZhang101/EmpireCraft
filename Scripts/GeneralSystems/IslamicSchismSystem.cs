using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 伊斯兰分裂更多样(用户 2026-11-06 在宗教四项改进里选"伊斯兰分裂更多样")。以前伊斯兰只有一种分裂：哈里发、苏丹更替之乱开争位之战时
// 争位者五成另奉什叶派(见 ReligionSchismSystem.OnSuccessionContest)。现在(数据存在宗教的 ReligionExtraData.Islamic，见 IslamicSchismData)：
//   · 对立哈里发(原型科尔多瓦后倭马亚、法蒂玛)：奉伊斯兰(有哈里发座、不是地方教会)、立教满 RivalMinAge 年的宗教，本教的哈里发国
//     (阿拉伯政体、受册封之列，见 CaliphateFaith.IsCaliphate、PapacySystem.AppliesCaliphal)君主成年、正统 RivalMinLegitimacy 以上、
//     RivalMinCities 城以上且不少于哈里发座所在之国(帝国按全境)的 RivalStrengthShare，不在分裂进程里、不奉别的对立哈里发，并且
//       - 远：帝都离圣地至少地图宽高的 RivalFarShare；或
//       - 敌：与哈里发座所在之国(帝国按中央)交战、哈里发之国衰微(正闹更替之乱或正统低于 DeclineLegitimacy)、
//         与之分立相争(两朝并立，见 EmpirePartitionSystem)，
//     每年 RivalChance(敌 RivalHostileChance；受过圣地哈里发册封的减半)。本教同时至多 MaxRivals 位对立哈里发，有一位终结后
//     RivalCooldownYears 年不另立，同一国废后 SeatReproclaimYears 年不再立。宣告：自立者当场算受册封，本教教权 −RivalAuthorityLoss；
//     哈里发更替之乱相持分裂、争位者带走一方时(见 SchismSystem.Settle)，仍奉本教的争位者 ContestRivalChance 自称哈里发；
//   · 法蒂玛式：自立者以先代哈里发之裔自居(Alid：君主出自曾据哈里发座、不是现任一系的宗族)、或民心不附(Unhappy)、或境内奉什叶派的
//     人口过 FatimidShiaShare 时，FatimidShare 机会改立什叶派(本教还没分出过)——圣地在其都城，本人即伊玛目-哈里发；已有什叶派而它的
//     伊玛目之座已失的，迎至本都(率国改奉)；已有什叶派且伊玛目在位的，另立伊斯玛仪派(什叶派还没分出过)；本就奉什叶派的，另立伊斯玛仪派；
//   · 奉哪位哈里发(各哈里发国，每年)：对立哈里发本国奉自己，圣地之国奉圣地哈里发；其余已受册封、与所奉的哈里发不交战的不改，
//     否则按离各哈里发都城的远近择一(圣地哈里发 −OrthodoxPull、现奉的 −StayPull，交战的、两朝相争的对方不奉)。改奉的册封作废；
//     册封、天课都给所奉的哈里发(见 PapacySystem.CaliphalYear)。只奉对立哈里发、本教圣地已失的帝国照样有册封与天课；
//   · 对立哈里发的终结(每年)：哈里发座之国覆亡(fallen；帝国迁了中央的随中央)、改奉别教(faith)、归附他国成了成员(submitted)、
//     都城落入他国之手(conquered；自己迁都的随新都)、据有圣地(reunited，两哈里发复归为一)、不再行君主制(abolished)、
//     易姓(line：帝国看皇室宗族，独立王国看君主宗族，与自立者不同世系)；陷落、易姓、覆亡、归附、复归，本教教权 +RivalEndAuthority。
//     终结后奉它的帝国册封作废，改向圣地哈里发求册封；
//   · 自成一派：对立哈里发历 RecognizeYears 年不废，每年 RecognizeChance(每个宗教一次)诸国承认它自成一派——以其都城为圣地另立分支
//     (以都城命名)，本国与同奉旧教的成员当场改奉，奉它的帝国中央改奉(其余城按国教势能陆续改，见 ReligionConversionSystem.Convert)；
//     母宗教此后 RecognizedPeaceYears 年不讨伐分出去的"异端"(见 PapacySystem.HereticTarget)；
//   · 什叶派(争位之外，每年，独立王国与帝国中央，君主成年、ShiaMinCities 城以上，不握本教圣地、不是哈里发座)：
//       - 立：本教(本土伊斯兰，立教满 ShiaMinAge 年)还没分出过什叶派——先代哈里发之裔且(与哈里发座交战或民心不附) ShiaAlidChance，
//         交战且民心不附 ShiaGrievanceChance，只有圣裔之望 ShiaClaimChance；圣地在其都城，本国当场改奉，帝国成员各 FollowerShare 随之；
//       - 改奉：已有什叶派——治下奉什叶派的人口过半 ShiaAdoptMajorityChance、过 ShiaAdoptMinority ShiaAdoptMinorityChance，
//         先代哈里发之裔与哈里发座交战 ShiaAdoptClaimChance；君主率国皈依(见 ReligionConversionSystem.Convert)，皈依后 AdoptCooldownYears 年不再判；
//     每个母宗教至多一支什叶派(ShiaSplit)，每支什叶派至多分出一支伊斯玛仪派(sub_split)——原型 765 年伊玛目继承之争，只在什叶派的争位
//     (ContestIsmailiChance)、法蒂玛式自立时发生；已有什叶派时哈里发、苏丹争位者 ContestJoinShiaChance 投向它(见 ReligionSchismSystem)。
//     争位之外立什叶派、伊斯玛仪派，原奉之教教权 −SplitAuthorityLoss；
//   · 复古运动(原型穆拉比特、穆瓦希德)：每个伊斯兰教同时至多一场，上一场兴起后 MovementCooldownYears 年不另起。奉本教的国家宽纵失教
//     (王朝末世、苛政负担 LaxHarshBurden 以上、与所奉的哈里发交战、治下 LaxMinorityShare 以上的城奉异教)，境内奉本教、稳定低于起事线的城
//     (见 ReligiousTension.CanRevolt)每年 MovementChance 举教起事；起事成功(叛乱了结、自立)即成圣战之国——本教教权 +ZealAuthority，
//     国内政教合一；此后 JihadYears 年内太平时每年 JihadChance 对接壤(或隔海相望)、城数不过本国 JihadTargetRatio 倍的异教国、
//     别的伊斯兰教派之国或宽纵的故国发动圣战(先异教、后异派、再故国)，至多 MaxJihads 次；起事之国覆亡即告平定。
// 圣战响应、宗教窗口的对立哈里发一栏要在别的文件里接(见 RallyFactor 与窗口用的几个查询)。
public static class IslamicSchismSystem
{
    public const string BranchShia = "shia", BranchIsmaili = "ismaili", BranchRival = "rival";

    // 对立哈里发
    public const int RivalMinAge = 40, RivalMinCities = 10, RivalMinLegitimacy = 50, DeclineLegitimacy = 40, MaxRivals = 2, RivalCooldownYears = 30,
        SeatReproclaimYears = 60, RecognizeYears = 80, RecognizedPeaceYears = 50, MaxRivalRecords = 12;
    public const float RivalStrengthShare = 0.5f, RivalFarShare = 0.3f, RivalChance = 0.01f, RivalHostileChance = 0.03f,
        RivalInvestedFactor = 0.5f, RivalAuthorityLoss = 10f, RivalEndAuthority = 5f, RecognizeChance = 0.03f,
        ContestRivalChance = 0.5f;
    // 法蒂玛式
    public const float FatimidShare = 0.4f, FatimidShiaShare = 0.25f;
    // 奉哪位哈里发：距离按地图宽高折算
    private const float OrthodoxPull = 0.15f, StayPull = 0.1f;
    // 什叶派
    public const int ShiaMinAge = 40, ShiaMinCities = 3, AdoptCooldownYears = 50;
    public const float ShiaAlidChance = 0.02f, ShiaGrievanceChance = 0.01f, ShiaClaimChance = 0.003f, UnhappyShare = 0.3f,
        ShiaAdoptMajority = 0.5f, ShiaAdoptMinority = 0.3f, ShiaAdoptMajorityChance = 0.04f, ShiaAdoptMinorityChance = 0.015f,
        ShiaAdoptClaimChance = 0.01f, FollowerShare = 0.6f, SplitAuthorityLoss = 5f;
    // 争位(见 ReligionSchismSystem.OnSuccessionContest)：已有什叶派时争位者投向它；什叶派的争位分出伊斯玛仪派
    public const float ContestJoinShiaChance = 0.25f, ContestIsmailiChance = 0.25f;
    // 复古运动
    public const int MovementCooldownYears = 40, JihadYears = 40, MaxJihads = 3, MaxMovementRecords = 20, MovementNames = 4;
    public const float MovementChance = 0.01f, LaxMinorityShare = 0.3f, LaxHarshBurden = 40f, JihadChance = 0.15f,
        JihadTargetRatio = 3f, ZealAuthority = 5f;
    // 先代哈里发宗族至多记几家
    private const int MaxHouses = 24;
    private const double SecondsPerYear = 60d;
    private const string Tag = "[EmpireCraft][伊斯兰分裂]";

    private static double _lastPass = -1d;

    // 换世界、读档时清(见 SchismSystem.ResetWorldState)
    public static void ResetWorldState() => _lastPass = -1d;

    #region 查询

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild;

    private static bool Alive(Religion religion) => religion?.data != null && !religion.isRekt();

    private static bool Alive(Actor actor) => actor?.data != null && !actor.isRekt() && actor.isAlive();

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    private static Kingdom LiveKingdom(long id)
    {
        Kingdom kingdom = id >= 0 && World.world?.kingdoms != null ? World.world.kingdoms.get(id) : null;
        return Alive(kingdom) ? kingdom : null;
    }

    private static Empire LiveEmpire(long id)
    {
        Empire empire = id >= 0 ? ModClass.EMPIRE_MANAGER?.get(id) : null;
        return empire?.data != null && !empire.isRekt() && !empire.IsArchived() ? empire : null;
    }

    private static double Now => World.world.getCurWorldTime();

    private static bool Within(double at, int years) => at >= 0d && Now >= at && Date.getYearsSince(at) < years;

    private static float Span => Mathf.Max(1f, Mathf.Max(MapBox.width, MapBox.height));

    // 本教的伊斯兰分裂数据(没有就建)
    public static IslamicSchismState State(Religion religion)
    {
        if (religion?.data == null) return null;
        ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
        data.Islamic ??= new IslamicSchismState();
        data.Islamic.rivals ??= new List<RivalCaliphateData>();
        data.Islamic.caliph_houses ??= new List<long>();
        data.Islamic.movements ??= new List<ReformMovementData>();
        return data.Islamic;
    }

    // 只读(没有不建)
    private static IslamicSchismState Peek(Religion religion) => religion?.data == null ? null : religion.GetOrCreate().Islamic;

    public static List<RivalCaliphateData> AllRivals(Religion religion) =>
        Peek(religion)?.rivals?.Where(rival => rival != null).ToList() ?? new List<RivalCaliphateData>();

    public static List<RivalCaliphateData> ActiveRivals(Religion religion) =>
        AllRivals(religion).Where(rival => rival.ended_at < 0d).ToList();

    // 对立哈里发座所在之国(已终结、已亡返回空；是否仍奉本教每年核，见 Ending)
    public static Kingdom Seat(RivalCaliphateData rival) => rival == null || rival.ended_at >= 0d ? null : LiveKingdom(rival.seat_kingdom_id);

    public static Actor Caliph(RivalCaliphateData rival)
    {
        Actor king = Seat(rival)?.king;
        return Alive(king) ? king : null;
    }

    // 这个帝国奉的对立哈里发(奉圣地哈里发返回空)
    public static RivalCaliphateData ObedienceOf(Empire empire, Religion religion)
    {
        if (empire?.data == null || religion?.data == null) return null;
        IslamicSchismState state = Peek(religion);
        if (state?.rivals == null) return null;
        foreach (RivalCaliphateData rival in state.rivals)
            if (rival != null && rival.ended_at < 0d && rival.adherents != null && rival.adherents.Contains(empire.id)) return rival;
        return null;
    }

    // 这个帝国所奉哈里发的座(对立哈里发之国或圣地之国)
    public static Kingdom SeatFor(Empire empire, Religion religion)
    {
        RivalCaliphateData rival = ObedienceOf(empire, religion);
        if (rival == null) return PapacySystem.Papacy(religion);
        Kingdom seat = Seat(rival);
        return seat != null && seat.religion == religion ? seat : null;
    }

    // 以 realm 为座的对立哈里发
    public static RivalCaliphateData RivalSeatedIn(Religion religion, Kingdom realm) =>
        realm == null ? null : ActiveRivals(religion).FirstOrDefault(rival => Seat(rival) is Kingdom seat && Realm(seat) == realm);

    // 本教是怎么分出来的(BranchShia / BranchIsmaili / BranchRival，本教或未记为 "")
    public static string BranchOf(Religion religion)
    {
        if (!Alive(religion)) return "";
        string branch = Peek(religion)?.branch;
        if (!string.IsNullOrEmpty(branch)) return branch;
        long parentId = religion.GetOrCreate().ParentReligionId;
        Religion parent = parentId >= 0 ? World.world.religions.get(parentId) : null;
        return Alive(parent) && parent != religion && ShiaOf(parent) == religion ? BranchShia : "";
    }

    public static bool IsShia(Religion religion) => BranchOf(religion) == BranchShia;

    // 母宗教分出的什叶派(没分过、已经没了返回空)。旧档、剧本(WorldMapReligion)只在母宗教上记了 ShiaSplit：
    // 头一次查时取它最早分出的伊斯兰分支补记为什叶派
    public static Religion ShiaOf(Religion parent)
    {
        if (!Alive(parent) || !parent.GetOrCreate().ShiaSplit || !ReligionFaith.IsCaliphal(parent)) return null;
        IslamicSchismState state = State(parent);
        if (state.shia_id == -1L)
        {
            Religion legacy = null;
            double legacyAt = double.MaxValue;
            foreach (Religion religion in World.world.religions.list)
            {
                if (!Alive(religion) || religion == parent || !ReligionFaith.IsCaliphal(religion)) continue;
                ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
                if (data.ParentReligionId != parent.data.id || data.Caesaropapist ||
                    !string.IsNullOrEmpty(data.Islamic?.branch) && data.Islamic.branch != BranchShia) continue;
                double at = data.SplitAt >= 0d ? data.SplitAt : double.MaxValue / 2d;
                if (legacy != null && (at > legacyAt || at == legacyAt && religion.data.id > legacy.data.id)) continue;
                legacy = religion;
                legacyAt = at;
            }
            // -2：分出过但已找不到(不再找)
            state.shia_id = legacy?.data.id ?? -2L;
            if (legacy != null) State(legacy).branch = BranchShia;
        }
        Religion shia = state.shia_id >= 0 ? World.world.religions.get(state.shia_id) : null;
        return Alive(shia) ? shia : null;
    }

    // 先代哈里发之裔：君主出自曾据本教哈里发座的宗族世系，而不是现任哈里发一系(史上什叶派奉阿里一系)
    public static bool Alid(Religion religion, Actor ruler)
    {
        SpecificClan clan = ruler?.GetSpecificClan();
        List<long> houses = Peek(religion)?.caliph_houses;
        if (clan == null || houses == null || houses.Count == 0) return false;
        SpecificClan reigning = PapacySystem.Pope(religion)?.GetSpecificClan();
        if (reigning != null && SpecificClanManager.SameLineage(clan, reigning)) return false;
        foreach (long id in houses)
        {
            SpecificClan house = SpecificClanManager.Get(id);
            if (house != null && SpecificClanManager.SameLineage(clan, house)) return true;
        }
        return false;
    }

    // 圣战响应(见 PapacySystem.Crusade.Rally)：号召者是圣地哈里发时，奉对立哈里发的帝国响应减半，对立哈里发本国不响应；没有对立哈里发为 1
    public static float RallyFactor(Kingdom kingdom, Religion religion, Kingdom caller)
    {
        if (!Alive(kingdom) || religion?.data == null || ActiveRivals(religion).Count == 0) return 1f;
        Kingdom realm = Realm(kingdom);
        if (RivalSeatedIn(religion, realm) is RivalCaliphateData own && Realm(Seat(own)) != Realm(caller)) return 0f;
        Empire empire = realm.GetEmpire();
        RivalCaliphateData rival = empire != null && empire.CoreKingdom == realm ? ObedienceOf(empire, religion) : null;
        if (rival == null) return 1f;
        return Seat(rival) is Kingdom seat && Realm(seat) == Realm(caller) ? 1f : 0.5f;
    }

    #endregion

    #region 每年

    // 每年一次(挂在 CaliphateFaith.YearlyPass 后面，即 ReligiousTension 的年度扫描)；同一年只算一次
    public static void YearlyPass()
    {
        if (World.world?.religions == null || ModClass.IS_CLEAR || ModClass.EMPIRE_MANAGER == null) return;
        double now = World.world.getCurWorldTime();
        if (_lastPass >= 0d && now >= _lastPass && Date.getYearsSince(_lastPass) < 1) return;
        _lastPass = now;
        List<Religion> faiths = World.world.religions.list.Where(religion => Alive(religion) && ReligionFaith.IsCaliphal(religion)).ToList();
        foreach (Religion religion in faiths)
        {
            Step("哈里发世系", religion, NoteCaliphHouse);
            Step("对立哈里发", religion, UpdateRivals);
            Step("奉哪位哈里发", religion, UpdateObedience);
            Step("另立哈里发", religion, TryProclaimRival);
            Step("复古运动", religion, UpdateMovements);
            Step("复古运动兴起", religion, TryReformMovement);
        }
        try
        {
            TryShiaTurns();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{Tag} 什叶派结算失败: {exception.Message}");
        }
    }

    private static void Step(string name, Religion religion, Action<Religion> step)
    {
        try
        {
            if (Alive(religion)) step(religion);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{Tag} {religion?.name} {name}结算失败: {exception.Message}");
        }
    }

    // 记下现任哈里发(圣地之主)的宗族
    private static void NoteCaliphHouse(Religion religion)
    {
        SpecificClan clan = PapacySystem.Pope(religion)?.GetSpecificClan();
        if (clan == null) return;
        IslamicSchismState state = State(religion);
        if (state.caliph_houses.Contains(clan.id)) return;
        state.caliph_houses.Add(clan.id);
        if (state.caliph_houses.Count > MaxHouses) state.caliph_houses.RemoveRange(0, state.caliph_houses.Count - MaxHouses);
    }

    #endregion

    #region 对立哈里发：宣告

    private static void TryProclaimRival(Religion religion)
    {
        Kingdom orthodox = PapacySystem.Papacy(religion);
        if (orthodox == null || ReligionSphere.IsLocalChurch(religion) || religion.getAge() < RivalMinAge) return;
        IslamicSchismState state = Peek(religion);
        if (state != null && (ActiveRivals(religion).Count >= MaxRivals || Within(state.last_rival_end_at, RivalCooldownYears))) return;
        Kingdom see = Realm(orthodox);
        int seeCities = Cities(see);
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (!Candidate(empire, religion, orthodox, see, seeCities, out bool hostile)) continue;
            float chance = hostile ? RivalHostileChance : RivalChance;
            if (PapacySystem.IsCrowned(empire)) chance *= RivalInvestedFactor;
            if (UnityEngine.Random.value >= chance) continue;
            if (TryFatimid(empire, religion) || ProclaimRival(religion, empire.CoreKingdom, "rival_caliph_proclaimed_log") != null) return;
        }
    }

    private static bool Candidate(Empire empire, Religion religion, Kingdom orthodox, Kingdom see, int seeCities, out bool hostile)
    {
        hostile = false;
        if (empire?.data == null || !CaliphateFaith.IsCaliphate(empire) || !PapacySystem.AppliesCaliphal(empire) ||
            PapacySystem.ReligionOf(empire) != religion) return false;
        Kingdom core = empire.CoreKingdom;
        Actor ruler = empire.Emperor;
        City capital = core.capital;
        if (!Alive(ruler) || !ruler.isAdult() || capital?.data == null || see == core || orthodox.GetEmpire() == empire ||
            SchismSystem.Active(empire) || AncientWarfareCompatibility.Owns(core) || ObedienceOf(empire, religion) != null ||
            empire.Legitimacy < RivalMinLegitimacy) return false;
        int cities = empire.countCities();
        if (cities < RivalMinCities || cities < seeCities * RivalStrengthShare) return false;
        // 同一国、同一帝国废后不久的不再立
        if (AllRivals(religion).Any(rival => rival.ended_at >= 0d && Within(rival.ended_at, SeatReproclaimYears) &&
                                             (rival.seat_empire_id == empire.id || rival.seat_kingdom_id == core.id))) return false;
        Empire twin = EmpirePartitionSystem.Twin(empire);
        Empire seeEmpire = see.GetEmpire();
        // 与哈里发座交战；哈里发之国衰微(正在闹更替之乱、正统低于 DeclineLegitimacy)；与哈里发座所在的那一朝分立相争
        hostile = core.isInWarWith(see) ||
                  seeEmpire != null && seeEmpire.CoreKingdom == see && (SchismSystem.Active(seeEmpire) || seeEmpire.Legitimacy < DeclineLegitimacy) ||
                  twin != null && !empire.data.partition_amity && seeEmpire == twin;
        City holy = PapacySystem.HolyCity(religion);
        bool far = holy != null && Vector2.Distance(capital.city_center, holy.city_center) >= Span * RivalFarShare;
        return hostile || far;
    }

    // 哈里发争位相持分裂(见 SchismSystem.Settle)：争位者离开帝国自立，仍奉本教的有 ContestRivalChance 自称哈里发
    public static void OnContestSplit(Empire empire, Kingdom claimant)
    {
        try
        {
            Religion religion = claimant?.religion;
            if (!Alive(claimant) || Realm(claimant) != claimant || !ReligionFaith.IsCaliphal(religion) ||
                religion != empire?.CoreKingdom?.religion || ReligionSphere.IsLocalChurch(religion)) return;
            Kingdom orthodox = PapacySystem.Papacy(religion);
            if (orthodox == null || Realm(orthodox) == claimant || ActiveRivals(religion).Count >= MaxRivals ||
                UnityEngine.Random.value >= ContestRivalChance) return;
            ProclaimRival(religion, claimant, "rival_caliph_contest_log", empire.GetEmpireFullName());
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{Tag} 争位者自称哈里发失败: {exception.Message}");
        }
    }

    // 立对立哈里发：seat 的君主自称哈里发(文字：{0} 国号、{1} 君主、{2} 都城、{3} 圣地、{4} 本教、{5} extra)
    public static RivalCaliphateData ProclaimRival(Religion religion, Kingdom seat, string logKey, string extra = "")
    {
        Actor ruler = seat?.king;
        City capital = seat?.capital;
        if (!Alive(seat) || !Alive(ruler) || capital?.data == null || capital.isRekt() || seat.religion != religion) return null;
        IslamicSchismState state = State(religion);
        Empire empire = seat.GetEmpire();
        if (empire != null && empire.CoreKingdom != seat) empire = null;
        SpecificClan clan = empire?.EmpireSpecificClan ?? ruler.GetSpecificClan();
        double now = Now;
        var rival = new RivalCaliphateData
        {
            id = OverallHelperFunc.IdGenerator.NextId(),
            seat_kingdom_id = seat.id,
            seat_empire_id = empire?.id ?? -1L,
            seat_city_id = capital.data.id,
            seat_name = capital.GetCityName(),
            realm_name = empire?.GetEmpireFullName() ?? seat.GetKingdomName(),
            founder_id = ruler.id,
            founder_name = ruler.getName(),
            clan_id = clan?.id ?? -1L,
            proclaimed_at = now
        };
        // 奉它的帝国：自立者本国(从别的哈里发名下移出)，当场算受册封
        if (empire != null)
        {
            foreach (RivalCaliphateData other in ActiveRivals(religion)) other.adherents?.Remove(empire.id);
            rival.adherents.Add(empire.id);
            InvestSelf(empire, ruler);
        }
        NoteHeads(rival);
        state.rivals.Add(rival);
        PapacySystem.AddAuthority(religion, -RivalAuthorityLoss);
        string holy = PapacySystem.HolyCity(religion)?.GetCityName() ?? LM.Get("papacy_see_lost");
        string text = string.Format(LM.Get(logKey), rival.realm_name, ruler.getName(), capital.GetCityName(), holy, religion.name, extra);
        EventRecorder.Record(seat, text, ruler);
        ruler.RecordPersonalHistory(text);
        ReligionChronicle.Record(religion, ReligionChronicle.KindSchism, text);
        LogService.LogInfo($"{Tag} {rival.realm_name} 的 {ruler.getName()} 以 {rival.seat_name} 自称哈里发(本教 {religion.name}，" +
                           $"圣地 {holy}，现有对立哈里发 {ActiveRivals(religion).Count} 位)");
        return rival;
    }

    // 法蒂玛式：先代哈里发之裔、民心不附、境内多什叶派的，改立什叶派(或伊斯玛仪派)而自为伊玛目-哈里发
    private static bool TryFatimid(Empire empire, Religion religion)
    {
        Kingdom core = empire.CoreKingdom;
        bool alid = Alid(religion, core.king);
        // 本就奉什叶派：伊玛目之争，另立伊斯玛仪派
        if (IsShia(religion))
        {
            if (State(religion).sub_split || UnityEngine.Random.value >= FatimidShare) return false;
            return FoundBranch(religion, core, BranchIsmaili, "fatimid_founded_log", null, FollowerShare, out _) != null;
        }
        if (!ReligionSphere.IsNative(religion)) return false;
        Religion shia = ShiaOf(religion);
        if (shia == null)
        {
            if (religion.GetOrCreate().ShiaSplit || !alid && !Unhappy(core) || UnityEngine.Random.value >= FatimidShare) return false;
            return FoundBranch(religion, core, BranchShia, "fatimid_founded_log", null, FollowerShare, out _) != null;
        }
        if (!alid && ShareFollowing(core, shia) < FatimidShiaShare || UnityEngine.Random.value >= FatimidShare) return false;
        // 什叶派的伊玛目之座已失：迎至本都
        if (PapacySystem.Papacy(shia) == null) return RestoreImamate(empire, shia, religion);
        if (State(shia).sub_split) return false;
        return FoundBranch(shia, core, BranchIsmaili, "fatimid_founded_log", null, FollowerShare, out _) != null;
    }

    // 什叶派的伊玛目之座已失(圣地不在奉它的国手里)：率国改奉，伊玛目之座迁到本都
    private static bool RestoreImamate(Empire empire, Religion shia, Religion old)
    {
        Kingdom core = empire.CoreKingdom;
        City capital = core.capital;
        Actor ruler = core.king;
        if (capital?.data == null || !Alive(ruler)) return false;
        ConvertRealm(core, old, shia, null, FollowerShare);
        shia.SetCity(capital);
        string text = string.Format(LM.Get("imamate_restored_log"), empire.GetEmpireFullName(), ruler.getName(), shia.name,
            capital.GetCityName(), old.name);
        EventRecorder.Record(empire, text, ruler);
        ReligionChronicle.Record(shia, old, ReligionChronicle.KindSchism, text);
        LogService.LogInfo($"{Tag} {empire.GetEmpireName()} 改奉{shia.name}，伊玛目之座迁到 {capital.GetCityName()}");
        return true;
    }

    // 自立者当场算受册封(加冕状态与 PapacySystem 同一份)
    private static void InvestSelf(Empire empire, Actor ruler)
    {
        PapalRelationState papal = PapacySystem.State(empire);
        if (papal == null || ruler == null) return;
        papal.crowned_emperor_id = ruler.id;
        papal.self_crowned = false;
        papal.crowned_at = Now;
        MonarchyLegitimacy.Invalidate(empire);
    }

    #endregion

    #region 对立哈里发：每年核对、终结、自成一派

    private static void UpdateRivals(Religion religion)
    {
        IslamicSchismState state = Peek(religion);
        if (state?.rivals == null) return;
        foreach (RivalCaliphateData rival in state.rivals.Where(rival => rival != null && rival.ended_at < 0d).ToList())
        {
            string end = Ending(religion, rival);
            if (end != null)
            {
                End(religion, rival, end, announce: true);
                continue;
            }
            NoteHeads(rival);
            PruneAdherents(religion, rival);
            if (!state.rival_branch_split && rival.proclaimed_at >= 0d && Now >= rival.proclaimed_at &&
                Date.getYearsSince(rival.proclaimed_at) >= RecognizeYears && UnityEngine.Random.value < RecognizeChance)
                Recognize(religion, rival);
        }
        // 只留最近的几条已终结的
        int ended = state.rivals.Count(rival => rival != null && rival.ended_at >= 0d);
        if (ended <= MaxRivalRecords) return;
        foreach (RivalCaliphateData old in state.rivals.Where(rival => rival != null && rival.ended_at >= 0d)
                     .OrderBy(rival => rival.ended_at).Take(ended - MaxRivalRecords).ToList())
            state.rivals.Remove(old);
    }

    // 终结的缘由(文字键后缀)；不终结返回 null。顺带让哈里发座随帝国迁中央、随本国迁都
    private static string Ending(Religion religion, RivalCaliphateData rival)
    {
        Kingdom seat = LiveKingdom(rival.seat_kingdom_id);
        Empire empire = LiveEmpire(rival.seat_empire_id);
        if (empire != null && Alive(empire.CoreKingdom) && seat != empire.CoreKingdom && (seat == null || seat.GetEmpire() == empire))
        {
            seat = empire.CoreKingdom;
            rival.seat_kingdom_id = seat.id;
        }
        if (seat == null) return "fallen";
        // 争位者自立的王国后来称了帝：记下帝国
        if (seat.GetEmpire() is Empire own && own.CoreKingdom == seat) rival.seat_empire_id = own.id;
        if (seat.religion != religion) return "faith";
        if (Realm(seat) != seat) return "submitted";
        City old = rival.seat_city_id >= 0 ? World.world.cities.get(rival.seat_city_id) : null;
        if (old?.data != null && !old.isRekt() && old.kingdom != null && !old.kingdom.wild && Realm(old.kingdom) != seat) return "conquered";
        if (seat.capital?.data != null && !seat.capital.isRekt()) rival.seat_city_id = seat.capital.data.id;
        Kingdom orthodox = PapacySystem.Papacy(religion);
        if (orthodox != null && Realm(orthodox) == seat) return "reunited";
        if (!RegimeManager.IsMonarchy(seat.GetRegime()?.type) || seat.GetEmpire() is Empire realm && RepublicSystem.IsRepublic(realm))
            return "abolished";
        return LineEnded(rival, seat) ? "line" : null;
    }

    // 易姓：帝国看皇室宗族，独立王国看君主宗族；与自立者不同世系即绝。宗族不可考(空)的不判
    private static bool LineEnded(RivalCaliphateData rival, Kingdom seat)
    {
        if (rival.clan_id < 0) return false;
        Empire empire = seat.GetEmpire();
        SpecificClan current = empire != null && empire.CoreKingdom == seat ? empire.EmpireSpecificClan : seat.king?.GetSpecificClan();
        if (current == null || current.id == rival.clan_id) return false;
        SpecificClan founder = SpecificClanManager.Get(rival.clan_id);
        return founder == null || !SpecificClanManager.SameLineage(current, founder);
    }

    private static void End(Religion religion, RivalCaliphateData rival, string end, bool announce)
    {
        IslamicSchismState state = State(religion);
        double now = Now;
        rival.ended_at = now;
        rival.end = end;
        state.last_rival_end_at = now;
        CloseHead(rival, now);
        // 奉它的帝国：册封作废，此后向圣地哈里发求册封(复归为一的，哈里发还是那一位；自成一派的另有归宿)
        if (end != "reunited" && end != "recognized")
            foreach (long id in rival.adherents?.ToList() ?? new List<long>())
            {
                Empire adherent = LiveEmpire(id);
                PapalRelationState papal = adherent == null ? null : PapacySystem.State(adherent);
                if (papal == null || papal.crowned_emperor_id < 0) continue;
                papal.crowned_emperor_id = -1L;
                MonarchyLegitimacy.Invalidate(adherent);
            }
        int count = rival.adherents?.Count ?? 0;
        rival.adherents?.Clear();
        if (end is "conquered" or "line" or "reunited" or "fallen" or "submitted") PapacySystem.AddAuthority(religion, RivalEndAuthority);
        LogService.LogInfo($"{Tag} {rival.realm_name}(都 {rival.seat_name})的对立哈里发终结：{end}，" +
                           $"历 {Mathf.Max(0, Date.getYearsSince(rival.proclaimed_at))} 年，奉之者 {count} 国");
        if (!announce) return;
        string text = string.Format(LM.Get("rival_caliph_end_" + end), rival.realm_name, rival.seat_name,
            PapacySystem.HolyCity(religion)?.GetCityName() ?? LM.Get("papacy_see_lost"), religion.name);
        Kingdom seat = LiveKingdom(rival.seat_kingdom_id);
        if (seat != null) EventRecorder.Record(seat, text);
        else TranslateHelper.LogEventMessage(text, PapacySystem.Papacy(religion));
        ReligionChronicle.Record(religion, ReligionChronicle.KindSchism, text);
    }

    // 自成一派：以对立哈里发的都城为圣地另立分支，本国与同奉旧教的成员当场改奉，奉它的帝国中央改奉
    private static void Recognize(Religion religion, RivalCaliphateData rival)
    {
        Kingdom seat = Seat(rival);
        if (seat == null) return;
        List<Empire> adherents = (rival.adherents ?? new List<long>()).Select(LiveEmpire)
            .Where(empire => empire != null && empire.CoreKingdom != seat && Alive(empire.CoreKingdom) &&
                             empire.CoreKingdom.religion == religion && Alive(empire.CoreKingdom.king)).ToList();
        Religion branch = FoundBranch(religion, seat, BranchRival, "rival_caliph_branch_log", null, 1f, out _);
        if (branch == null) return;
        foreach (Empire adherent in adherents)
            if (CaliphateFaith.MayAdopt(adherent.CoreKingdom, branch))
                ReligionConversionSystem.Convert(adherent.CoreKingdom, branch, religion, "rival_branch_follow_log");
        // 教随国定：母宗教此后不讨伐这一派(见 PapacySystem.HereticTarget)
        ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
        data.ReligiousPeaceUntil = Math.Max(data.ReligiousPeaceUntil, Now + RecognizedPeaceYears * SecondsPerYear);
        rival.branch_religion_id = branch.data.id;
        End(religion, rival, "recognized", announce: false);
    }

    private static void PruneAdherents(Religion religion, RivalCaliphateData rival)
    {
        rival.adherents ??= new List<long>();
        // 先挑出来再删：AppliesCaliphal 会经 ObedienceOf 读这份名单
        foreach (long id in rival.adherents.ToList())
        {
            Empire empire = LiveEmpire(id);
            if (empire == null || PapacySystem.ReligionOf(empire) != religion || !PapacySystem.AppliesCaliphal(empire))
                rival.adherents.Remove(id);
        }
    }

    // 历任对立哈里发(同 ReligionHeads.Note 的写法)
    private static void NoteHeads(RivalCaliphateData rival)
    {
        rival.heads ??= new List<ReligionExtension.ReligionHeadRecord>();
        Actor head = Caliph(rival);
        ReligionExtension.ReligionHeadRecord last = rival.heads.Count > 0 ? rival.heads[rival.heads.Count - 1] : null;
        bool reigning = last != null && last.to < 0d;
        if (reigning && head != null && last.actor_id == head.id) return;
        if (!reigning && head == null) return;
        double now = Now;
        if (reigning)
        {
            Actor former = World.world.units.get(last.actor_id);
            bool alive = Alive(former);
            if (alive) last.name = former.getName();
            last.to = now;
            last.end = !alive ? "religion_head_end_died" : head == null ? "religion_head_end_see_lost" : "religion_head_end_deposed";
        }
        if (head == null) return;
        rival.heads.Add(new ReligionExtension.ReligionHeadRecord { actor_id = head.id, name = head.getName(), from = now });
        if (rival.heads.Count > ReligionHeads.MaxHeads) rival.heads.RemoveRange(0, rival.heads.Count - ReligionHeads.MaxHeads);
    }

    private static void CloseHead(RivalCaliphateData rival, double now)
    {
        ReligionExtension.ReligionHeadRecord last = rival.heads?.Count > 0 ? rival.heads[rival.heads.Count - 1] : null;
        if (last == null || last.to >= 0d) return;
        Actor former = World.world.units.get(last.actor_id);
        if (Alive(former)) last.name = former.getName();
        last.to = now;
        last.end = Alive(former) ? "rival_caliph_head_end" : "religion_head_end_died";
    }

    #endregion

    #region 奉哪位哈里发

    private static void UpdateObedience(Religion religion)
    {
        List<RivalCaliphateData> active = ActiveRivals(religion);
        if (active.Count == 0) return;
        Kingdom orthodox = PapacySystem.Papacy(religion);
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || PapacySystem.ReligionOf(empire) != religion ||
                !PapacySystem.AppliesCaliphal(empire)) continue;
            Kingdom core = empire.CoreKingdom;
            RivalCaliphateData current = ObedienceOf(empire, religion);
            RivalCaliphateData chosen;
            RivalCaliphateData own = active.FirstOrDefault(rival => Seat(rival) == core);
            if (own != null) chosen = own;
            else if (orthodox != null && Realm(orthodox) == core) chosen = null;
            else
            {
                // 已受册封、与所奉的哈里发不交战的不改；新君未受册封、或与所奉的哈里发反目的重新择一
                Kingdom seat = current != null ? Seat(current) : orthodox;
                if (PapacySystem.IsCrowned(empire) && Acceptable(empire, religion, seat)) continue;
                chosen = Choose(empire, religion, active, current, orthodox);
            }
            if (chosen != current) Switch(empire, religion, current, chosen);
        }
    }

    private static RivalCaliphateData Choose(Empire empire, Religion religion, List<RivalCaliphateData> active,
        RivalCaliphateData current, Kingdom orthodox)
    {
        Kingdom core = empire.CoreKingdom;
        RivalCaliphateData best = null;
        float bestScore = float.MaxValue;
        if (Acceptable(empire, religion, orthodox))
            bestScore = Distance(core, orthodox) - OrthodoxPull - (current == null ? StayPull : 0f);
        foreach (RivalCaliphateData rival in active)
        {
            Kingdom seat = Seat(rival);
            if (!Acceptable(empire, religion, seat)) continue;
            float score = Distance(core, seat) - (rival == current ? StayPull : 0f);
            if (score >= bestScore) continue;
            best = rival;
            bestScore = score;
        }
        return best;
    }

    // 奉得了：奉本教、不与本国交战、不是分立相争的另一朝
    private static bool Acceptable(Empire empire, Religion religion, Kingdom seat)
    {
        if (!Alive(seat) || seat.religion != religion || seat.isInWarWith(empire.CoreKingdom)) return false;
        Empire twin = EmpirePartitionSystem.Twin(empire);
        return twin == null || empire.data.partition_amity || seat.GetEmpire() != twin;
    }

    private static float Distance(Kingdom from, Kingdom to)
    {
        City a = from?.capital, b = to?.capital;
        return a == null || b == null ? 1f : Vector2.Distance(a.city_center, b.city_center) / Span;
    }

    // 改奉：从原哈里发名下移到新的；先前的册封作废(自立者本国当场算受册封)；史书、世界日志、本教大事记各记一笔
    private static void Switch(Empire empire, Religion religion, RivalCaliphateData from, RivalCaliphateData to)
    {
        from?.adherents?.Remove(empire.id);
        if (to != null)
        {
            to.adherents ??= new List<long>();
            if (!to.adherents.Contains(empire.id)) to.adherents.Add(empire.id);
        }
        Kingdom seat = to != null ? Seat(to) : PapacySystem.Papacy(religion);
        bool home = seat != null && (seat == empire.CoreKingdom || seat.GetEmpire() == empire);
        if (to != null && home)
        {
            InvestSelf(empire, empire.Emperor);
            return;
        }
        PapalRelationState papal = PapacySystem.State(empire);
        if (papal != null && papal.crowned_emperor_id >= 0)
        {
            papal.crowned_emperor_id = -1L;
            MonarchyLegitimacy.Invalidate(empire);
        }
        // 圣地在本帝国：就地受封(见 PapacySystem.CaliphalYear)，不另记
        if (home) return;
        string text = string.Format(LM.Get("caliph_obedience_log"), empire.GetEmpireFullName(),
            seat?.capital?.GetCityName() ?? to?.seat_name ?? LM.Get("papacy_see_lost"), seat?.king?.getName() ?? LM.Get("label_none"));
        EventRecorder.Record(empire, text);
        ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, text);
    }

    #endregion

    #region 什叶派(争位之外)

    private static void TryShiaTurns()
    {
        foreach (Kingdom realm in World.world.kingdoms.list.ToList())
        {
            if (!Alive(realm) || Realm(realm) != realm || !realm.hasReligion() || AncientWarfareCompatibility.Owns(realm)) continue;
            Religion faith = realm.religion;
            Actor king = realm.king;
            if (!ReligionFaith.IsCaliphal(faith) || !ReligionSphere.IsNative(faith) || ReligionSphere.IsLocalChurch(faith) ||
                !Alive(king) || !king.isAdult() || realm.capital == null || !RegimeManager.IsMonarchy(realm.GetRegime()?.type) ||
                PapacySystem.HoldsHolyCity(realm) || RivalSeatedIn(faith, realm) != null) continue;
            Kingdom orthodox = PapacySystem.Papacy(faith);
            if (orthodox != null && Realm(orthodox) == realm) continue;
            try
            {
                if (faith.GetOrCreate().ShiaSplit) TryAdoptShia(realm, faith, orthodox);
                else TryEstablishShia(realm, faith, orthodox);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"{Tag} {realm.GetKingdomName()} 什叶派结算失败: {exception.Message}");
            }
        }
    }

    // 立什叶派：先代哈里发之裔、与哈里发座交战、民心不附
    private static void TryEstablishShia(Kingdom realm, Religion faith, Kingdom orthodox)
    {
        if (faith.getAge() < ShiaMinAge || Cities(realm) < ShiaMinCities) return;
        bool alid = Alid(faith, realm.king);
        bool hostile = orthodox != null && realm.isInWarWith(Realm(orthodox));
        if (!alid && !hostile) return;
        bool unhappy = Unhappy(realm);
        float chance = alid && (hostile || unhappy) ? ShiaAlidChance : hostile && unhappy ? ShiaGrievanceChance : alid ? ShiaClaimChance : 0f;
        if (chance <= 0f || UnityEngine.Random.value >= chance) return;
        FoundBranch(faith, realm, BranchShia, alid ? "shia_founded_alid_log" : "shia_founded_log", null, FollowerShare, out _);
    }

    // 改奉已有的什叶派：治下多奉什叶派(民心所向)，或先代哈里发之裔与哈里发座交战
    private static void TryAdoptShia(Kingdom realm, Religion faith, Kingdom orthodox)
    {
        Religion shia = ShiaOf(faith);
        if (shia == null || !CaliphateFaith.MayAdopt(realm, shia) || Within(realm.GetOrCreate().religion_converted_at, AdoptCooldownYears))
            return;
        float share = ShareFollowing(realm, shia);
        float chance = share >= ShiaAdoptMajority ? ShiaAdoptMajorityChance : share >= ShiaAdoptMinority ? ShiaAdoptMinorityChance : 0f;
        if (chance <= 0f && orthodox != null && realm.isInWarWith(Realm(orthodox)) && Alid(faith, realm.king)) chance = ShiaAdoptClaimChance;
        if (chance <= 0f || UnityEngine.Random.value >= chance) return;
        ReligionConversionSystem.Convert(realm, shia, faith, "shia_adopt_log");
        LogService.LogInfo($"{Tag} {realm.GetKingdomName()} 改奉{shia.name}(治下奉之者 {share:P0})");
    }

    // 争位分出的什叶派(见 ReligionSchismSystem.OnSuccessionContest)：记下分支，此后同一母宗教不再另立
    public static void MarkShia(Religion parent, Religion shia)
    {
        if (!Alive(parent) || !Alive(shia)) return;
        State(shia).branch = BranchShia;
        State(parent).shia_id = shia.data.id;
    }

    // 什叶派的争位(见 ReligionSchismSystem.OnSuccessionContest)：争位者另立伊斯玛仪派，随攻方的成员同改
    public static Religion OnShiaContest(Religion shia, Kingdom claimant, List<Kingdom> followers, out string text)
    {
        text = null;
        if (!IsShia(shia) || State(shia).sub_split || UnityEngine.Random.value >= ContestIsmailiChance) return null;
        return FoundBranch(shia, claimant, BranchIsmaili, "ismaili_split_log", followers, 0f, out text);
    }

    #endregion

    #region 立分支

    // 立伊斯兰分支：圣地在 realm 的都城，realm 的君主即其伊玛目(哈里发座)。realm 当场改奉；realm 是帝国中央的，同奉旧教的成员
    // 各 memberShare 随之；followers 同改。文字：{0} 国号、{1} 君主、{2} 都城、{3} 新分支、{4} 原奉之教
    public static Religion FoundBranch(Religion parent, Kingdom realm, string branch, string logKey, IEnumerable<Kingdom> followers,
        float memberShare, out string text)
    {
        text = null;
        Actor head = realm?.king;
        City capital = realm?.capital;
        if (!Alive(parent) || !Alive(realm) || !Alive(head) || capital?.data == null || capital.isRekt()) return null;
        Religion old = realm.hasReligion() ? realm.religion : parent;
        if (head.city != capital) head.joinCity(capital);
        Religion faith = World.world.religions.newReligion(head, false);
        if (faith?.data == null) return null;
        faith.data.name = branch == BranchRival
            ? string.Format(LM.Get("caliph_sect_name"), capital.GetCityName())
            : ReligionFaith.PickName(branch, branch == BranchIsmaili ? 2 : 1) ?? faith.data.name;
        faith.SetCity(capital);
        ReligionExtension.ReligionExtraData data = faith.GetOrCreate();
        data.ParentReligionId = parent.data.id;
        data.SplitAt = Now;
        data.Faith = FaithType.Islam.ToString();
        data.Sphere = ReligionSphere.SphereOf(parent);
        State(faith).branch = branch;
        IslamicSchismState parentState = State(parent);
        if (branch == BranchShia)
        {
            parent.GetOrCreate().ShiaSplit = true;
            parentState.shia_id = faith.data.id;
        }
        else if (branch == BranchIsmaili) parentState.sub_split = true;
        else if (branch == BranchRival) parentState.rival_branch_split = true;
        List<Kingdom> converted = ConvertRealm(realm, old, faith, followers, memberShare);
        if (branch != BranchRival) PapacySystem.AddAuthority(old, -SplitAuthorityLoss);
        Empire empire = realm.GetEmpire();
        string name = empire != null && empire.CoreKingdom == realm ? empire.GetEmpireFullName() : realm.GetKingdomName();
        text = string.Format(LM.Get(logKey), name, head.getName(), capital.GetCityName(), faith.name, old.name);
        EventRecorder.Record(realm, text, head);
        head.RecordPersonalHistory(text);
        // 创教时(ReligionPatch)记下的那条用的是改名前的名字，由分裂这条起头
        ReligionChronicle.Clear(faith);
        ReligionChronicle.Record(parent, faith, ReligionChronicle.KindSchism, text);
        if (old != parent) ReligionChronicle.Record(old, ReligionChronicle.KindSchism, text);
        LogService.LogInfo($"{Tag} {name} 另立{faith.name}({branch}，分自{parent.name}，原奉{old.name})，改奉 {converted.Count} 国");
        return faith;
    }

    // 一国(连同帝国内随之的成员、指明的随从)改奉：国、君主、奉旧教的城
    private static List<Kingdom> ConvertRealm(Kingdom realm, Religion old, Religion faith, IEnumerable<Kingdom> followers, float memberShare)
    {
        var converted = new List<Kingdom> { realm };
        ConvertKingdom(realm, old, faith);
        Empire empire = realm.GetEmpire();
        if (empire != null && empire.CoreKingdom == realm)
        {
            empire.Religion = faith;
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (!Alive(member) || member == realm || member.religion != old || UnityEngine.Random.value >= memberShare) continue;
                ConvertKingdom(member, old, faith);
                converted.Add(member);
            }
        }
        foreach (Kingdom follower in followers ?? Enumerable.Empty<Kingdom>())
        {
            if (!Alive(follower) || converted.Contains(follower) || follower.religion != old) continue;
            ConvertKingdom(follower, old, faith);
            converted.Add(follower);
        }
        realm.GetOrCreate().religion_converted_at = Now;
        return converted;
    }

    private static void ConvertKingdom(Kingdom kingdom, Religion old, Religion faith)
    {
        kingdom.setReligion(faith);
        Actor king = kingdom.king;
        if (Alive(king) && (king.religion == old || !king.hasReligion())) king.setReligion(faith);
        foreach (City city in kingdom.cities.ToList())
            if (city?.data != null && !city.isRekt() && city.religion == old) BackgroundReligionSystem.Convert(city, faith, old);
    }

    #endregion

    #region 复古运动

    private static void TryReformMovement(Religion religion)
    {
        IslamicSchismState state = Peek(religion);
        if (state?.movements != null && (state.movements.Any(movement => movement != null && movement.stage < 2) ||
                                         Within(state.last_movement_at, MovementCooldownYears))) return;
        foreach (Kingdom realm in World.world.kingdoms.list.ToList())
        {
            if (!Alive(realm) || Realm(realm) != realm || realm.religion != religion || AncientWarfareCompatibility.Owns(realm) ||
                !Lax(realm, religion)) continue;
            foreach (City city in RealmCities(realm).ToList())
            {
                if (city.religion != religion || !ReligiousTension.CanRevolt(city) || UnityEngine.Random.value >= MovementChance) continue;
                if (StartMovement(religion, realm, city)) return;
            }
        }
    }

    // 宽纵失教：王朝末世、苛政、与所奉的哈里发交战、治下多奉异教
    private static bool Lax(Kingdom realm, Religion religion)
    {
        Empire empire = realm.GetEmpire();
        bool core = empire != null && empire.CoreKingdom == realm;
        if (core && DynasticCycleSystem.InCrisis(empire)) return true;
        float burden = core ? HarshRuleSystem.GetBurden(empire) : HarshRuleSystem.GetBurden(realm);
        if (burden >= LaxHarshBurden) return true;
        Kingdom seat = core ? SeatFor(empire, religion) : PapacySystem.Papacy(religion);
        if (seat != null && Realm(seat) != realm && realm.isInWarWith(Realm(seat))) return true;
        int total = 0, other = 0;
        foreach (City city in RealmCities(realm))
        {
            total++;
            if (city.hasReligion() && city.religion != religion) other++;
        }
        return total > 0 && other >= total * LaxMinorityShare;
    }

    private static bool StartMovement(Religion religion, Kingdom realm, City city)
    {
        string name = LM.Get("reform_movement_name_" + UnityEngine.Random.Range(1, MovementNames + 1));
        string cityName = city.GetCityName();
        Kingdom origin = city.kingdom;
        string history = string.Format(LM.Get("reform_revolt_history"), cityName, name, realm.GetKingdomName());
        if (!ReligiousTension.StartRevolt(city, "rebellion_reason_islamic_reform", name,
                string.Format(LM.Get("reform_revolt_kingdom_name"), cityName, name),
                string.Format(LM.Get("reform_revolt_war_name"), cityName, name), history)) return false;
        Kingdom rebel = city.kingdom;
        if (!Alive(rebel) || rebel == origin) return false;
        IslamicSchismState state = State(religion);
        double now = Now;
        state.movements.Add(new ReformMovementData
        {
            id = OverallHelperFunc.IdGenerator.NextId(),
            name = name,
            kingdom_id = rebel.id,
            origin_kingdom_id = realm.id,
            city_name = cityName,
            founded_at = now
        });
        state.last_movement_at = now;
        int ended = state.movements.Count(movement => movement != null && movement.stage >= 2);
        if (ended > MaxMovementRecords)
            foreach (ReformMovementData old in state.movements.Where(movement => movement != null && movement.stage >= 2)
                         .OrderBy(movement => movement.ended_at).Take(ended - MaxMovementRecords).ToList())
                state.movements.Remove(old);
        ReligionChronicle.Record(religion, ReligionChronicle.KindSchism, history);
        LogService.LogInfo($"{Tag} {realm.GetKingdomName()} {cityName} {name}起事(本教 {religion.name})");
        return true;
    }

    private static void UpdateMovements(Religion religion)
    {
        IslamicSchismState state = Peek(religion);
        if (state?.movements == null) return;
        foreach (ReformMovementData movement in state.movements.Where(movement => movement != null && movement.stage < 2).ToList())
        {
            Kingdom rebel = LiveKingdom(movement.kingdom_id);
            if (rebel == null)
            {
                EndMovement(religion, movement, "reform_movement_crushed_log", LiveKingdom(movement.origin_kingdom_id));
                continue;
            }
            // 改了宗、归附了别国：运动就此散去
            if (rebel.religion != religion || Realm(rebel) != rebel)
            {
                EndMovement(religion, movement, null, null);
                continue;
            }
            if (movement.stage == 0)
            {
                Kingdom origin = LiveKingdom(movement.origin_kingdom_id);
                if (rebel.IsLocalRebelling() || origin != null && rebel.isInWarWith(Realm(origin))) continue;
                BecomeJihadState(religion, movement, rebel);
                continue;
            }
            if (movement.state_at >= 0d && Now >= movement.state_at && Date.getYearsSince(movement.state_at) >= JihadYears ||
                movement.jihads >= MaxJihads)
            {
                EndMovement(religion, movement, "reform_movement_settled_log", rebel);
                continue;
            }
            if (rebel.hasEnemies() || UnityEngine.Random.value >= JihadChance) continue;
            DeclareJihad(religion, movement, rebel);
        }
    }

    // 起事成功：圣战之国——本教教权回升，国内政教合一
    private static void BecomeJihadState(Religion religion, ReformMovementData movement, Kingdom state)
    {
        movement.stage = 1;
        movement.state_at = Now;
        PapacySystem.AddAuthority(religion, ZealAuthority);
        Regime regime = state.GetRegime();
        if (regime?.options != null && regime.options.ContainsKey("option_religion_type") && regime.GetReligionLevel() < ReligionLevel.High)
            regime.SetReligionLevel(ReligionLevel.High);
        string text = string.Format(LM.Get("reform_jihad_state_log"), state.GetKingdomName(), movement.name, religion.name);
        EventRecorder.Record(state, text);
        ReligionChronicle.Record(religion, ReligionChronicle.KindHolyWar, text);
        LogService.LogInfo($"{Tag} {movement.name}起事告成，{state.GetKingdomName()} 成圣战之国");
    }

    // 圣战：接壤(或隔海相望)、城数不过本国 JihadTargetRatio 倍的——先异教、后别的伊斯兰教派、再宽纵的故国，同档挑城多的
    private static void DeclareJihad(Religion religion, ReformMovementData movement, Kingdom state)
    {
        Kingdom origin = Realm(LiveKingdom(movement.origin_kingdom_id));
        int own = Mathf.Max(1, Cities(state));
        Kingdom target = null;
        int rank = int.MaxValue, size = -1;
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (!Alive(kingdom) || Realm(kingdom) != kingdom || kingdom == state || !kingdom.hasReligion()) continue;
            int order = !ReligionFaith.IsCaliphal(kingdom.religion) ? 0 : kingdom.religion != religion ? 1 : kingdom == origin ? 2 : -1;
            if (order < 0 || order > rank) continue;
            int cities = Cities(kingdom);
            if (cities > own * JihadTargetRatio || order == rank && cities <= size || !StraitReach.Within(state, kingdom)) continue;
            target = kingdom;
            rank = order;
            size = cities;
        }
        if (target == null) return;
        War war = World.world.diplomacy.startWar(state, target, WarTypeLibrary.normal);
        if (war?.data == null) return;
        war.SetEmpireWarType(EmpireWarType.神圣, pre: religion.name);
        WarGoalSystem.Preset(war, WarGoal.割地);
        war.data.name = string.Format(LM.Get("reform_jihad_war_name"), movement.name, target.GetKingdomName());
        movement.jihads++;
        string text = string.Format(LM.Get("reform_jihad_declared_log"), state.GetKingdomName(), movement.name, target.GetKingdomName());
        EventRecorder.Record(state, text);
        ReligionChronicle.Record(religion, ReligionChronicle.KindHolyWar, text);
        LogService.LogInfo($"{Tag} {state.GetKingdomName()}({movement.name})第 {movement.jihads} 次圣战：伐 {target.GetKingdomName()}");
    }

    private static void EndMovement(Religion religion, ReformMovementData movement, string key, Kingdom witness)
    {
        movement.stage = 2;
        movement.ended_at = Now;
        if (key == null) return;
        string text = string.Format(LM.Get(key), movement.name, movement.city_name);
        if (witness != null) EventRecorder.Record(witness, text);
        ReligionChronicle.Record(religion, ReligionChronicle.KindSchism, text);
    }

    #endregion

    #region 内部

    // 一国的城数(帝国中央按全境)
    private static int Cities(Kingdom realm)
    {
        if (!Alive(realm)) return 0;
        Empire empire = realm.GetEmpire();
        return empire != null && empire.CoreKingdom == realm ? empire.countCities() : realm.cities.Count;
    }

    // 一国治下的城(帝国中央按全体成员)
    private static IEnumerable<City> RealmCities(Kingdom realm)
    {
        Empire empire = realm.GetEmpire();
        List<Kingdom> members = empire != null && empire.CoreKingdom == realm ? empire.kingdoms_list.ToList() : new List<Kingdom> { realm };
        foreach (Kingdom member in members)
        {
            if (!Alive(member)) continue;
            foreach (City city in member.cities.ToList())
                if (city?.data != null && !city.isRekt()) yield return city;
        }
    }

    // 治下奉 faith 的人口占比
    private static float ShareFollowing(Kingdom realm, Religion faith)
    {
        float total = 0f, followers = 0f;
        foreach (City city in RealmCities(realm))
        {
            float people = Mathf.Max(1, city.CountLivingPopulation());
            total += people;
            if (city.religion == faith) followers += people;
        }
        return total <= 0f ? 0f : followers / total;
    }

    // 民心不附：治下 UnhappyShare 以上的城稳定低于起事线，或王朝已入末世
    private static bool Unhappy(Kingdom realm)
    {
        Empire empire = realm.GetEmpire();
        if (empire != null && empire.CoreKingdom == realm && DynasticCycleSystem.InCrisis(empire)) return true;
        int total = 0, restless = 0;
        foreach (City city in RealmCities(realm))
        {
            total++;
            if (CityStabilitySystem.Effective(city) < CityStabilityRules.RebellionThreshold) restless++;
        }
        return total > 0 && restless >= total * UnhappyShare;
    }

    #endregion

    #region 窗口用

    // 分支的叫法(什叶派 / 伊斯玛仪派 / 对立哈里发自成一派)；本教或未记为 ""
    public static string BranchLabel(Religion religion)
    {
        string branch = BranchOf(religion);
        return branch.Length == 0 ? "" : LM.Get("islam_branch_" + branch);
    }

    // 帝国奉哪位哈里发(教廷页的帝国一行)：本教没有对立哈里发时为 ""
    public static string ObedienceLabel(Empire empire)
    {
        Religion religion = PapacySystem.ReligionOf(empire);
        if (religion == null || ActiveRivals(religion).Count == 0) return "";
        RivalCaliphateData rival = ObedienceOf(empire, religion);
        if (rival == null) return LM.Get("caliph_obedience_orthodox");
        Kingdom seat = Seat(rival);
        if (seat != null && seat == empire.CoreKingdom) return LM.Get("caliph_obedience_self");
        return string.Format(LM.Get("caliph_obedience_rival"), seat?.capital?.GetCityName() ?? rival.seat_name);
    }

    // 对立哈里发一行："{国号} · 哈里发 {某} · 自 {年} · 奉之者 {n} 国"
    public static string RivalSummary(RivalCaliphateData rival)
    {
        Kingdom seat = Seat(rival);
        string realm = seat == null ? rival.realm_name
            : seat.GetEmpire() is Empire empire && empire.CoreKingdom == seat ? empire.GetEmpireFullName() : seat.GetKingdomName();
        return string.Format(LM.Get("rival_caliph_row"), realm, Caliph(rival)?.getName() ?? LM.Get("label_none"),
            HistoryDateFormatter.GetYear(rival.proclaimed_at), rival.adherents?.Count ?? 0, seat?.capital?.GetCityName() ?? rival.seat_name);
    }

    // 已终结的对立哈里发一行："{国号}(都 {城}) · {起}—{止} · {缘由}"
    public static string RivalEndedSummary(RivalCaliphateData rival) =>
        string.Format(LM.Get("rival_caliph_ended_row"), rival.realm_name, rival.seat_name, HistoryDateFormatter.GetYear(rival.proclaimed_at),
            HistoryDateFormatter.GetYear(rival.ended_at), LM.Get("rival_caliph_end_label_" + rival.end));

    // 复古运动一行："{名}(起于 {城}) · {阶段} · 圣战 {n} 次"
    public static string MovementSummary(ReformMovementData movement) =>
        string.Format(LM.Get("reform_movement_row"), movement.name, movement.city_name, LM.Get("reform_movement_stage_" + movement.stage),
            movement.jihads, LiveKingdom(movement.kingdom_id)?.GetKingdomName() ?? LM.Get("label_none"));

    public static List<ReformMovementData> Movements(Religion religion) =>
        Peek(religion)?.movements?.Where(movement => movement != null).ToList() ?? new List<ReformMovementData>();

    #endregion
}
