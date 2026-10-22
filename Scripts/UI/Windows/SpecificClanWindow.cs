using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 宗族(「卷册」宽窗口，用户 2026-10-22 定"宗族与人物合成一个宽窗口"，示意图 Tasks/ui-mockups/clan-wide.png)：
//   · 左栏(固定)：宗族名牌(族色、族名、门第、郡望 · 立族之年 · 世数) → 当前人物(头像、第几世、姓名、官职；
//     换过人就多一个"回到某某") → 宗族一组页(总览、族人名录、支派、大事记)与本人一组页(亲属、族谱树、生平列传、相关记事) → 改族名；
//   · 右栏按页画，各页在分部文件：.Overview(总览、门第走势)、.Members(族人名录)、.Branches(支派)、.Chronicle(大事记)、
//     .Relatives(亲属、族谱树)、.Biography(生平列传、相关记事)。
// 点任何一个人都是"换成这个人"：左栏人物跟着换，人在别的宗族就连宗族一起换。
// 入口：人物窗口的宗族页签(选中的人，默认亲属页)、ShowFor(族谱身份, 页)、ShowClan(宗族, 页)；OpenIdentity 是旧入口，照旧可用。
public partial class SpecificClanWindow : WideScrollWindow<SpecificClanWindow>
{
    public enum Page { Overview, Members, Branches, Chronicle, Relatives, Tree, Biography, Related }

    // 下次打开时按这个族谱身份显示(整族虚拟的宗族没有实体可选)
    public static PersonalClanIdentity OpenIdentity;
    private static Page? _pendingPage;
    // ShowFor 打开的才重置；从史书、名册等窗口返回时 OnOpen 也会跑，那时保持原来的人、页和筛选
    private static bool _pendingOpen;

    private Page _page = Page.Relatives;
    private SpecificClan _sc;
    // 当前人物；_home 是打开窗口时的那个人
    private PersonalClanIdentity _identity, _home;
    // 每次重画前取一遍：全族(历代)与在世族人、门第名牌、本宗往下的支数(后两样要扫全部宗族，不在画的时候各算一遍)
    private List<PersonalClanIdentity> _people = new(), _living = new();
    private (string text, string hex) _standing;
    private int _branchCount;

    // identity 为空(这人没有族谱)也打开，显示空状态，不留上一次的宗族
    public static void ShowFor(PersonalClanIdentity identity, Page page = Page.Relatives)
    {
        OpenIdentity = identity;
        _pendingPage = page;
        _pendingOpen = true;
        ScrollWindow.showWindow(nameof(SpecificClanWindow));
    }

    // 按宗族打开：当前人物取宗子(族长)，没有就取开族始祖
    public static void ShowClan(SpecificClan clan, Page page = Page.Overview)
    {
        if (clan == null) return;
        ShowFor(Heir(clan) ?? SpecificClanManager.getPerson(clan.founder), page);
    }

    protected override void OnOpen()
    {
        PersonalClanIdentity identity = OpenIdentity;
        bool fresh = _pendingOpen || identity != null || _identity == null;
        OpenIdentity = null;
        _pendingOpen = false;
        if (!fresh)
        {
            _pendingPage = null;
            return;
        }
        // 没走 ShowFor 的旧入口：按选中的人
        if (identity == null && SelectedUnit.unit is Actor actor && actor.HasSpecificClan()) identity = actor.GetPersonalIdentity();
        _home = _identity = identity;
        _sc = identity?._specificClan;
        _page = _pendingPage ?? Page.Relatives;
        _pendingPage = null;
        ResetClanState();
    }

    public override void OnNormalDisable()
    {
        base.OnNormalDisable();
        SetOuterScroll(true);
    }

    // 换了宗族：各页的筛选、翻页、选中的支都回到初始
    private void ResetClanState()
    {
        _memberFilter = MemberFilter.Living;
        _memberSort = MemberSort.Renown;
        _memberSearch = "";
        _memberPage = 0;
        _branch = null;
        _chronicleFilter = ChronicleFilter.All;
        _side = Side.Home;
        _relativeSearch = "";
    }

