using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// Puppet：割地立傀儡(近代割他国本土，城交给战胜国这一片的傀儡政府，见 OccupationPuppetSystem)；追加在末尾
// Protectorate 立为保护国、Lease 租借某城、Mandate 移交殖民地(委任统治，city_id 记那块殖民地的首府)：属地条款，见 ColonySystem
public enum PeaceTermKind { City, Indemnity, Tribute, Vassal, Install, Trade, Convert, Puppet, Protectorate, Lease, Mandate }

// 和约里的一项：谁出(by_attacker = 攻方出)、出什么
public sealed class PeaceTerm
{
    public PeaceTermKind kind;
    public bool by_attacker;
    public long city_id = -1L;
    public int tier = PeaceTermRules.StandardIndemnity;
    public string resource;
    // 神在台子上新加的、撮合添上的(界面标"新加")
    public bool added;

    public string Key => $"{(int)kind}:{(by_attacker ? 1 : 0)}:{city_id}:{tier}:{resource}";

    // 同一方出的同类条款只能有一项(赔款换档、岁供换矿都是替换)；城各算各的，同一座城割让、立傀儡二者取一
    public string Slot => IsCity ? $"city:{(by_attacker ? 1 : 0)}:{city_id}"
        : kind == PeaceTermKind.Mandate ? Key : $"{(int)kind}:{(by_attacker ? 1 : 0)}";

    // 关于一座城的条款：割让、立傀儡、租借，同一座城三者取一
    public bool IsCity => kind is PeaceTermKind.City or PeaceTermKind.Puppet or PeaceTermKind.Lease;

    public PeaceTerm Copy() => (PeaceTerm)MemberwiseClone();
}

public sealed class PeaceDeal
{
    public readonly List<PeaceTerm> terms = new();

    public PeaceDeal Copy()
    {
        var deal = new PeaceDeal();
        deal.terms.AddRange(terms.Select(term => term.Copy()));
        return deal;
    }

    public bool Has(PeaceTerm term) => terms.Exists(item => item.Key == term.Key);
}

// 一方的意愿账：意愿 = 所得 − 所失 + 战局 + 疲敝 − 势头 − 死战 − 民族意识(见 PeaceTermRules 文件头与民族意识一节)
public sealed class PeaceOpinion
{
    public int gain, loss, standing, wear, momentum, desperate, nation;
    public readonly List<(PeaceTerm term, int value)> gains = new(), losses = new(), nations = new();
    public int Total => gain - loss + standing + wear - momentum - desperate - nation;
    public bool Accepts => Total >= 0;
}

// 与台上条款无关、一次算好的底子：两边的筹码、疲敝、势头，全境价值，条款折价的缓存
public sealed class PeaceBasis
{
    public PeaceTermRules.Leverage attacker, defender;
    public PeaceTermRules.Wear attackerWear, defenderWear;
    public int attackerMomentum, defenderMomentum;
    public int attackerRealm, defenderRealm;
    public bool attackerCollapsing, defenderCollapsing;
    internal readonly Dictionary<string, int> values = new();
}

// 议和台(用户 2026-11-10"要不要照着全战三国的逻辑做""玩家可以添加筹码""添加双方的""也可以取消"
// "感觉目前的还是只能给看和就改个目标"；问答定：单独一页「议和」、两国都肯才能签另有强令、AI 也按台子议和；
// 示意图 Tasks/ui-mockups/peace-table.png 定稿)：
//   · 两国各出条件(割城、赔款、岁供、称臣、扶立、通商、改宗)，每项按 PeaceTermRules 折价，算两国各自肯不肯；
//   · 战争窗口的议和页上神可以加、撤、照目标开价、撮合；两国都肯才能"签约"，"强令签约"不看意愿、不要代价、记一条神力裁定；
//   · 两国自己议和(Talks)：满最短战期后每年一次，主攻方照战争目标开价，撮合到两国都肯就签；统一战争、法统内战、
//     开国雄主之战、剧本点名的仗、全面战争照旧打到底；
//   · 别的路子以议和收场的(十年强制停战、攻方罢兵、欠饷罢兵、外交面板停战)也不再按"谁多得一城"套战争目标，
//     而是照目标开价后撮合出两边最接近的折中(Conclude)；
//   · 分出胜负(攻下主城池、一方认输)照旧按战争目标结算，不进台子。
// 台子只放主攻、主守两国之间的条件；盟国随和约退出。
public static class PeaceTable
{
    private enum Source { Talks, Signed, Forced }

    // 神在议和页上摆的台子(只在这一局里，不进存档)
    private static readonly Dictionary<long, PeaceDeal> Drafts = new();
    // 已经签下、等 end_war 结算的
    private static readonly Dictionary<long, (PeaceDeal deal, Source source)> Concluded = new();

    // 撮合最多走几步
    private const int BalanceSteps = 30;
    // 岁供列几种矿
    private const int TributeChoices = 2;

    public static void ResetWorldState()
    {
        Drafts.Clear();
        Concluded.Clear();
    }

    private static long Id(War war) => war.data.id;

    // 这场仗能不能上议和台：有开战目标的国战，还在打，主攻主守都在
    public static bool Usable(War war)
    {
        if (war?.data == null || war.hasEnded() || !WarGoalSystem.UsesPeaceTable(war)) return false;
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        return attacker?.data != null && defender?.data != null && !attacker.isRekt() && !defender.isRekt() && attacker != defender;
    }

    public static Kingdom Giver(War war, PeaceTerm term) => term.by_attacker ? war.main_attacker : war.main_defender;
    public static Kingdom Receiver(War war, PeaceTerm term) => term.by_attacker ? war.main_defender : war.main_attacker;

    #region 台子(神在议和页上摆的)

    public static PeaceDeal Draft(War war)
    {
        long id = Id(war);
        if (!Drafts.TryGetValue(id, out PeaceDeal deal)) Drafts[id] = deal = Offer(war);
        Prune(war, deal);
        return deal;
    }

    public static void ResetToOffer(War war) => Drafts[Id(war)] = Offer(war);

    public static void Clear(War war) => Drafts[Id(war)] = new PeaceDeal();