    protected override void Prepare()
    {
        _people = _sc?.SnapshotPeople().Where(person => person != null).ToList() ?? new List<PersonalClanIdentity>();
        _living = _sc?.LivingPeople ?? new List<PersonalClanIdentity>();
        _standing = _sc == null ? ("", WideKit.Muted) : Standing(_sc, RulingEmpire(_sc));
        _branchCount = BranchCount();
        if (_page == Page.Branches) PrepareBranches();
    }

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        if (_sc == null || _identity == null)
        {
            rail.AddRailTitle(LM.Get("SpecificClanWindow Title"));
            return;
        }
        var head = rail.BeginRailCard("FactionFrame_dominate");
        if (ColorUtility.TryParseHtmlString(ClanHex(_sc), out Color clanColor)) head.AddColorStrip(UIHelper.RailWidth + 4f, clanColor, 3f);
        head.AddRailText(_sc.GetDisplayName().ColorString(WideKit.Gold), 10, 14f);
        head.AddRailText(_standing.text.ColorString(_standing.hex), 7, 10f);
        head.AddRailText(string.Format(LM.Get("clan_rail_summary"), Year(_sc.established_timestamp), Generations())
            .ColorString(WideKit.Muted), 6, 9f);

        var who = rail.BeginRailCard();
        var line = who.BeginHoriGroup(new Vector2(UIHelper.RailWidth - 8f, 30f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        line.AddIdentityAvatar(_identity, () => Go(Page.Biography), 28f);
        float textWidth = UIHelper.RailWidth - 8f - 31f;
        var lines = line.BeginVertGroup(new Vector2(textWidth, 30f), pSpacing: 0, pAlignment: TextAnchor.MiddleLeft,
            pPadding: new RectOffset(0, 0, 0, 0));
        lines.AddTextIntoVertLayout(Caption(_identity).ColorString(WideKit.Muted), true, TextAnchor.MiddleLeft,
            new Vector2(textWidth, 9f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        var name = lines.AddTextIntoVertLayout(_identity.name.ColorString(_identity.is_alive ? WideKit.Gold : WideKit.Soft), true,
            TextAnchor.MiddleLeft, new Vector2(textWidth, 11f));
        name.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(name);
        var post = lines.AddTextIntoVertLayout(Post(_identity).ColorString(OnOffice(_identity) ? WideKit.Gold : WideKit.Soft), true,
            TextAnchor.MiddleLeft, new Vector2(textWidth, 9f));
        post.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(post);
        if (_home != null && _home != _identity)
        {
            PersonalClanIdentity home = _home;
            who.AddButtonIntoVertLayout("clan_rail_back_home", string.Format(LM.Get("clan_rail_back_home"), home.name),
                () => Focus(home), size: new Vector2(UIHelper.RailWidth - 8f, 13f));
        }

        var clanMenu = rail.BeginRailMenu(LM.Get("clan_rail_group_clan"));
        int index = 0;
        clanMenu.AddRailOption(Icon("iconClanList", "iconClan"), LM.Get("clan_page_overview"), null, _page == Page.Overview,
            () => Go(Page.Overview), index++);
        clanMenu.AddRailOption(Icon("iconPopulation"), LM.Get("clan_page_members"), UIHelper.ShortCount(_living.Count),
            _page == Page.Members, () => Go(Page.Members), index++);
        clanMenu.AddRailOption(Icon("iconFamily"), LM.Get("clan_page_branches"), _branchCount.ToString(), _page == Page.Branches,
            () => Go(Page.Branches), index++);
        clanMenu.AddRailOption(Icon("iconWorldLog", "iconClock"), LM.Get("clan_page_chronicle"), null, _page == Page.Chronicle,
            () => Go(Page.Chronicle), index++);

        var personMenu = rail.BeginRailMenu(LM.Get("clan_rail_group_person"));
        index = 0;
        personMenu.AddRailOption(Icon("iconFamilyList", "iconFamily"), LM.Get("clan_page_relatives"), null, _page == Page.Relatives,
            () => Go(Page.Relatives), index++);
        personMenu.AddRailOption(SpriteTextureLoader.getSprite("ui/specificClanIcon"), LM.Get("clan_page_tree"), null,
            _page == Page.Tree, () => Go(Page.Tree), index++);
        personMenu.AddRailOption(Icon("iconWorldInfo"), LM.Get("clan_page_biography"),
            (_identity.personal_history?.Count ?? 0).ToString(), _page == Page.Biography, () => Go(Page.Biography), index++);
        personMenu.AddRailOption(Icon("iconPlotList", "iconWorldLog"), LM.Get("clan_page_related"),
            (_identity.related_history_records?.Count ?? 0).ToString(), _page == Page.Related, () => Go(Page.Related), index++);

        var rename = rail.BeginRailCard();
        rename.AddRailInput(LM.Get("clan_rail_rename"), _sc.name, Rename);
    }

    private static Sprite Icon(params string[] names) =>
        UIHelper.FirstSprite(names.Select(name => "ui/icons/" + name).ToArray());

    // 改族名：本族在世的人姓氏、原版氏族名一起改
    private void Rename(string text)
    {
        string[] parts = (text ?? "").SplitNameParts();
        if (_sc == null || parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]) || parts[0] == _sc.name) return;
        _sc.name = parts[0];
        foreach (PersonalClanIdentity member in _sc.SnapshotPeople().Where(member => member != null && member.is_alive && member._actor != null))
        {
            member._actor.GetModName().familyName = parts[0];
            member._actor.GetModName().SetName(member._actor);
            if (member._actor.hasClan())
                member._actor.clan.data.name = OverallHelperFunc.JoinNameParts(parts[0], LM.Get("Clan"));
        }
        LogService.LogInfo("[EmpireCraft][宗族] 改族名: " + parts[0]);
        if (gameObject.activeInHierarchy) StartCoroutine(RefreshNextFrame());
    }

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        SetOuterScroll(_page != Page.Tree);
        if (_sc == null || _identity == null)
        {
            page.AddEmptyCard(PageWidth, LM.Get("clan_window_empty"));
            return;
        }
        switch (_page)
        {
            case Page.Overview: DrawOverview(page); break;
            case Page.Members: DrawMembers(page); break;
            case Page.Branches: DrawBranches(page); break;
            case Page.Chronicle: DrawChronicle(page); break;
            case Page.Relatives: DrawRelatives(page); break;
            case Page.Tree: DrawTree(page); break;
            case Page.Biography: DrawBiography(page); break;
            case Page.Related: DrawRelated(page); break;
        }
    }