    // 点一项：在台上就撤下，不在就加上(同一方出的同类条款换掉)
    public static void Toggle(War war, PeaceTerm term)
    {
        PeaceDeal deal = Draft(war);
        if (deal.terms.RemoveAll(item => item.Key == term.Key) > 0) return;
        deal.terms.RemoveAll(item => item.Slot == term.Slot);
        PeaceTerm copy = term.Copy();
        copy.added = true;
        deal.terms.Add(copy);
    }

    public static void Remove(War war, PeaceTerm term) => Draft(war).terms.RemoveAll(item => item.Key == term.Key);

    // 撮合：凑到两国都肯；凑不拢就停在两边最接近的地方。不动主城池
    public static bool Mediate(War war)
    {
        PeaceDeal deal = Draft(war);
        if (deal.terms.Count == 0) Drafts[Id(war)] = deal = Offer(war);
        return Balance(war, deal, lockMain: true, markAdded: true);
    }

    // 签约：forced = 强令(不看意愿)；否则两国都肯才签。上帝视角不受最短战期限制(见 WarPeaceRules)
    public static bool Sign(War war, bool forced)
    {
        if (!Usable(war)) return false;
        PeaceDeal deal = Draft(war).Copy();
        if (!forced)
        {
            (PeaceOpinion a, PeaceOpinion d) = Evaluate(war, deal, Basis(war));
            if (!a.Accepts || !d.Accepts) return false;
        }
        long id = Id(war);
        Concluded[id] = (deal, forced ? Source.Forced : Source.Signed);
        MembershipCooldown.ByGod(() => World.world.wars.endWar(war, WarWinner.Peace));
        if (!war.hasEnded())
        {
            Concluded.Remove(id);
            return false;
        }
        Drafts.Remove(id);
        return true;
    }

    // 罢兵(用户 2026-11-10"谁开启的战争谁就可以自主停战""但是停战需要赔付钱""相当于战争目标没完成打输了")：
    // 开战的一方随时可以自己收兵，不看对方肯不肯；算作目标没打成、打输了——守方胜，攻方照败方赔款、占地交还(见 WarGoalSystem.Settle)
    public static bool Withdraw(War war, bool byGod)
    {
        if (!Usable(war)) return false;
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        string text = string.Format(LM.Get("peace_withdraw_history"), attacker.GetKingdomName(), defender.GetKingdomName());
        if (byGod) MembershipCooldown.ByGod(() => World.world.wars.endWar(war, WarWinner.Defenders));
        else World.world.wars.endWar(war, WarWinner.Defenders);
        if (!war.hasEnded()) return false;
        Drafts.Remove(Id(war));
        TranslateHelper.LogEventMessage(text, attacker);
        return true;
    }

    // 两国谈不拢时攻方肯不肯罢兵：照标准一档赔款算它的意愿(打不动、疲敝重的宁可赔钱收兵)
    private static bool WouldWithdraw(War war, PeaceBasis basis)
    {
        var deal = new PeaceDeal();
        var pay = new PeaceTerm { kind = PeaceTermKind.Indemnity, by_attacker = true, tier = PeaceTermRules.StandardIndemnity };
        if (Unavailable(war, pay) == null) deal.terms.Add(pay);
        (PeaceOpinion attacker, PeaceOpinion _) = Evaluate(war, deal, basis);
        return attacker.Accepts;
    }

    // 去掉已经不成立的(城换了主、条件不再可行)
    private static void Prune(War war, PeaceDeal deal) => deal.terms.RemoveAll(term => Unavailable(war, term) != null);

    #endregion

    #region 照目标开价

    // 主攻方照战争目标开的价：割目标城、吞并全境、收复法理城、称臣、扶立、改宗、通商、赔款
    public static PeaceDeal Offer(War war)
    {
        var deal = new PeaceDeal();
        if (!Usable(war)) return deal;
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        WarExtension.WarExtraData data = war.GetOrCreate();
        WarGoal goal = WarGoalSystem.Get(war);

        void Cede(City city)
        {
            if (city?.data == null || city.isRekt() || city.kingdom != defender) return;
            var term = new PeaceTerm { kind = CessionKind(city, defender, attacker), city_id = city.id };
            if (!deal.Has(term)) deal.terms.Add(term);
        }

        void Give(PeaceTermKind kind, string resource = null) =>
            deal.terms.Add(new PeaceTerm { kind = kind, resource = resource });

        switch (goal)
        {
            case WarGoal.吞并:
                foreach (City city in defender.cities.ToList()) Cede(city);
                break;
            case WarGoal.收复:
                foreach (City city in WarGoalSystem.Claimed(attacker, defender.cities)) Cede(city);
                break;
            case WarGoal.割地:
            case WarGoal.夺城:
            case WarGoal.侵略:
            case WarGoal.争法理:
            case WarGoal.争夺资源:
                foreach (City city in WarObjective.Targets(war, data)) Cede(city);
                if (deal.terms.Count == 0)
                    foreach (City city in defender.cities.Where(city => WarObjective.Held(war, city)).ToList()) Cede(city);
                if (deal.terms.Count == 0 && goal == WarGoal.争夺资源 && !string.IsNullOrEmpty(data.goal_resource))
                    Give(PeaceTermKind.Tribute, data.goal_resource);
                break;
            case WarGoal.迫使附庸:
            case WarGoal.羁縻:
            case WarGoal.继承:
                Give(PeaceTermKind.Vassal);
                break;
            case WarGoal.扶立:
                Give(PeaceTermKind.Install);
                break;
            case WarGoal.改宗:
                Give(PeaceTermKind.Convert);
                break;
            case WarGoal.保护国:
                Give(PeaceTermKind.Protectorate);
                break;
            case WarGoal.租借:
                foreach (City city in WarObjective.Targets(war, data))
                    if (city?.data != null && !city.isRekt() && city.kingdom == defender)
                        deal.terms.Add(new PeaceTerm { kind = PeaceTermKind.Lease, city_id = city.id });
                break;
            case WarGoal.通商:
                Give(PeaceTermKind.Trade);
                break;
        }
        Prune(war, deal);
        // 目标落不到台子上(赔款、目标城已经不在对方手里、条件不成立)：退为赔款
        if (deal.terms.Count == 0)
        {
            var indemnity = new PeaceTerm { kind = PeaceTermKind.Indemnity };
            if (Unavailable(war, indemnity) == null) deal.terms.Add(indemnity);
        }
        return deal;
    }