    // 族谱树自带拖拽缩放，外层滚动要关掉，两层滚动会互相抢事件
    private void SetOuterScroll(bool enabled)
    {
        ScrollRect scroll = GetComponent<ScrollWindow>()?.scrollRect;
        if (scroll == null) return;
        scroll.vertical = enabled;
        if (!enabled) scroll.verticalNormalizedPosition = 1f;
    }

    private void Go(Page page)
    {
        if (_page == page) return;
        _page = page;
        Refresh(resetScroll: true);
    }

    // 换成这个人(人在别的宗族就连宗族一起换)；page 为空则留在当前页
    private void Focus(PersonalClanIdentity person, Page? page = null)
    {
        SpecificClan clan = person?._specificClan;
        if (clan == null) return;
        _identity = person;
        if (clan != _sc)
        {
            _sc = clan;
            ResetClanState();
        }
        if (page.HasValue) _page = page.Value;
        Refresh(resetScroll: true);
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        Refresh();
    }

    // ───────── 人物文字 ─────────
    private static string Year(double time) =>
        time < 0d ? "?" : string.Format(LM.Get("clan_lineage_year"), Date.getYear(time));

    // 生卒："276生 · 今年46岁" / "245—301 · 享年56"
    private static string Life(PersonalClanIdentity person) => person.is_alive
        ? string.Format(LM.Get("clan_lineage_life_alive"), person.birthday ?? "?", person.age)
        : string.Format(LM.Get("clan_lineage_life_dead"), person.birthday ?? "?", person.deathday ?? "?",
            person.recordedAge >= 0 ? person.recordedAge : person.age);

    internal static bool OnOffice(PersonalClanIdentity person)
    {
        if (person == null || !person.is_alive) return false;
        Actor actor = person.is_virtual ? null : person._actor;
        if (actor?.data != null && !actor.isRekt()) return actor.IsOnOffice() || actor.isKing() || actor.isCityLeader();
        return !string.IsNullOrWhiteSpace(person.officeName);
    }

    private static int Renown(PersonalClanIdentity person)
    {
        Actor actor = person.is_virtual || !person.is_alive ? null : person._actor;
        return actor?.data != null && !actor.isRekt() ? actor.renown : -1;
    }

    // 第几世：按与开族始祖的辈分差
    private int GenerationOf(PersonalClanIdentity person)
    {
        PersonalClanIdentity founder = SpecificClanManager.getPerson(_sc?.founder ?? -1L);
        if (founder != null && founder.generation > 0 && person.generation >= founder.generation)
            return person.generation - founder.generation + 1;
        return Mathf.Max(1, person.generation);
    }