    // 主城池那一项(守方出)：撮合时不动它
    private static string MainKey(War war)
    {
        City main = WarObjective.Main(war.GetOrCreate());
        return main?.data != null && main.kingdom == war.main_defender
            ? new PeaceTerm { kind = CessionKind(main, war.main_defender, war.main_attacker), city_id = main.id }.Key
            : null;
    }

    // 照目标开价时割城的写法：近代割他国本土写"割地立傀儡"(民族意识的砝码少得多，见 PeaceTermRules)，别的照旧割让
    private static PeaceTermKind CessionKind(City city, Kingdom giver, Kingdom receiver) =>
        OccupationPuppetSystem.NationalLand(city, giver, receiver) ? PeaceTermKind.Puppet : PeaceTermKind.City;

    #endregion

    #region 能出什么

    // byAttacker 一方能出的条件，连同出不了的(reason 为原因键)；nearOnly = 城只列对方占着的、挨着对方的(撮合用)
    public static List<(PeaceTerm term, string reason)> Options(War war, bool byAttacker, bool nearOnly = false)
    {
        var list = new List<(PeaceTerm, string)>();
        if (!Usable(war)) return list;
        Kingdom giver = byAttacker ? war.main_attacker : war.main_defender, receiver = byAttacker ? war.main_defender : war.main_attacker;
        bool dependentReceiver = WarlordEraSystem.Dependent(receiver);
        foreach (City city in CityOptions(war, giver, receiver, !byAttacker, nearOnly))
        {
            list.Add((new PeaceTerm { kind = PeaceTermKind.City, by_attacker = byAttacker, city_id = city.id }, null));
            // 他国本土另有"割地立傀儡"一项(收的一方本身是附从的不立傀儡)
            if (!dependentReceiver && OccupationPuppetSystem.NationalLand(city, giver, receiver))
                list.Add((new PeaceTerm { kind = PeaceTermKind.Puppet, by_attacker = byAttacker, city_id = city.id }, null));
            // 近代殖民列强另可租借港口城
            if (!dependentReceiver && ColonySystem.ColonialPower(receiver) && city != giver.capital && ColonySystem.Port(city))
                list.Add((new PeaceTerm { kind = PeaceTermKind.Lease, by_attacker = byAttacker, city_id = city.id }, null));
        }
        Add(new PeaceTerm { kind = PeaceTermKind.Protectorate, by_attacker = byAttacker });
        // 出的一方名下的殖民地移交收的一方，改为委任统治地
        foreach (Kingdom colony in ColonySystem.ColoniesOf(giver).Where(colony => colony.capital != null).ToList())
            Add(new PeaceTerm { kind = PeaceTermKind.Mandate, by_attacker = byAttacker, city_id = colony.capital.id });
        for (int tier = 0; tier < PeaceTermRules.IndemnityTiers.Length; tier++)
            Add(new PeaceTerm { kind = PeaceTermKind.Indemnity, by_attacker = byAttacker, tier = tier });
        List<string> minerals = TributeResources(giver, receiver);
        if (minerals.Count == 0) list.Add((new PeaceTerm { kind = PeaceTermKind.Tribute, by_attacker = byAttacker }, "peace_reason_no_mineral"));
        foreach (string mineral in minerals) Add(new PeaceTerm { kind = PeaceTermKind.Tribute, by_attacker = byAttacker, resource = mineral });
        Add(new PeaceTerm { kind = PeaceTermKind.Vassal, by_attacker = byAttacker });
        Add(new PeaceTerm { kind = PeaceTermKind.Install, by_attacker = byAttacker });
        Add(new PeaceTerm { kind = PeaceTermKind.Trade, by_attacker = byAttacker });
        Add(new PeaceTerm { kind = PeaceTermKind.Convert, by_attacker = byAttacker });
        return list;

        void Add(PeaceTerm term) => list.Add((term, Unavailable(war, term)));
    }

    // 出的一方的城：对方占着的在前，再是挨着对方的，再按户数
    private static List<City> CityOptions(War war, Kingdom giver, Kingdom receiver, bool receiverAttacks, bool nearOnly)
    {
        Kingdom realm = FrontSystem.Realm(receiver) ?? receiver;
        return giver.cities.Where(city => city?.data != null && !city.isRekt())
            .Select(city => (city, held: PeaceTermRules.OccupiedShare(war, city, receiverAttacks), near: Borders(city, realm)))
            .Where(entry => !nearOnly || entry.held > 0f || entry.near)
            .OrderByDescending(entry => entry.held).ThenByDescending(entry => entry.near)
            .ThenByDescending(entry => PeaceTermRules.CityValue(entry.city))
            .Select(entry => entry.city).ToList();
    }

    private static bool Borders(City city, Kingdom realm)
    {
        foreach (City next in (IEnumerable<City>)city.neighbours_cities ?? Array.Empty<City>())
            if (next?.data != null && !next.isRekt() && next.kingdom != null && FrontSystem.Realm(next.kingdom) == realm) return true;
        return false;
    }

    // 出的一方有、收的一方没有的矿，挑最值钱的几种
    private static List<string> TributeResources(Kingdom giver, Kingdom receiver)
    {
        var ours = new HashSet<string>();
        foreach (City city in receiver.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            foreach ((string id, _, _, float chance) in MineralResourceSystem.Minerals)
                if (!ours.Contains(id) && MineralResourceSystem.HasDeposit(city, id, chance)) ours.Add(id);
        }
        var theirs = new HashSet<string>();
        foreach (City city in giver.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            foreach ((string id, _, _, float chance) in MineralResourceSystem.Minerals)
                if (!ours.Contains(id) && !theirs.Contains(id) && MineralResourceSystem.HasDeposit(city, id, chance)) theirs.Add(id);
        }
        return theirs.OrderByDescending(WarObjective.Value).Take(TributeChoices).ToList();
    }

    // 这一项出不了的原因键；出得了返回 null
    public static string Unavailable(War war, PeaceTerm term)
    {
        Kingdom giver = Giver(war, term), receiver = Receiver(war, term);
        if (giver?.data == null || receiver?.data == null || giver.isRekt() || receiver.isRekt()) return "peace_reason_gone";
        // 别国的附从(傀儡、属地、宗主国不在图上的剧本属地)不收附庸、保护国、租借、托管、傀儡，也不被第三国收作附庸、另立君主
        // (以前葡属澳门收闽系军阀作附庸)
        if ((term.kind is PeaceTermKind.Vassal or PeaceTermKind.Install or PeaceTermKind.Protectorate or PeaceTermKind.Lease or
                 PeaceTermKind.Mandate or PeaceTermKind.Puppet && WarlordEraSystem.Dependent(receiver)) ||
            (term.kind is PeaceTermKind.Vassal or PeaceTermKind.Install or PeaceTermKind.Protectorate && WarlordEraSystem.Dependent(giver)))
            return "peace_reason_dependent";
        switch (term.kind)
        {
            case PeaceTermKind.City:
                City city = World.world.cities.get(term.city_id);
                return city?.data != null && !city.isRekt() && city.kingdom == giver ? null : "peace_reason_gone";
            case PeaceTermKind.Puppet:
                City land = World.world.cities.get(term.city_id);
                if (land?.data == null || land.isRekt() || land.kingdom != giver) return "peace_reason_gone";
                return OccupationPuppetSystem.NationalLand(land, giver, receiver) ? null : "peace_reason_not_national";
            case PeaceTermKind.Lease:
                City port = World.world.cities.get(term.city_id);
                if (port?.data == null || port.isRekt() || port.kingdom != giver) return "peace_reason_gone";
                return port == giver.capital || !ColonySystem.Port(port) ? "peace_reason_no_port" : null;
            case PeaceTermKind.Protectorate:
                if (giver.IsEmpire() && giver.GetEmpire()?.kingdoms_list?.Count > 1) return "peace_reason_empire_vassal";
                return ColonySystem.IsColony(giver) || PuppetStateSystem.IsPuppet(giver) || ColonySystem.IsColony(receiver) ||
                       PuppetStateSystem.IsPuppet(receiver) ? "peace_reason_dependent" : null;
            case PeaceTermKind.Mandate:
                City seat = World.world.cities.get(term.city_id);
                return seat?.kingdom != null && ColonySystem.MetropoleOf(seat.kingdom) == giver ? null : "peace_reason_gone";
            case PeaceTermKind.Indemnity:
                return TreasurySystem.Enabled(giver) && TreasurySystem.Enabled(receiver) ? null : "peace_reason_treasury";
            case PeaceTermKind.Tribute:
                return string.IsNullOrEmpty(term.resource) ? "peace_reason_no_mineral" : null;
            case PeaceTermKind.Vassal:
                return giver.IsEmpire() ? "peace_reason_empire_vassal" : null;
            case PeaceTermKind.Install:
                return giver.IsEmpire() ? "peace_reason_empire_install"
                    : WarGoalSystem.InstallCandidate(giver) == null ? "peace_reason_no_heir" : null;
            case PeaceTermKind.Trade:
                return !TradeRouteSystem.HasPort(giver) || !TradeRouteSystem.HasPort(receiver) ? "peace_reason_no_port"
                    : TradeRouteSystem.TreatyPartner(giver) == receiver ? "peace_reason_trading" : null;
            case PeaceTermKind.Convert:
                return WarGoalSystem.Convertible(receiver, giver) ? null : "peace_reason_same_faith";
        }
        return null;
    }

    #endregion

    #region 估价与意愿

    public static PeaceBasis Basis(War war)
    {
        var basis = new PeaceBasis();
        if (!Usable(war)) return basis;
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        basis.attacker = PeaceTermRules.LeverageOf(war, true);
        basis.defender = PeaceTermRules.LeverageOf(war, false);
        basis.attackerWear = PeaceTermRules.WearOf(war, true);
        basis.defenderWear = PeaceTermRules.WearOf(war, false);
        basis.attackerMomentum = PeaceTermRules.Momentum(war, true, basis.attacker);
        basis.defenderMomentum = PeaceTermRules.Momentum(war, false, basis.defender);
        basis.attackerRealm = PeaceTermRules.RealmValue(attacker);
        basis.defenderRealm = PeaceTermRules.RealmValue(defender);
        basis.attackerCollapsing = FrontCollapse.Collapsing(attacker, defender);
        basis.defenderCollapsing = FrontCollapse.Collapsing(defender, attacker);
        return basis;
    }

    public static int Value(War war, PeaceTerm term, PeaceBasis basis = null)
    {
        string key = term.Key;
        if (basis != null && basis.values.TryGetValue(key, out int cached)) return cached;
        int value = Price(war, term, basis);
        if (basis != null) basis.values[key] = value;
        return value;
    }

    private static int Price(War war, PeaceTerm term, PeaceBasis basis)
    {
        Kingdom giver = Giver(war, term);
        if (giver?.data == null || giver.isRekt()) return 0;
        int realm = basis == null ? PeaceTermRules.RealmValue(giver) : term.by_attacker ? basis.attackerRealm : basis.defenderRealm;
        switch (term.kind)
        {
            case PeaceTermKind.City:
            case PeaceTermKind.Puppet:
                City city = World.world.cities.get(term.city_id);
                return city?.kingdom == giver ? PeaceTermRules.CityValue(city) : 0;
            case PeaceTermKind.Lease:
                City port = World.world.cities.get(term.city_id);
                return port?.kingdom == giver ? (int)Math.Round(PeaceTermRules.CityValue(port) * PeaceTermRules.LeaseShare) : 0;
            case PeaceTermKind.Protectorate:
                return (int)Math.Round(realm * PeaceTermRules.ProtectorateShare);
            case PeaceTermKind.Mandate:
                City seat = World.world.cities.get(term.city_id);
                return seat?.kingdom == null ? 0 : (int)Math.Round(PeaceTermRules.RealmValue(seat.kingdom) * PeaceTermRules.MandateShare);
            case PeaceTermKind.Indemnity:
                return PeaceTermRules.MoneyValue(giver, PeaceTermRules.IndemnityAmount(giver, term.tier));
            case PeaceTermKind.Tribute:
                int grade = term.resource == WarObjective.PastureResource ? 4 : WarObjective.Value(term.resource ?? "");
                return Math.Max(10, (int)Math.Round(realm * PeaceTermRules.TributeSharePerGrade * grade));
            case PeaceTermKind.Vassal:
                return (int)Math.Round(realm * PeaceTermRules.VassalShare);
            case PeaceTermKind.Install:
                return (int)Math.Round(realm * PeaceTermRules.InstallShare);
            case PeaceTermKind.Convert:
                return (int)Math.Round(realm * PeaceTermRules.ConvertShare);
            case PeaceTermKind.Trade:
                return Math.Max(5, (int)Math.Round(realm * PeaceTermRules.TradeShare));
        }
        return 0;
    }