    private int Generations() => _people.Count == 0 ? 0 : _people.Max(GenerationOf);

    // 左栏人物上方的小字：第几世 · 在世/已故
    private string Caption(PersonalClanIdentity person) =>
        string.Format(LM.Get("clan_generation"), GenerationOf(person)) + " · " +
        LM.Get(person.is_alive ? person.is_virtual ? "clan_person_virtual" : "clan_person_alive" : "clan_person_dead");

    // 族色(宗族的颜色存的是十六进制)
    internal static string ClanHex(SpecificClan clan)
    {
        string hex = clan?.color ?? "";
        return hex.StartsWith("#") ? hex : "#" + hex;
    }

    // 宗族名牌第二行：皇族 / 甲等世家 · 门第 78 / 寒门 · 门第 23 / 不评门第
    // ruling = 这一族所属的在位皇室的帝国(名册一次算好传进来，免得每族都把各帝国宗室查一遍)
    internal static (string text, string hex) Standing(SpecificClan clan, Empire ruling)
    {
        if (ruling is Empire empire)
            return (string.Format(LM.Get("clan_standing_imperial"), empire.GetEmpireName()), WideKit.Red);
        if (!clan.gentry_assessed) return (LM.Get("clan_standing_unassessed"), WideKit.Muted);
        int pedigree = Mathf.RoundToInt(GentrySystem.Pedigree(clan));
        return GentrySystem.IsGentry(clan)
            ? (string.Format(LM.Get("clan_standing_gentry"), GentrySystem.GradeName(clan), pedigree), WideKit.Orange)
            : (string.Format(LM.Get("clan_standing_commoner"), pedigree), WideKit.Soft);
    }

    // 在位皇帝的宗室(连同分出去的各支)
    // (与 SpecificClanManager.LineageClans 同一口径：同一个始祖宗族，只比始祖、不把各族都列一遍)
    internal static Empire RulingEmpire(SpecificClan clan)
    {
        if (clan == null || ModClass.EMPIRE_MANAGER == null) return null;
        SpecificClan root = clan.GetRootClan();
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            SpecificClan imperial = empire?.EmpireSpecificClan;
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || imperial == null) continue;
            if (imperial == clan || imperial.GetRootClan() == root) return empire;
        }
        return null;
    }

    // 称谓(人物格上的小字)：兄弟姐妹按长幼称，配偶按性别称，其余用原来的亲属称谓
    private string RelationTag(ClanRelation relation, PersonalClanIdentity person)
    {
        bool male = person.sex == ActorSex.Male;
        switch (relation)
        {
            case ClanRelation.SELF: return LM.Get("clan_rel_self");
            case ClanRelation.SFSM:
            case ClanRelation.SFDM:
            case ClanRelation.DFSM:
                bool elder = _identity != null && Born(person) < Born(_identity);
                string word = LM.Get(male ? elder ? "clan_rel_elder_brother" : "clan_rel_younger_brother"
                    : elder ? "clan_rel_elder_sister" : "clan_rel_younger_sister");
                return relation == ClanRelation.SFSM ? word
                    : LM.Get(relation == ClanRelation.SFDM ? "clan_rel_half_mother" : "clan_rel_half_father") + word;
            case ClanRelation.CHILDS: return LM.Get("clan_rel_son");
            case ClanRelation.CHILDD: return LM.Get("clan_rel_daughter");
            case ClanRelation.CHILD: return LM.Get(male ? "clan_rel_son" : "clan_rel_daughter");
            case ClanRelation.LOV: return LM.Get(male ? "clan_rel_husband" : "clan_rel_wife");
            case ClanRelation.COB: return LM.Get("clan_rel_consort");
            case ClanRelation.FIL: return LM.Get("clan_rel_fil");
            case ClanRelation.MIL: return LM.Get("clan_rel_mil");
            case ClanRelation.NONE: return "";
            default:
                string key = "relation_" + relation;
                return LM.Has(key) ? LM.Get(key) : "";
        }
    }

    // 出生时间：按生日换回世界时间(已故者的年龄是享年，不能拿来比长幼)，认不出生日就按年龄倒推
    private static double Born(PersonalClanIdentity person) =>
        HistoryDateFormatter.TryParseTime(person.birthday, out double time) ? time
            : World.world.getCurWorldTime() - person.age * 60d;

    private static string RelationHex(ClanRelation relation) => relation switch
    {
        ClanRelation.SELF => WideKit.Gold,
        ClanRelation.FAT or ClanRelation.MOM or ClanRelation.FIL or ClanRelation.MIL => WideKit.Cyan,
        ClanRelation.LOV or ClanRelation.COB => "#C9A7E8",
        ClanRelation.CHILD or ClanRelation.CHILDS or ClanRelation.CHILDD or ClanRelation.SSGB or ClanRelation.SSGG or
            ClanRelation.SDGB or ClanRelation.SDGG => WideKit.Green,
        _ => WideKit.Soft
    };

    // 人物格：点一下换成这个人(留在当前页)
    private void DrawTiles(AutoVertLayoutGroup card, IEnumerable<(ClanRelation relation, PersonalClanIdentity person)> people,
        int perRow = 3, Page? page = null)
    {
        List<(ClanRelation relation, PersonalClanIdentity person)> list = people.Where(item => item.person != null).ToList();
        float inner = PageWidth - 12f;
        float width = (inner - UIHelper.CardGap * (perRow - 1)) / perRow;
        for (int start = 0; start < list.Count; start += perRow)
        {
            var row = card.BeginColumns(inner);
            for (int i = start; i < Mathf.Min(list.Count, start + perRow); i++)
            {
                (ClanRelation relation, PersonalClanIdentity person) = list[i];
                row.AddPersonTile(width, person, RelationTag(relation, person), RelationHex(relation), Life(person), Post(person),
                    OnOffice(person) ? WideKit.Gold : WideKit.Muted, person == _identity, () => Focus(person, page));
            }
        }
    }

    // 官职栏：当过皇帝的换成帝号——在位的"国号+年号+皇帝"(明永乐皇帝)，已故有庙号的"国号+庙号(年号)"(明成祖(永乐))，
    // 只有谥号的用"国号+谥号+帝"；已故有谥号的封君、藩王显示谥号(湘文侯、湘献王，见 FeudalPosthumousSystem、PrinceLineage)；
    // 无谥号的藩王显示王号；其余显示记录的官职，没有写"白身"
    internal static string Post(PersonalClanIdentity identity)
    {
        string fallback = identity != null && !identity.is_alive && !string.IsNullOrWhiteSpace(identity.posthumous_name)
            ? identity.posthumous_name
            : !string.IsNullOrWhiteSpace(identity?.prince_title) ? identity.prince_title
            : string.IsNullOrWhiteSpace(identity?.officeName) ? LM.Get("clan_no_post") : identity.officeName;
        if (identity == null || identity.actor_id <= 0 || ModClass.EMPIRE_MANAGER == null) return fallback;

        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            EmpireCraftHistory current = empire?.data?.currentHistory;
            if (identity.is_alive && current != null && current.id == identity.actor_id)
            {
                string yearName = empire.data.has_year_name ? current.year_name : "";
                return string.IsNullOrWhiteSpace(yearName)
                    ? fallback
                    : $"{current.empire_name}{yearName}{LM.Get("emperor")}";
            }
        }
        if (identity.is_alive) return fallback;

        // 跟帝国核心窗口同一个来源：在世帝国的历代记录 + 已灭亡帝国归档在 ALL_HISTORY_DATA 里的记录
        IEnumerable<EmpireCraftHistory> liveHistories = ModClass.EMPIRE_MANAGER
            .Where(empire => empire?.data?.history != null)
            .SelectMany(empire => empire.data.history);
        IEnumerable<EmpireCraftHistory> archivedHistories = (ModClass.ALL_HISTORY_DATA ??
                                                             new Dictionary<long, List<EmpireCraftHistory>>())
            .Values.Where(list => list != null).SelectMany(list => list);
        EmpireCraftHistory history = liveHistories.Concat(archivedHistories)
            .LastOrDefault(record => record != null && record.id == identity.actor_id);
        if (history == null) return fallback;

        string era = string.IsNullOrWhiteSpace(history.year_name) ? "" : $"({history.year_name})";
        if (!string.IsNullOrEmpty(history.miaohao_name))
            return $"{history.empire_name}{LM.Get(history.miaohao_name)}{LM.Get(history.miaohao_suffix)}{era}";
        if (!string.IsNullOrEmpty(history.shihao_name))
            return $"{PosthumousNameGenerator.ShiTitle(history)}{era}";
        return string.IsNullOrWhiteSpace(history.year_name)
            ? fallback
            : $"{history.empire_name}{history.year_name}{LM.Get("emperor")}";
    }
}