    // 两国各自的意愿账
    public static (PeaceOpinion attacker, PeaceOpinion defender) Evaluate(War war, PeaceDeal deal, PeaceBasis basis)
    {
        if (!Usable(war)) return (new PeaceOpinion(), new PeaceOpinion());
        return Tally(deal, basis, term => Value(war, term, basis),
            term => World.world.cities.get(term.city_id) is City city && city == Giver(war, term).capital,
            war.main_attacker.cities?.Count ?? 0, war.main_defender.cities?.Count ?? 0,
            term => Nation(war, term, basis), term => Gain(war, term, basis));
    }

    // 民族意识(失地一方另记)：割让、立傀儡他国本土(见 OccupationPuppetSystem.Resistance)
    private static int Nation(War war, PeaceTerm term, PeaceBasis basis)
    {
        if (!term.IsCity && term.kind != PeaceTermKind.Protectorate) return 0;
        string key = "nation:" + term.Key;
        if (basis != null && basis.values.TryGetValue(key, out int cached)) return cached;
        City city = term.IsCity ? World.world.cities.get(term.city_id) : null;
        // 立保护国按全境算；租借与立傀儡同按四成
        int value = term.kind == PeaceTermKind.Protectorate
            ? OccupationPuppetSystem.RealmResistance(Giver(war, term), Receiver(war, term), PeaceTermRules.ProtectorateNationFactor)
            : city?.data == null ? 0
            : OccupationPuppetSystem.Resistance(city, Giver(war, term), Receiver(war, term), term.kind != PeaceTermKind.City);
        if (basis != null) basis.values[key] = value;
        return value;
    }

    // 收的一方所得：立傀儡只是间接统治，按城价的几成
    private static int Gain(War war, PeaceTerm term, PeaceBasis basis)
    {
        int value = Value(war, term, basis);
        return term.kind == PeaceTermKind.Puppet ? (int)Math.Round(value * PeaceTermRules.PuppetGainShare) : value;
    }

    // 意愿账的算术(不碰游戏对象，回归测试直接跑这一段)：value 给条款折价，capital 判这座城是不是出的一方的都城
    // nation：失地一方另记的民族意识；gain：收的一方的所得(不写同 value)
    internal static (PeaceOpinion attacker, PeaceOpinion defender) Tally(PeaceDeal deal, PeaceBasis basis, Func<PeaceTerm, int> value,
        Func<PeaceTerm, bool> capital, int attackerCityCount, int defenderCityCount, Func<PeaceTerm, int> nation = null,
        Func<PeaceTerm, int> gain = null)
    {
        var a = new PeaceOpinion();
        var d = new PeaceOpinion();
        int attackerCities = 0, defenderCities = 0;
        bool attackerCapital = false, defenderCapital = false;
        foreach (PeaceTerm term in deal.terms)
        {
            int worth = value(term), got = gain?.Invoke(term) ?? worth, pride = nation?.Invoke(term) ?? 0;
            PeaceOpinion giver = term.by_attacker ? a : d, taker = term.by_attacker ? d : a;
            giver.loss += worth;
            giver.losses.Add((term, worth));
            taker.gain += got;
            taker.gains.Add((term, got));
            if (pride > 0)
            {
                giver.nation += pride;
                giver.nations.Add((term, pride));
            }
            if (!term.IsCity) continue;
            if (term.by_attacker)
            {
                attackerCities++;
                attackerCapital |= capital(term);
            }
            else
            {
                defenderCities++;
                defenderCapital |= capital(term);
            }
        }
        a.standing = basis.defender.Total - basis.attacker.Total;
        d.standing = -a.standing;
        a.wear = basis.attackerWear.Total;
        d.wear = basis.defenderWear.Total;
        a.momentum = basis.attackerMomentum;
        d.momentum = basis.defenderMomentum;
        a.desperate = Desperate(attackerCities, attackerCityCount, attackerCapital, basis.attackerRealm, basis.attackerCollapsing);
        d.desperate = Desperate(defenderCities, defenderCityCount, defenderCapital, basis.defenderRealm, basis.defenderCollapsing);
        return (a, d);
    }

    // 死战：割了都城、或割走一半以上的城，又没到兵败如山倒(同 WarGoalSystem.Excessive)
    private static int Desperate(int citiesGiven, int cities, bool capitalGiven, int realm, bool collapsing)
    {
        if (citiesGiven <= 0 || collapsing) return 0;
        if (!capitalGiven && citiesGiven * 2 < Math.Max(1, cities)) return 0;
        return (int)Math.Round(realm * PeaceTermRules.DesperateShare);
    }

    #endregion

    #region 撮合

    // 返回两国是不是都肯了。lockMain：不动主城池(两国自己谈、神点撮合)；强制收场的折中可以动
    private static bool Balance(War war, PeaceDeal deal, bool lockMain, bool markAdded = false)
    {
        if (!Usable(war)) return false;
        PeaceBasis basis = Basis(war);
        List<PeaceTerm> candidates = Options(war, false, nearOnly: true).Concat(Options(war, true, nearOnly: true))
            .Where(option => option.reason == null).Select(option => option.term).ToList();
        return Search(deal, candidates, lockMain ? MainKey(war) : null, markAdded, trial =>
        {
            (PeaceOpinion a, PeaceOpinion d) = Evaluate(war, trial, basis);
            return (a.Total, d.Total);
        }, term => Value(war, term, basis));
    }

    // 撮合的搜索(不碰游戏对象，回归测试直接跑这一段)：一项一项地撤、添(同一方的同类换掉)，每步挑两国"还差多少"合计最少的一步，
    // 一样少的挑两国里更不肯的那一方更肯的，再一样挑变动小的；直到两国都肯，或者再怎么动都不更近为止。locked 那一项不撤。
    // 只看更不肯的那一方会被死战卡住：割都城能让双方都"差得少一点"，加上它以后哪一步都更差，就停在割都城上谈不拢，
    // 明明换几座别的城就能谈成。totals 给出一份和约两国的意愿
    internal static bool Search(PeaceDeal deal, IReadOnlyList<PeaceTerm> candidates, string locked, bool markAdded,
        Func<PeaceDeal, (int attacker, int defender)> totals, Func<PeaceTerm, int> value)
    {
        for (int step = 0; step < BalanceSteps; step++)
        {
            (int a, int d) = totals(deal);
            if (a >= 0 && d >= 0) return true;
            int bestShort = Short(a, d), bestMin = Math.Min(a, d), bestShift = int.MaxValue;
            PeaceDeal best = null;
            foreach (PeaceTerm term in deal.terms)
            {
                if (term.Key == locked) continue;
                PeaceDeal trial = deal.Copy();
                trial.terms.RemoveAll(item => item.Key == term.Key);
                Consider(trial, value(term));
            }
            foreach (PeaceTerm term in candidates)
            {
                if (deal.Has(term) || deal.terms.Exists(item => item.Slot == term.Slot && item.Key == locked)) continue;
                PeaceDeal trial = deal.Copy();
                trial.terms.RemoveAll(item => item.Slot == term.Slot);
                PeaceTerm copy = term.Copy();
                copy.added = markAdded;
                trial.terms.Add(copy);
                Consider(trial, value(term));
            }
            if (best == null) break;
            deal.terms.Clear();
            deal.terms.AddRange(best.terms);

            void Consider(PeaceDeal trial, int shift)
            {
                (int ta, int td) = totals(trial);
                int shortfall = Short(ta, td), worst = Math.Min(ta, td);
                if (shortfall > bestShort || shortfall == bestShort &&
                    (worst < bestMin || worst == bestMin && (best == null || shift >= bestShift))) return;
                best = trial;
                bestShort = shortfall;
                bestMin = worst;
                bestShift = shift;
            }
        }
        (int fa, int fd) = totals(deal);
        return fa >= 0 && fd >= 0;

        static int Short(int a, int d) => Math.Max(0, -a) + Math.Max(0, -d);
    }

    #endregion

    #region 两国自己议和、收场

    // 每场仗每年一次(WarPatch.update)：满最短战期、不是打到底的仗，主攻方照目标开价、撮合到两国都肯就签；
    // 顺手记下这一年的本钱，下一年算势头
    public static void Talks(War war)
    {
        if (!Usable(war) || World.world == null) return;
        WarExtension.WarExtraData data = war.GetOrCreate();
        double now = World.world.getCurWorldTime();
        if (data.peace_talks_at >= 0d && now >= data.peace_talks_at && Date.getYearsSince(data.peace_talks_at) < 1) return;
        bool first = data.peace_talks_at < 0d;
        PeaceTermRules.Leverage attacker = PeaceTermRules.LeverageOf(war, true), defender = PeaceTermRules.LeverageOf(war, false);
        if (!first && TalksAllowed(war))
        {
            PeaceDeal deal = Offer(war);
            if (Balance(war, deal, lockMain: true))
            {
                long id = Id(war);
                Concluded[id] = (deal, Source.Talks);
                World.world.wars.endWar(war, WarWinner.Peace);
                if (war.hasEnded()) return;
                Concluded.Remove(id);
            }
            // 谈不拢：开战的一方宁可赔款收兵就罢兵(算打输了)
            else if (WouldWithdraw(war, Basis(war)) && Withdraw(war, false)) return;
        }
        data.peace_talks_at = now;
        data.peace_momentum_attacker = attacker.Cities;
        data.peace_momentum_defender = defender.Cities;
    }

    private static bool TalksAllowed(War war)
    {
        WarExtension.WarExtraData data = war.GetOrCreate();
        if (data.scripted_war || war.isTotalWar()) return false;
        if (WarlordEraSystem.IsCivilWar(war) || RulerTraitSystem.IsFounderUnificationWar(war) || UnificationSystem.IsUnificationWar(war))
            return false;
        return WarSituation.WarYears(war) >= WarPeaceRules.MinWarYears;
    }

    // 以议和收场时结算(WarGoalSystem.Settle)：签下的照台上的条款；别的路子(强制停战、罢兵、外交面板停战)照目标开价后撮合出折中
    public static void Conclude(War war, Kingdom attacker, Kingdom defender)
    {
        long id = Id(war);
        PeaceDeal deal;
        Source source;
        if (Concluded.TryGetValue(id, out (PeaceDeal deal, Source source) signed))
        {
            deal = signed.deal;
            source = signed.source;
            Concluded.Remove(id);
        }
        else
        {
            deal = Offer(war);
            Balance(war, deal, lockMain: false);
            source = Source.Talks;
        }
        Drafts.Remove(id);
        Prune(war, deal);
        Apply(war, deal, attacker, defender, source);
    }

    private static void Apply(War war, PeaceDeal deal, Kingdom attacker, Kingdom defender, Source source)
    {
        string line = string.Format(LM.Get(source == Source.Forced ? "peace_deal_forced" : "peace_deal_signed"),
            attacker.GetKingdomName(), defender.GetKingdomName(), Describe(war, deal));
        var ceded = new Dictionary<bool, List<City>> { [true] = new List<City>(), [false] = new List<City>() };
        // 统一战争里割给攻方的城记作统一所得(同 WarGoalSystem.Cede)
        bool unification = UnificationSystem.IsUnificationWar(war);
        foreach (PeaceTerm term in deal.terms)
        {
            Kingdom giver = term.by_attacker ? attacker : defender, receiver = term.by_attacker ? defender : attacker;
            try
            {
                switch (term.kind)
                {
                    case PeaceTermKind.City:
                        City city = World.world.cities.get(term.city_id);
                        if (city?.data == null || city.isRekt() || city.kingdom != giver) break;
                        WarGoalSystem.QueueCession(city, receiver, unification && !term.by_attacker);
                        ceded[term.by_attacker].Add(city);
                        break;
                    case PeaceTermKind.Puppet:
                        City land = World.world.cities.get(term.city_id);
                        if (land?.data == null || land.isRekt() || land.kingdom != giver) break;
                        OccupationPuppetSystem.QueueCession(land, receiver);
                        ceded[term.by_attacker].Add(land);
                        break;
                    case PeaceTermKind.Lease:
                    {
                        City port = World.world.cities.get(term.city_id);
                        if (port?.data == null || port.isRekt() || port.kingdom != giver) break;
                        Kingdom lessee = receiver, lessor = giver;
                        ColonySystem.Defer(() => ColonySystem.Lease(lessee, lessor, new List<City> { port }));
                        break;
                    }
                    case PeaceTermKind.Protectorate:
                    {
                        Kingdom lord = receiver, ward = giver;
                        ColonySystem.Defer(() => ColonySystem.MakeProtectorate(lord, ward));
                        break;
                    }
                    case PeaceTermKind.Mandate:
                    {
                        Kingdom colony = World.world.cities.get(term.city_id)?.kingdom, trustee = receiver;
                        if (colony != null && ColonySystem.MetropoleOf(colony) == giver)
                            ColonySystem.Defer(() => ColonySystem.TransferAsMandate(colony, trustee));
                        break;
                    }
                    case PeaceTermKind.Indemnity:
                        (double share, bool installments) = PeaceTermRules.IndemnityTiers[
                            Math.Max(0, Math.Min(PeaceTermRules.IndemnityTiers.Length - 1, term.tier))];
                        WarReparationsSystem.Impose(giver, receiver, share, installments);
                        break;
                    case PeaceTermKind.Tribute:
                        ResourceTributeSystem.Impose(giver, receiver, term.resource);
                        break;
                    case PeaceTermKind.Vassal:
                        WarGoalSystem.QueueVassal(receiver, giver);
                        break;
                    case PeaceTermKind.Install:
                        WarGoalSystem.TryInstall(receiver, giver);
                        break;
                    case PeaceTermKind.Trade:
                        if (TradeRouteSystem.ImposeTreaty(giver, receiver))
                            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_trade"), giver.GetKingdomName(),
                                receiver.GetKingdomName()), receiver);
                        break;
                    case PeaceTermKind.Convert:
                        WarGoalSystem.Convert(giver, receiver);
                        break;
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][议和台] {term.kind} 结算失败: {exception.Message}");
            }
        }
        foreach (bool byAttacker in new[] { false, true })
        {
            List<City> cities = ceded[byAttacker];
            if (cities.Count == 0) continue;
            Kingdom giver = byAttacker ? attacker : defender, receiver = byAttacker ? defender : attacker;
            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_ceded_named"), giver.GetKingdomName(), receiver.GetKingdomName(),
                string.Join(LM.Get("list_separator"), cities.Select(city => city.GetCityName()))), receiver);
        }
        if (deal.terms.Count == 0)
            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_white"), attacker.GetKingdomName(),
                defender.GetKingdomName()), attacker);
        WarChronicle.Record(war, WarChronicle.End, line);
        LogService.LogInfo($"[EmpireCraft][议和台] {line}");
    }

    #endregion

    #region 文字

    // 一项条款的说法：(条款、小标、小标颜色)
    public static (string label, string tag, string tagHex) Label(War war, PeaceTerm term)
    {
        Kingdom giver = Giver(war, term), receiver = Receiver(war, term);
        switch (term.kind)
        {
            case PeaceTermKind.City:
            {
                City city = World.world.cities.get(term.city_id);
                if (city?.data == null) return ("—", null, null);
                var tags = new List<string>();
                string hex = "#8FA0A8";
                if (WarObjective.Main(war.GetOrCreate()) == city)
                {
                    tags.Add(LM.Get("peace_tag_main"));
                    hex = "#F3C34A";
                }
                if (giver?.capital == city)
                {
                    tags.Add(LM.Get("peace_tag_capital"));
                    hex = "#FF6666";
                }
                float held = PeaceTermRules.OccupiedShare(war, city, !term.by_attacker);
                if (held > 0f)
                {
                    tags.Add(string.Format(LM.Get("peace_tag_held"), Math.Round(held * 100f)));
                    if (tags.Count == 1) hex = held >= 0.5f ? "#65D66E" : "#E9A85B";
                }
                return (string.Format(LM.Get("peace_term_city"), city.GetCityName()), string.Join(" · ", tags), hex);
            }
            case PeaceTermKind.Puppet:
            {
                City city = World.world.cities.get(term.city_id);
                return city?.data == null ? ("—", null, null)
                    : (string.Format(LM.Get("peace_term_puppet"), city.GetCityName()), LM.Get("peace_tag_puppet"), "#C58BE0");
            }
            case PeaceTermKind.Lease:
            {
                City city = World.world.cities.get(term.city_id);
                return city?.data == null ? ("—", null, null)
                    : (string.Format(LM.Get("peace_term_lease"), city.GetCityName(), ColonySystem.LeaseYears), LM.Get("peace_tag_lease"), "#5FB8D6");
            }
            case PeaceTermKind.Protectorate:
                return (string.Format(LM.Get("peace_term_protectorate"), receiver?.GetKingdomName() ?? ""), null, null);
            case PeaceTermKind.Mandate:
            {
                Kingdom colony = World.world.cities.get(term.city_id)?.kingdom;
                return (string.Format(LM.Get("peace_term_mandate"), colony?.GetKingdomFullName() ?? "—"), null, null);
            }
            case PeaceTermKind.Indemnity:
                return (string.Format(LM.Get("peace_term_indemnity"), MoneyDisplay.Format(PeaceTermRules.IndemnityAmount(giver, term.tier))),
                    LM.Get("peace_tier_" + term.tier), "#8FA0A8");
            case PeaceTermKind.Tribute:
                return string.IsNullOrEmpty(term.resource)
                    ? (LM.Get("peace_term_tribute_any"), null, null)
                    : (string.Format(LM.Get("peace_term_tribute"), WarCatalog.ResourceName(term.resource)), null, null);
            case PeaceTermKind.Vassal:
                return (string.Format(LM.Get(receiver != null && FeudalVassalService.UsesFeudalVassals(receiver)
                    ? "peace_term_vassal_feudal" : "peace_term_vassal_tributary"), receiver?.GetKingdomName() ?? ""), null, null);
            case PeaceTermKind.Install:
            {
                Actor candidate = giver != null ? WarGoalSystem.InstallCandidate(giver) : null;
                return candidate != null
                    ? (string.Format(LM.Get("peace_term_install"), candidate.getName()), null, null)
                    : (LM.Get("peace_term_install_any"), null, null);
            }
            case PeaceTermKind.Trade:
                return (LM.Get("peace_term_trade"), null, null);
            case PeaceTermKind.Convert:
                return (string.Format(LM.Get("peace_term_convert"), receiver?.religion?.name ?? ""), null, null);
        }
        return ("—", null, null);
    }

    // 议和页"添加条件"的分类：1 城池、2 财货、3 臣属、4 宗教通商
    public static int Category(PeaceTerm term) => term.kind switch
    {
        PeaceTermKind.City or PeaceTermKind.Puppet or PeaceTermKind.Lease => 1,
        PeaceTermKind.Protectorate or PeaceTermKind.Mandate => 3,
        PeaceTermKind.Indemnity or PeaceTermKind.Tribute => 2,
        PeaceTermKind.Vassal or PeaceTermKind.Install => 3,
        _ => 4
    };

    // 一方出的条件写成一句：割洛阳、偃师，赔款金 1,200
    private static string Clauses(War war, IEnumerable<PeaceTerm> terms)
    {
        var list = terms.ToList();
        var clauses = new List<string>();
        List<string> cities = list.Where(term => term.kind == PeaceTermKind.City)
            .Select(term => World.world.cities.get(term.city_id)).Where(city => city?.data != null)
            .Select(city => city.GetCityName()).ToList();
        if (cities.Count > 0) clauses.Add(string.Format(LM.Get("peace_deal_cities"), string.Join(LM.Get("list_separator"), cities)));
        clauses.AddRange(list.Where(term => term.kind != PeaceTermKind.City).Select(term => Label(war, term).label));
        return string.Join(LM.Get("peace_deal_comma"), clauses);
    }

    // 整份和约：北燕割洛阳、偃师，赔款金 1,200；大梁开港通商二十年。空的是白和
    public static string Describe(War war, PeaceDeal deal)
    {
        var parts = new List<string>();
        foreach (bool byAttacker in new[] { false, true })
        {
            List<PeaceTerm> terms = deal.terms.Where(term => term.by_attacker == byAttacker).ToList();
            if (terms.Count == 0) continue;
            Kingdom giver = byAttacker ? war.main_attacker : war.main_defender;
            parts.Add(string.Format(LM.Get("peace_deal_giver"), giver?.GetKingdomName() ?? "", Clauses(war, terms)));
        }
        return parts.Count == 0 ? LM.Get("peace_deal_white") : string.Join(LM.Get("peace_deal_semicolon"), parts);
    }

    // 不带国名的简述(议和页"照目标开价"一格)
    public static string Summary(War war, PeaceDeal deal) =>
        deal.terms.Count == 0 ? LM.Get("peace_deal_white") : Clauses(war, deal.terms);

    // 概况页"若此刻议和"一行：两国肯不肯、照目标开的什么价
    public static (string text, string hex) Preview(War war)
    {
        if (!Usable(war)) return (null, null);
        PeaceDeal offer = Offer(war);
        (PeaceOpinion a, PeaceOpinion d) = Evaluate(war, offer, Basis(war));
        string summary = Summary(war, offer);
        if (a.Accepts && d.Accepts) return (string.Format(LM.Get("peace_preview_both"), summary), "#65D66E");
        if (!a.Accepts && !d.Accepts) return (string.Format(LM.Get("peace_preview_neither"), summary), "#FF6666");
        Kingdom refusing = a.Accepts ? war.main_defender : war.main_attacker;
        return (string.Format(LM.Get("peace_preview_refuse"), refusing.GetKingdomName(), summary), "#E9A85B");
    }

    // 议和页天平下的一行提示：(左、右)
    public static (string left, string right) Hint(War war, PeaceDeal deal, PeaceBasis basis, PeaceOpinion a, PeaceOpinion d)
    {
        if (a.Accepts && d.Accepts) return (LM.Get("peace_hint_both_label"), LM.Get("peace_hint_both"));
        if (!a.Accepts && !d.Accepts) return (LM.Get("peace_hint_neither_label"), LM.Get("peace_hint_neither"));
        bool attackerRefuses = !a.Accepts;
        Kingdom refusing = attackerRefuses ? war.main_attacker : war.main_defender;
        int gap = -(attackerRefuses ? a.Total : d.Total);
        string left = string.Format(LM.Get("peace_hint_short"), refusing.GetKingdomName(), gap.ToString("#,0"));
        // 撤掉不肯那一方出的哪一项就够了(挑最便宜的)
        foreach (var (term, _) in (attackerRefuses ? a.losses : d.losses).OrderBy(entry => entry.value))
        {
            PeaceDeal trial = deal.Copy();
            trial.terms.RemoveAll(item => item.Key == term.Key);
            (PeaceOpinion ta, PeaceOpinion td) = Evaluate(war, trial, basis);
            if (ta.Accepts && td.Accepts) return (left, string.Format(LM.Get("peace_hint_drop"), Label(war, term).label));
        }
        return (left, LM.Get("peace_hint_mediate"));
    }

    #endregion
}
