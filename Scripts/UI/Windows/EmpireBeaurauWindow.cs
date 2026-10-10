using NeoModLoader.api;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using NCMS.Extensions;
using EpPathFinding.cs;
using System.Drawing.Printing;
using DG.Tweening;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine.Events;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General.UI.Window;
using UnityEngine.Pool;
using NeoModLoader.services;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;

namespace EmpireCraft.Scripts.UI.Windows;
public class EmpireBeaurauWindow : AutoLayoutWindow<EmpireBeaurauWindow>
{
    public Empire _empire;
    public string culture = "Huaxia";
    AutoHoriLayoutGroup topSpace;
    AutoGridLayoutGroup topOfficeGroup1;
    AutoGridLayoutGroup topOfficeGroup2;

    AutoVertLayoutGroup coreOfficeSpace;
    AutoGridLayoutGroup coreOfficeGroup;

    AutoVertLayoutGroup divisionsSpace;
    AutoGridLayoutGroup divisionsGroup;

    AutoVertLayoutGroup haremsSpace;
    AutoGridLayoutGroup haremsGroup;

    AutoVertLayoutGroup provincesSpace;
    AutoGridLayoutGroup provincesGroup;
    AutoVertLayoutGroup virtualPeeragesSpace;
    AutoGridLayoutGroup virtualPeeragesGroup;
    [Header("UI Prefab & 根容器")]
    public GameObject _itemPrefab;
    public ListPool<GameObject> pool = new ListPool<GameObject>();
    protected override void Init()
    {
        layout.spacing = 3;
        this.layout.padding = new RectOffset(3, 3, 95, 3);
    }
    private void InitialTabButtons()
    {
        if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "empire_bureau_offices"))
        {
            var tab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            tab.Setup("empire_bureau_offices", ScrollWindowComponent, action: ShowBureauOffices,
                sprite: SpriteTextureLoader.getSprite("ui/icons/iconOptions"));
        }
        if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "empire_virtual_peerages"))
        {
            var tab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            tab.Setup("empire_virtual_peerages", ScrollWindowComponent, action: ShowVirtualPeerages,
                sprite: SpriteTextureLoader.getSprite("ChineseCrown"));
        }
    }

    private void ShowBureauOffices(WindowMetaTab _)
    {
        _empire = EmpireCraftMetaTypeLibrary.selected_empire;
        if (_empire?.CoreKingdom == null) return;
        Clear();
        BuildBureauPage();
    }

    private void ShowVirtualPeerages(WindowMetaTab _)
    {
        _empire = EmpireCraftMetaTypeLibrary.selected_empire;
        if (_empire?.CoreKingdom?.GetRegime()?.enfeoff_virtual_only != true) return;
        Clear();
        layout.padding = new RectOffset(3, 3, 95, 3);
        InitialTopPartInfoLvLing();
        virtualPeeragesSpace = this.BeginVertGroup();
        AddSectionHeader(virtualPeeragesSpace, LM.Get("empire_virtual_peerages"), "ChineseCrown", "ui/icons/iconCrown");
        AddSubHeader(virtualPeeragesSpace, LM.Get("empire_legal_virtual_peerages").ColorString("#5AD9FF"));
        virtualPeeragesGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 55));
        Regime regime = _empire.CoreKingdom.GetRegime();
        EmpireCore core = EmpireCoreManager.Get(_empire);
        List<KingdomTitle> titles = EmpireCoreManager.GetTitles(core)
            .Where(t => t != null && !t.isRekt() && !string.Equals(t.data.name, _empire.GetEmpireName(), StringComparison.Ordinal))
            .OrderBy(t => t.data.name)
            .ToList();
        List<Actor> honoraryHolders = _empire.getUnits()
            .Where(a => a != null && !a.isRekt() && a.HasHonoraryPeerage(_empire))
            .ToList();

        // 法理只生成封地席位；王或国公在实际授予时才确定。
        foreach (KingdomTitle title in titles)
        {
            Kingdom landedKingdom = _empire.GetLandedLegalTitleKingdom(title);
            Actor holder = _empire.GetLegalPeerageHolder(title);
            bool isLanded = landedKingdom != null;
            bool isImperialClan = holder?.GetSpecificClan() != null &&
                                   SpecificClanManager.SameLineage(holder.GetSpecificClan(), _empire.EmpireSpecificClan);
            bool isPetitionedTributaryTitle = landedKingdom?.GetTakenAllianceEmpire() == _empire;
            string peerageKey = _empire.data.legal_peerage_types?.TryGetValue(title.id, out string savedType) == true
                ? savedType
                : isLanded && holder != null
                    ? DeJureTitleBindingRules.GetLandedPeerageKey(isImperialClan || isPetitionedTributaryTitle)
                    : holder?.GetOrCreate().virtual_enfeoff_peerage_key ?? "";
            SetVirtualPeerageView(holder, title, peerageKey, false, isLanded,
                ref virtualPeeragesGroup);
        }
        virtualPeeragesSpace.AddChild(virtualPeeragesGroup.gameObject);

        AddSubHeader(virtualPeeragesSpace, LM.Get("empire_honorary_virtual_peerages").ColorString("#FFBF33"));
        var honoraryGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 55));
        foreach (string peerage in regime.virtual_honorary_peerages ?? new List<string>())
        {
            Actor holder = honoraryHolders.FirstOrDefault(a => a.GetOrCreate().honorary_peerage_key == peerage);
            SetVirtualPeerageView(holder, null, peerage, true, false, ref honoraryGroup);
        }
        virtualPeeragesSpace.AddChild(honoraryGroup.gameObject);
        AddChild(virtualPeeragesSpace.gameObject);
    }

    private void SetVirtualPeerageView(Actor actor, KingdomTitle title, string peerageKey, bool honorary,
        bool hasLandedFief, ref AutoGridLayoutGroup parent)
    {
        string fief = title?.data?.name ?? actor?.city?.GetCityName() ?? LM.Get("label_none");
        bool isVacant = actor == null;
        if (!honorary && !isVacant && string.IsNullOrWhiteSpace(peerageKey))
        {
            if (hasLandedFief)
            {
                bool isImperialClan = actor.GetSpecificClan() != null &&
                                       SpecificClanManager.SameLineage(actor.GetSpecificClan(), _empire.EmpireSpecificClan);
                peerageKey = DeJureTitleBindingRules.GetLandedPeerageKey(isImperialClan);
            }
            else
            {
                actor.GetPeerageDisplayName();
                peerageKey = actor.GetOrCreate().virtual_enfeoff_peerage_key;
            }
        }
        string peerage = !honorary && isVacant ? LM.Get("label_peerage_pending") : LM.Get(peerageKey);
        string displayName = isVacant
            ? honorary ? $"{peerage} ({LM.Get("label_vacant")})" : $"{fief} ({LM.Get("label_peerage_pending")})"
            : honorary ? peerage : FormatVirtualPeerageName(fief, peerage);
        string holderName = actor?.getName() ?? LM.Get("label_vacant");
        string fiefText = honorary ? LM.Get("label_honorary") : fief;
        string titleHex = isVacant ? "#8FA0A8" : honorary ? "#FFBF33" : "#5AD9FF";
        var lines = new List<string>
        {
            holderName.ColorString(isVacant ? "#8FA0A8" : "#E6E0CF"),
            $"{LM.Get("OfficialLevel")} {peerage}".ColorString(titleHex),
            $"{LM.Get("label_fief")} {fiefText}".ColorString(honorary ? "#FF9A5A" : "#A8B8BE")
        };
        if (!honorary)
            lines.Add((hasLandedFief ? LM.Get("label_landed_fief") : LM.Get("label_unlanded_fief"))
                .ColorString(hasLandedFief ? "#65D66E" : "#8FA0A8"));
        AddPersonCard(parent, displayName.ColorString(titleHex), "", actor, lines, null);
    }

    private static string FormatVirtualPeerageName(string fief, string peerage)
    {
        if (fief.Length == 1 && (peerage == "公" || peerage == "侯")) fief += "国";
        return fief + peerage;
    }
    [Hotfixable]
    private void InitialTopPartInfoLvLing()
    {
        if (_empire?.CoreKingdom?.data == null) return;
        //总容器
        topSpace = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        topSpace.transform.AddStretchBackground("clanFrame", new Vector2(220, 100));

        var centerPart = topSpace.BeginVertGroup(pSpacing:-3);
        centerPart.AddTextIntoVertLayout("内阁首辅", true, TextAnchor.MiddleCenter);
        Actor cabinetLeader = _empire.GetCabinetLeader();
        string leaderFaction = cabinetLeader?.data == null ? null : cabinetLeader.GetFaction()?.Name;
        centerPart.AddActorViewIntoVertLayout(cabinetLeader,
            description:string.IsNullOrWhiteSpace(leaderFaction)
                ? LM.Get("label_none")
                : leaderFaction.ColorString(pColor:new Color(0.0f, 1, 0.5f)));
        centerPart.AddTextIntoVertLayout("内阁大臣", true, TextAnchor.MiddleCenter);
        var cabinetMemberSpace = centerPart.BeginHoriGroup(pSpacing:-5);
        var members = _empire.GetCabinetMembers() ?? new List<Actor>();
        for(int i=1; i<5; i++)
        {
            int cCount = members.Count;
            Actor member = i < cCount && members[i]?.data != null && !members[i].isRekt() ? members[i] : null;
            string factionName = member?.GetFaction()?.Name;
            cabinetMemberSpace.AddActorViewIntoHoriLayout(member, description: string.IsNullOrWhiteSpace(factionName)
                ? LM.Get("label_none")
                : factionName.ColorString(pColor:new Color(0.0f, 1, 0.5f)));
        }

        FixedFaction dominateFaction = _empire.CoreKingdom.GetRegime()?.GetDominateFaction();
        if (dominateFaction != null)
        {
            AddDominantFactionRow(dominateFaction);
        }
        
        topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset:new Vector2(0, 0));
    }
    [Hotfixable]
    private void InitialTopPartInfoFeudalism()
    {
        //总容器
        topSpace = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        topSpace.transform.AddStretchBackground("clanFrame", new Vector2(220, 100));

        var centerPart = topSpace.BeginVertGroup(pSpacing:-3);
        var members = _empire.GetCabinetMembers();
        centerPart.AddTextIntoVertLayout("教廷选帝侯", true, TextAnchor.MiddleCenter);
        var cabinetMemberSpace = centerPart.BeginHoriGroup(pSpacing:-5);
        for (int i=0; i<3; i++ )
        {
            int cCount = members.Count;
            var member = i<cCount ? members[i] : null;
            cabinetMemberSpace.AddActorViewIntoHoriLayout(member, 
                description: member?.GetFaction()?.Name?.ColorString(pColor:new Color(0.0f, 1, 0.5f))??"无");
        }
        centerPart.AddTextIntoVertLayout("世俗选帝侯", true, TextAnchor.MiddleCenter);
        var cabinetMemberSpace2 = centerPart.BeginHoriGroup(pSpacing:-5);
        for(int i=3; i<7; i++)
        {
            int cCount = members.Count;
            var member = i<cCount ? members[i] : null;
            cabinetMemberSpace2.AddActorViewIntoHoriLayout(member, 
                description: member?.GetFaction()?.Name?.ColorString(pColor:new Color(0.0f, 1, 0.5f))??"无");
        }
        var dominate = _empire.CoreKingdom.GetRegime().GetDominateFaction();
        if (dominate != null)
        {
            AddDominantFactionRow(dominate);
        }
        
        topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset:new Vector2(0, 0));
    }
    [Hotfixable]
    private void InitialTopPartInfoNormal()
    {
        // 没有内阁时顶部那块 220x100 的框里只有两行字，空得难看；君主本人已经在下面金字塔的顶端了，
        // 这里改成正文里一条细的信息条，把原来给顶部框让出的 95 像素也收回来。
        topSpace = null;
        layout.padding = new RectOffset(3, 3, 3, 3);
        var info = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        pool.Add(info.gameObject);
        AddSectionHeader(info, $"{LM.Get("empire_bureau_no_cabinet")} · {LM.Get("empire_bureau_direct_rule")}",
            "ui/icons/iconCrown", "ui/icons/iconKingdom");
        var dominate = _empire.CoreKingdom.GetRegime().GetDominateFaction();
        if (dominate != null)
        {
            AddDominantFactionRow(dominate);
        }
    }
    // 主导派系改成横式卡片后放不进顶部 220 宽的内阁框里了，挪到正文最上面单独一行
    // (顶部框被 AdjustTopPart 移出了布局，所以这一行就是正文的第一项)。只展示，不改格局。
    private void AddDominantFactionRow(FixedFaction faction)
    {
        var holder = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        pool.Add(holder.gameObject);
        UIHelper.AddFactionCard(faction, _empire.CoreKingdom, parentV: holder);
    }

    public void ShowCoreSpace()
    {
        coreOfficeSpace = this.BeginVertGroup();
        //中央核心部门
        AddSectionHeader(coreOfficeSpace, LM.Get("CoreOffice"), "ui/icons/iconKingdom");

        coreOfficeGroup = this.BeginGridGroup(2, pCellSize: new Vector2(100, 55));
        foreach (var oid in _empire.data.centerOffice.CoreOffices)
        {
            SetOfficeView(oid, ref coreOfficeGroup);
        }
        coreOfficeSpace.AddChild(coreOfficeGroup.gameObject);

        AddChild(coreOfficeSpace.gameObject);
    }

    public void ShowDivisionSpace()
    {
        divisionsSpace = this.BeginVertGroup();
        //中央二级部门
        AddSectionHeader(divisionsSpace, LM.Get("Divisions"), "ui/icons/iconWorldLaws", "ui/icons/iconKnowledge");

        divisionsGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize:new Vector2(100, 55));
        foreach (var o2 in _empire.data.centerOffice.Divisions)
        {
            SetOfficeView(o2, ref divisionsGroup);
        }
        divisionsSpace.AddChild(divisionsGroup.gameObject);

        AddChild(divisionsSpace.gameObject);
    }

    public void ShowHaremSpace()
    {
        haremsSpace = this.BeginVertGroup();
        //后宫
        AddSectionHeader(haremsSpace, LM.Get("Harems"), "ui/icons/iconFamily", "ui/icons/iconCrown");

        haremsGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize:new Vector2(100, 55));
        foreach (var o2 in _empire.data.centerOffice.Harems)
        {
            SetOfficeView(o2, ref haremsGroup);
        }
        haremsSpace.AddChild(haremsGroup.gameObject);

        AddChild(haremsSpace.gameObject);
    }

    public void ShowProvincesSpace()
    {
        provincesSpace = this.BeginVertGroup();
        //省级部门
        AddSectionHeader(provincesSpace, LM.Get("province"), "ui/icons/iconCity");

        provincesGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 55));
        foreach (Kingdom kingdom in _empire.kingdoms_hashset)
        {
            SetOfficeView(kingdom.GetOfficeID(), ref provincesGroup, kingdom);
        }

        provincesSpace.AddChild(provincesGroup.gameObject);

        AddChild(provincesSpace.gameObject);
    }

    // 议会召开后，不论原政体有没有内阁，这里一律显示议会：总理大臣、议员与议席分布
    private static readonly Color GoverningColor = new Color(0.0f, 1f, 0.5f);
    private static readonly Color ConstitutionalistColor = new Color(0.35f, 0.85f, 1f);
    private static readonly Color OppositionColor = new Color(1f, 0.55f, 0.4f);

    private static Color GetSeatColor(bool governing, bool constitutionalist) =>
        governing ? GoverningColor : constitutionalist ? ConstitutionalistColor : OppositionColor;

    [Hotfixable]
    private void InitialTopPartInfoParliament()
    {
        if (_empire?.CoreKingdom?.data == null) return;
        GeneralSystems.ParliamentView view = GeneralSystems.ParliamentSystem.GetView(_empire);
        topSpace = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        topSpace.transform.AddStretchBackground("clanFrame", new Vector2(220, 100));

        var centerPart = topSpace.BeginVertGroup(pSpacing: -3);
        // 总理大臣：议会选举产生
        string government = string.IsNullOrWhiteSpace(view.GovernmentType)
            ? LM.Get("prime_minister_vacant")
            : string.Format(LM.Get("prime_minister_status"), LM.Get($"parliament_government_{view.GovernmentType}"),
                view.GovernmentSeats, view.TotalSeats);
        centerPart.AddTextIntoVertLayout($"{LM.Get("prime_minister_title")} · {government}", true,
            TextAnchor.MiddleCenter, new Vector2(110, 10));

        // 议席图：按理念从左到右排开政党，执政一方加金框，正中为过半线
        List<GeneralSystems.ParliamentFactionView> ordered = view.Factions
            .OrderBy(faction => GeneralSystems.PartySystem.GetPosition(faction.Ideology).x)
            .ThenByDescending(faction => faction.Seats).ToList();
        Dictionary<string, Color> colors = PartyColors(ordered);
        var chartRow = centerPart.BeginHoriGroup(new Vector2(130, 50), TextAnchor.MiddleCenter, 2);
        chartRow.AddActorViewIntoHoriLayout(view.PrimeMinister,
            description: view.PrimeMinisterFaction == null
                ? LM.Get("label_none")
                : view.PrimeMinisterFaction.Name.ColorString(pColor: GoverningColor));
        var seats = new List<HemicycleChart.Seat>();
        foreach (GeneralSystems.ParliamentFactionView faction in ordered)
            seats.AddRange(view.Seats.Where(seat => seat.Faction == faction.Faction).Select(seat =>
                new HemicycleChart.Seat
                {
                    Color = colors[faction.Faction.GetID()], Governing = faction.Governing,
                    Vacant = seat.Representative == null
                }));
        GameObject chart = HemicycleChart.Create(chartRow.transform, new Vector2(96, 50), seats);
        UIHelper.AttachTextTooltip(chart, $"parliament_hemicycle_{_empire.id}",
            string.Format(LM.Get("parliament_members_title"), view.Term, view.YearsUntilElection),
            string.Join("\n", ordered.Select(faction => FactionSeatLine(faction, colors, true))) + "\n" +
            string.Format(LM.Get("parliament_majority_note"), view.TotalSeats / 2 + 1, view.TotalSeats) +
            (view.NextSeatCount != view.TotalSeats
                ? "\n" + string.Format(LM.Get("parliament_next_seat_count"), view.NextSeatCount) : "") +
            "\n" + LM.Get("parliament_hemicycle_legend").ColorString("#8FA0A8"));

        // 施政议程
        centerPart.AddTextIntoVertLayout(AgendaLine(view.Agenda), true, TextAnchor.MiddleCenter,
            new Vector2(130, 9)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);

        // 政治协商：参加的友党与作用
        List<FixedFaction> consultative = GeneralSystems.PartyBanSystem.GetConsultativeParties(_empire);
        if (consultative.Count > 0)
        {
            SimpleText consultation = centerPart.AddTextIntoVertLayout(
                string.Format(LM.Get("consultation_line"), string.Join("、", consultative.Select(party => party.Name)))
                    .ColorString("#7FD8EA"), true, TextAnchor.MiddleCenter, new Vector2(130, 9));
            consultation.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            UIHelper.AttachTextTooltip(consultation.gameObject, $"consultation_{_empire.id}",
                LM.Get("consultation_title"), string.Format(LM.Get("consultation_body"),
                    string.Join("、", consultative.Select(party =>
                        $"{party.Name}[{GeneralSystems.PartySystem.GetIdeologyName(party.Ideology)}]")),
                    GeneralSystems.PartyBanSystem.ConsultationLegitimacy,
                    Mathf.RoundToInt(GeneralSystems.PartyBanSystem.ConsultationDissent * 100f)));
        }

        // 说明：悬停查看议员与总理如何产生
        SimpleText hint = centerPart.AddTextIntoVertLayout(LM.Get("parliament_how_hint").ColorString("#8FA0A8"),
            false, TextAnchor.MiddleCenter, new Vector2(110, 10));
        UIHelper.AttachTextTooltip(hint.gameObject, $"parliament_how_{_empire.id}", LM.Get("parliament_title"),
            string.Format(LM.Get("parliament_how_body"), view.TotalSeats, GeneralSystems.ParliamentSystem.TermYears(_empire)) +
            "\n" + LM.Get("parliament_politics_body") +
            (view.ResponsibleGovernment ? "\n" + LM.Get("parliament_responsible_government_body") : ""));

        // 右侧：各党议席、涨跌与得票
        var seatPart = topSpace.BeginVertGroup(new Vector2(78, 90), pSpacing: 0, pAlignment: TextAnchor.MiddleCenter);
        seatPart.AddTextIntoVertLayout(LM.Get("parliament_seat_distribution"), true, TextAnchor.MiddleCenter,
            new Vector2(78, 10));
        foreach (GeneralSystems.ParliamentFactionView faction in view.Factions)
        {
            var line = seatPart.BeginHoriGroup(new Vector2(78, 9), TextAnchor.MiddleLeft, 1);
            UIHelper.AddLayoutIcon(line.transform,
                SpriteTextureLoader.getSprite(IdeologyTraitIcons.Path(faction.Ideology)), 8f);
            line.AddTextIntoHoriLayout(FactionSeatLine(faction, colors, false), true, TextAnchor.MiddleLeft,
                new Vector2(69, 9)).UseFixedFontSize(5, HorizontalWrapMode.Overflow);
        }
        if (GeneralSystems.ParliamentSystem.KeepsCabinet(_empire))
        {
            int electors = _empire.GetCabinetMembers().Count(actor => actor != null && !actor.isRekt());
            seatPart.AddTextIntoVertLayout(string.Format(LM.Get("parliament_electoral_college_note"), electors), true,
                TextAnchor.MiddleCenter, new Vector2(78, 10));
        }

        topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset: new Vector2(0, 0));
    }

    private static readonly Color[] FallbackPartyColors =
    {
        new(0.85f, 0.33f, 0.31f), new(0.31f, 0.55f, 0.85f), new(0.95f, 0.76f, 0.29f), new(0.4f, 0.75f, 0.45f),
        new(0.65f, 0.45f, 0.8f), new(0.55f, 0.55f, 0.55f)
    };

    // 政党颜色取理念颜色；同一理念的几个党依次调亮，免得在议席图上分不清
    private static Dictionary<string, Color> PartyColors(List<GeneralSystems.ParliamentFactionView> factions)
    {
        var result = new Dictionary<string, Color>();
        var used = new Dictionary<GeneralSystems.PartyIdeology, int>();
        for (int i = 0; i < factions.Count; i++)
        {
            GeneralSystems.ParliamentFactionView faction = factions[i];
            ColorAsset asset = EmpireCraftNamePlateLibrary.GetIdeologyColorAsset(faction.Ideology);
            Color color = asset?.getColorBanner() ?? FallbackPartyColors[i % FallbackPartyColors.Length];
            color.a = 1f;
            used.TryGetValue(faction.Ideology, out int repeat);
            used[faction.Ideology] = repeat + 1;
            result[faction.Faction.GetID()] = Color.Lerp(color, Color.white, 0.28f * repeat);
        }
        return result;
    }

    // "民主党·执政 4席 +1 得票32%"
    private static string FactionSeatLine(GeneralSystems.ParliamentFactionView faction,
        Dictionary<string, Color> colors, bool withIdeology)
    {
        // 党名用议席图上的颜色；执政一方另标金色的"执政"
        string name = faction.Faction.Name.ColorString(pColor: colors[faction.Faction.GetID()]);
        if (faction.Governing) name += LM.Get("parliament_governing_mark").ColorString("#F3C34A");
        string line = $"{name} {string.Format(LM.Get("parliament_seats_short"), faction.Seats)}";
        if (faction.PreviousSeats >= 0 && faction.PreviousSeats != faction.Seats)
        {
            int delta = faction.Seats - faction.PreviousSeats;
            line += " " + (delta > 0 ? $"+{delta}".ColorString("#65D66E") : $"{delta}".ColorString("#E05A4F"));
        }
        if (faction.VoteShare >= 0f)
            line += " " + string.Format(LM.Get("parliament_vote_share"), (faction.VoteShare * 100f).ToString("0"))
                .ColorString("#A8B8BE");
        if (withIdeology)
            line += " " + $"[{GeneralSystems.PartySystem.GetIdeologyName(faction.Ideology)}]".ColorString("#C9A7E8");
        return line;
    }

    private static string AgendaLine(GovernmentAgenda agenda)
    {
        if (agenda == null) return LM.Get("government_agenda_none").ColorString("#8FA0A8");
        string text = string.Format(LM.Get("government_agenda_label"), LM.Get($"constitution_clause_{agenda.clause}"),
            GeneralSystems.ConstitutionSystem.ValueText(agenda.clause, agenda.target), agenda.progress.ToString("0"));
        if (agenda.stalled) text += LM.Get("government_agenda_stalled").ColorString("#E9A85B");
        return text.ColorString("#7FD8EA");
    }

    private void BuildBureauPage()
    {
        // 有内阁/议会的顶部框被 AdjustTopPart 钉在窗口上方，正文要让出 95；无内阁的会在自己的方法里收回
        layout.padding = new RectOffset(3, 3, 95, 3);
        Regime regime = _empire.CoreKingdom.GetRegime();
        if (GeneralSystems.ParliamentSystem.HasParliament(_empire))
        {
            InitialTopPartInfoParliament();
        }
        else switch (regime.type)
        {
            case RegimeType.Feudalism:
                InitialTopPartInfoFeudalism();
                break;
            case RegimeType.LvLing:
                InitialTopPartInfoLvLing();
                break;
            default:
                InitialTopPartInfoNormal();
                break;
        }
        ShowOfficePyramid();
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        InitialTabButtons();
        layout.spacing = 3;
        this.layout.padding = new RectOffset(3, 3, 95, 3);
        _empire = EmpireCraftMetaTypeLibrary.selected_empire;
        Clear();
        BuildBureauPage();
    }

    public override void OnFirstEnable()
    {
        base.OnFirstEnable();
        InitialTabButtons();
        this.DORestart();
        layout.spacing = 3;
        this.layout.padding = new RectOffset(3, 3, 95, 3);
        _empire = EmpireCraftMetaTypeLibrary.selected_empire;
        Clear();
        BuildBureauPage();
    }

    public void Clear()
    {

        if (pool == null) return;
        float deleteTime = 0.1f;
        foreach (GameObject go in pool)
        {
            go.SetActive(false);
            Destroy(go, deleteTime);
            deleteTime += 0.1f;
        }
        if (topSpace != null)
        {
            topSpace.gameObject.SetActive(false);
            Destroy(topSpace, deleteTime);
        }
        if (haremsSpace != null)
        {
            haremsSpace.gameObject.SetActive(false);
            Destroy(haremsSpace, deleteTime);
        }
        if (coreOfficeSpace != null)
        {
            coreOfficeSpace.gameObject.SetActive(false);
            Destroy(coreOfficeSpace, deleteTime);
        }
        if (divisionsSpace != null)
        {
            divisionsSpace.gameObject.SetActive(false);
            Destroy(divisionsSpace, deleteTime);
        }
        if (provincesSpace != null)
        {
            provincesSpace.gameObject.SetActive(false);
            Destroy(provincesSpace, deleteTime);
        }
        if (virtualPeeragesSpace != null)
        {
            virtualPeeragesSpace.gameObject.SetActive(false);
            Destroy(virtualPeeragesSpace, deleteTime);
        }
        pool.Clear();
    }
    [Hotfixable]
    public void SetOfficeView(long oid, ref AutoGridLayoutGroup parent, NanoObject o = null)
    {
        GameObject card = BuildOfficeCard(oid, o);
        if (card != null) parent.AddChild(card);
    }

    // 旧的网格卡片(SetOfficeView 仍在用)
    private GameObject BuildOfficeCard(long oid, NanoObject o = null)
    {
        //寻找存在的官制
        if (!OfficeManager.Offices.TryGetValue(oid, out var officeObject)) return null;
        Actor officer = officeObject.GetActor();
        bool vacant = officer == null;
        var lines = new List<string>
        {
            vacant ? LM.Get("office_vacant").ColorString("#8FA0A8") : officer.data.name.ColorString("#E6E0CF"),
            officeObject.GetName(o).ColorString("#65D6C4")
        };
        // 空缺时 GetOnTime() 是 -1，不显示这一行
        if (!vacant) lines.Add($"{LM.Get("i_on_office_time")} {officeObject.GetOnTime()}".ColorString("#A8B8BE"));
        lines.Add(BuildPowerLine(officeObject, officer));
        string badge = string.Format(LM.Get("office_history_count"), officeObject.history_officers.Count);
        return BuildPersonCard(officeObject.GetOfficeName(o).ColorString("#F3C34A"), badge, officer, lines,
            () => ChangeOfficer(officeObject));
    }

    // 金字塔里的官职节点：卡面只放一眼要看的(官职名 / 头像 / 姓名 / 官阶 / 前两项权能)，
    // 在任时长、历任人数、完整权能放进悬浮提示。
    private GameObject BuildOfficeNode(long oid, string titleHex, NanoObject o = null)
    {
        if (!OfficeManager.Offices.TryGetValue(oid, out var officeObject)) return null;
        Actor officer = officeObject.GetActor();
        bool vacant = officer == null;
        string officeName = officeObject.GetOfficeName(o);
        string rank = officeObject.GetName(o);
        List<string> powers = GetPowerEntries(officeObject, officer);
        string footer = powers.Count == 0
            ? LM.Get("label_none").ColorString("#8FA0A8")
            : (string.Join(" ", powers.Take(2)) + (powers.Count > 2 ? " …" : "")).ColorString("#65D66E");

        var tip = new List<string>
        {
            vacant ? LM.Get("office_vacant") : officer.data.name,
            $"{LM.Get("OfficialLevel")}: {rank}"
        };
        if (!vacant) tip.Add($"{LM.Get("i_on_office_time")}: {officeObject.GetOnTime()}");
        tip.Add(string.Format(LM.Get("office_history_count"), officeObject.history_officers.Count));
        tip.Add($"{LM.Get("office_powers")}: " +
                (powers.Count == 0 ? LM.Get("label_none") : string.Join(" · ", powers)).ColorString("#65D66E"));
        // 政党政治下标出官员党籍(后宫不标)；不属于任何政党的为"无党派"。
        // 按实际岗位属性显示身份；division 中的部长也是政治任命。
        string rankLine = rank.ColorString("#65D6C4");
        bool careerPost = o == null && GeneralSystems.ParliamentSystem.IsCareerCivilServiceOffice(_empire, oid);
        if (!vacant && careerPost && GeneralSystems.PartySystem.IsActive(_empire))
        {
            rankLine += " " + LM.Get("bureau_career_civil_servant").ColorString("#8FA0A8");
            tip.Add(string.Format(LM.Get("bureau_officer_party"), LM.Get("bureau_career_civil_servant"))
                .ColorString("#C9A7E8"));
        }
        else if (!vacant && titleHex != HaremTitleHex && GeneralSystems.PartySystem.IsActive(_empire))
        {
            FixedFaction party = officer.GetFaction();
            bool member = party != null && party.IsParty;
            string partyName = member ? party.Name : LM.Get("party_label_none");
            rankLine += " " + partyName.ColorString(member ? "#C9A7E8" : "#8FA0A8");
            tip.Add(string.Format(LM.Get("bureau_officer_party"), member
                ? $"{party.Name} [{GeneralSystems.PartySystem.GetIdeologyName(party.Ideology)}]"
                : partyName).ColorString("#C9A7E8"));
        }
        // 行政区卡片点一下直接打开对应的国家界面
        Kingdom province = o as Kingdom;
        UnityAction openKingdom = null;
        if (province != null && !province.isRekt())
        {
            tip.Add(LM.Get("bureau_open_kingdom_hint").ColorString("#7FD8EA"));
            openKingdom = () =>
            {
                if (province.isRekt()) return;
                SelectedMetas.selected_kingdom = province;
                ScrollWindow.showWindow("kingdom");
            };
        }

        return BuildNode(officeName, titleHex, officer,
            (vacant ? LM.Get("office_vacant") : officer.data.name).ColorString(vacant ? "#8FA0A8" : "#F2EEE2"),
            rankLine, footer, $"office_{oid}", string.Join("\n", tip),
            () => ChangeOfficer(officeObject), highlight: false, onClick: openKingdom);
    }

    // 金字塔顶端的君主
    private GameObject BuildSovereignNode()
    {
        Actor emperor = _empire.Emperor;
        string name = emperor == null ? LM.Get("office_vacant") : emperor.getName();
        return BuildNode(GeneralSystems.RepublicSystem.IsRepublic(_empire)
                ? GeneralSystems.RepublicSystem.GetHeadOfStateTitle(_empire) : LM.Get("bureau_sovereign"), "#F3C34A", emperor,
            name.ColorString(emperor == null ? "#8FA0A8" : "#F2EEE2"),
            _empire.GetEmpireName().ColorString("#65D6C4"), "", $"sovereign_{_empire.id}",
            $"{name}\n{_empire.GetEmpireName()}", null, highlight: true);
    }

    private List<string> GetPowerEntries(OfficeObject officeObject, Actor officer) =>
        officeObject.powers.Select(power => OfficeManager.AllPower.Contains(power)
            ? $"{power}{officer?.CalcPower(power, _empire).addition[power] ?? 0}"
            : power.ToString()).ToList();

    // "权能 人事5 · 军事10"：权能名 + 现任官员在这项上的加成；没有权能就写"无"。
    private string BuildPowerLine(OfficeObject officeObject, Actor officer)
    {
        string label = LM.Get("office_powers").ColorString("#A8B8BE");
        List<string> powers = GetPowerEntries(officeObject, officer);
        if (powers.Count == 0) return $"{label} {LM.Get("label_none")}";
        return $"{label} {string.Join(" · ", powers).ColorString("#65D66E")}";
    }

    // ── 卡片部件 ──
    // 官职卡和爵位卡共用：标题行(名称 + 小字角标 + 可选的更换按钮)，下面左头像、右几行信息。
    private const float CardWidth = 100f;
    private const float CardHeight = 55f;

    private void AddPersonCard(AutoGridLayoutGroup parent, string title, string badge, Actor actor,
        List<string> lines, UnityAction onChange)
    {
        parent.AddChild(BuildPersonCard(title, badge, actor, lines, onChange));
    }

    private GameObject BuildPersonCard(string title, string badge, Actor actor, List<string> lines,
        UnityAction onChange)
    {
        var card = this.BeginVertGroup(new Vector2(CardWidth, CardHeight), pSpacing: 1,
            pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(5, 5, 4, 3));

        var header = card.BeginHoriGroup(new Vector2(CardWidth - 10f, 10f), TextAnchor.MiddleLeft, 2);
        string headerText = string.IsNullOrEmpty(badge) ? title : $"{title} {badge.ColorString("#8FA0A8")}";
        var titleText = header.AddTextIntoHoriLayout(headerText, true, TextAnchor.MiddleLeft,
            new Vector2(onChange == null ? CardWidth - 12f : CardWidth - 23f, 10f));
        titleText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        if (onChange != null)
            header.AddButtonIntoHoriLayout("change_officer", "", onChange,
                SpriteTextureLoader.getSprite("ui/changeOfficer"), size: new Vector2(9, 9), showTip: true);

        var body = card.BeginHoriGroup(new Vector2(CardWidth - 10f, CardHeight - 18f), TextAnchor.MiddleLeft, 2);
        body.AddActorViewIntoHoriLayout(actor);
        var info = body.BeginVertGroup(new Vector2(CardWidth - 44f, CardHeight - 18f), pSpacing: 0,
            pAlignment: TextAnchor.MiddleLeft);
        for (int i = 0; i < lines.Count; i++)
        {
            var line = info.AddTextIntoVertLayout(lines[i], true, TextAnchor.MiddleLeft,
                new Vector2(CardWidth - 44f, 8.5f));
            line.UseFixedFontSize(i == 0 ? 6 : 5, HorizontalWrapMode.Overflow);
        }

        card.transform.AddStretchBackground("FactionFrame", size: new Vector2(CardWidth, CardHeight));
        pool.Add(card.gameObject);
        return card.gameObject;
    }

    // ── 官职金字塔 ──
    // 跟制度科技树同一套 GraphView：可拖拽平移、滚轮缩放。自上而下按品级一层一层往下排——
    // 君主 → 后宫 → 中央部门 → 下级部门 → 行政区，每一层居中，越往下人越多，整体呈金字塔。
    // 一层超过 MaxCardsPerRow 张就折成几行，免得最底下的行政区一行拉得太长。
    private const float GraphWidth = 204f;
    private const float GraphHeight = 300f;
    private const int MaxCardsPerRow = 6;
    private const float NodeWidth = 72f;
    private const float NodeHeight = 80f;
    private const float CardGapX = 6f;
    private const float RowGapY = 12f;
    private const float TierLabelHeight = 14f;
    private const float GraphPadding = 12f;
    private const string HaremTitleHex = "#F29BC0";
    private const string OfficeTitleHex = "#F3C34A";
    private GraphView _officeGraph;

    private void ShowOfficePyramid()
    {
        var space = this.BeginVertGroup(pSpacing: 2, pAlignment: TextAnchor.UpperCenter);
        pool.Add(space.gameObject);
        AddSectionHeader(space, LM.Get("bureau_office_pyramid"), "ui/icons/iconKingdom");
        var header = space.BeginHoriGroup(new Vector2(GraphWidth, 12f), TextAnchor.MiddleCenter, 3);
        var hint = header.AddTextIntoHoriLayout(LM.Get("bureau_graph_hint").ColorString("#8FA0A8"), true,
            TextAnchor.MiddleLeft, new Vector2(GraphWidth - 40f, 11f));
        hint.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        header.AddButtonIntoHoriLayout("institution_graph_reset", LM.Get("institution_graph_reset"),
            () => _officeGraph?.ResetView(), size: new Vector2(32, 10));
        _officeGraph = GraphView.Create(space.transform, new Vector2(GraphWidth, GraphHeight),
            GraphOrientation.Vertical, objectName: "BureauOfficeGraph");
        _officeGraph.SetClampMargin(new Vector2(NodeWidth / 2f, NodeHeight / 2f));

        var tiers = new List<(string label, List<GameObject> cards)>
        {
            ("", new List<GameObject> { BuildSovereignNode() })
        };
        void AddTier(string labelKey, IEnumerable<GameObject> cards)
        {
            List<GameObject> built = cards.Where(card => card != null).ToList();
            if (built.Count > 0) tiers.Add((LM.Get(labelKey), built));
        }
        CenterOffice office = _empire.data.centerOffice;
        AddTier("Harems", office.Harems.Select(oid => BuildOfficeNode(oid, HaremTitleHex)));
        AddTier("CoreOffice", office.CoreOffices.Select(oid => BuildOfficeNode(oid, OfficeTitleHex)));
        AddTier("Divisions", office.Divisions.Select(oid => BuildOfficeNode(oid, OfficeTitleHex)));
        AddTier("province", _empire.kingdoms_hashset.Select(kingdom =>
            BuildOfficeNode(kingdom.GetOfficeID(), OfficeTitleHex, kingdom)));
        LayoutPyramid(tiers);
    }

    private void LayoutPyramid(List<(string label, List<GameObject> cards)> tiers)
    {
        // 先把每层折成行，算出整张图的尺寸，再从上往下摆
        var rows = new List<(string label, List<GameObject> cards)>();
        foreach ((string label, List<GameObject> cards) in tiers)
            for (int start = 0; start < cards.Count; start += MaxCardsPerRow)
                rows.Add((start == 0 ? label : "", cards.Skip(start).Take(MaxCardsPerRow).ToList()));

        float RowWidth(int count) => count * NodeWidth + (count - 1) * CardGapX;
        float contentWidth = rows.Max(row => RowWidth(row.cards.Count)) + GraphPadding * 2f;
        float contentHeight = GraphPadding * 2f + rows.Sum(row =>
            NodeHeight + (string.IsNullOrEmpty(row.label) ? 0f : TierLabelHeight)) + (rows.Count - 1) * RowGapY;

        Transform content = _officeGraph.ContentTransform;
        Vector2 half = new Vector2(NodeWidth / 2f, NodeHeight / 2f);
        var edgeColor = new Color(0.53f, 0.78f, 0.86f, 0.75f);
        float top = contentHeight / 2f - GraphPadding;
        float? previousRowY = null;
        foreach ((string label, List<GameObject> cards) in rows)
        {
            if (!string.IsNullOrEmpty(label))
            {
                CreateTierLabel(content, label, new Vector2(0f, top - TierLabelHeight / 2f));
                top -= TierLabelHeight;
            }
            float rowY = top - NodeHeight / 2f;
            float startX = -RowWidth(cards.Count) / 2f + NodeWidth / 2f;
            for (int i = 0; i < cards.Count; i++)
            {
                var position = new Vector2(startX + i * (NodeWidth + CardGapX), rowY);
                // 连线从上一层的中轴垂下来再分到每张卡片，像一棵倒挂的树
                if (previousRowY.HasValue)
                    _officeGraph.CreateElbow(new Vector2(0f, previousRowY.Value), position, half, edgeColor);
                PlaceInGraph(cards[i], content, position);
            }
            previousRowY = rowY;
            top = rowY - NodeHeight / 2f - RowGapY;
        }

        var contentSize = new Vector2(contentWidth, contentHeight);
        // 打开时按宽度缩到尽量装下(不低于 GraphView 的下限)，顶端贴住视口上沿，从君主开始往下看
        float fitScale = Mathf.Clamp(GraphWidth / contentWidth, 0.45f, 1f);
        var fitPosition = new Vector2(0f, GraphHeight / 2f - contentHeight * fitScale / 2f);
        _officeGraph.SetContent(contentSize, fitScale, fitPosition);
    }

    private static void PlaceInGraph(GameObject card, Transform content, Vector2 position)
    {
        var rect = card.GetComponent<RectTransform>();
        rect.SetParent(content, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.anchoredPosition = position;
    }

    // 层级标题做成一枚深色小胶囊，压在连线上，比裸字清楚
    private static void CreateTierLabel(Transform content, string text, Vector2 position)
    {
        SimpleText pill = Instantiate(SimpleText.Prefab, content);
        pill.Setup(text.ColorString("#F3C34A"), TextAnchor.MiddleCenter, new Vector2(64f, 11f));
        pill.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        RectTransform rect = pill.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.anchoredPosition = position;
        foreach (Graphic graphic in pill.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
    }

    // 竖版节点卡：标题条 / 头像 / 姓名 / 官阶 / 权能，右上角一个小的更换按钮。
    // 用绝对坐标摆放，不走布局组——布局组在这么小的卡片里总是把文字挤到一角。
    private GameObject BuildNode(string title, string titleHex, Actor actor, string nameLine, string rankLine,
        string footerLine, string tipKey, string tipBody, UnityAction onChange, bool highlight,
        UnityAction onClick = null)
    {
        var root = new GameObject("OfficeNode", typeof(RectTransform), typeof(CanvasGroup));
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(_officeGraph.ContentTransform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(NodeWidth, NodeHeight);
        root.transform.AddStretchBackground(highlight ? "FactionFrame_dominate" : "FactionFrame",
            new Vector2(NodeWidth, NodeHeight));
        // 空缺的官位整体压暗
        root.GetComponent<CanvasGroup>().alpha = actor == null ? 0.7f : 1f;

        SimpleText titleBar = Instantiate(SimpleText.Prefab, rect);
        titleBar.Setup(title.ColorString(titleHex), TextAnchor.MiddleCenter, new Vector2(NodeWidth - 12f, 11f));
        titleBar.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        titleBar.text.fontStyle = FontStyle.Bold;
        PlaceChild(titleBar.GetComponent<RectTransform>(), new Vector2(0f, NodeHeight / 2f - 10f));
        foreach (Graphic graphic in titleBar.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;

        var avatar = this.BeginHoriGroup(new Vector2(30f, 30f), TextAnchor.MiddleCenter, 0);
        avatar.AddActorViewIntoHoriLayout(actor);
        PlaceChild(avatar.GetComponent<RectTransform>(), new Vector2(0f, 9f), rect);

        CreateNodeText(rect, nameLine, -13f, 6, FontStyle.Bold);
        CreateNodeText(rect, rankLine, -21f, 5, FontStyle.Normal);
        if (!string.IsNullOrEmpty(footerLine)) CreateNodeText(rect, footerLine, -29f, 5, FontStyle.Normal);

        if (onChange != null)
        {
            var buttonHolder = this.BeginHoriGroup(new Vector2(9f, 9f), TextAnchor.MiddleCenter, 0);
            buttonHolder.AddButtonIntoHoriLayout("change_officer", "", onChange,
                SpriteTextureLoader.getSprite("ui/changeOfficer"), size: new Vector2(8f, 8f), showTip: true);
            PlaceChild(buttonHolder.GetComponent<RectTransform>(),
                new Vector2(NodeWidth / 2f - 8f, NodeHeight / 2f - 17f), rect);
        }

        if (onClick != null)
        {
            // 点击整张卡片；拖拽平移时 Unity 不会再派发点击，不会误触
            Button button = root.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);
        }
        UIHelper.AttachTextTooltip(root, $"bureau_{tipKey}", title, tipBody);
        pool.Add(root);
        return root;
    }

    private static void PlaceChild(RectTransform child, Vector2 position, RectTransform parent = null)
    {
        if (parent != null) child.SetParent(parent, false);
        var element = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
        element.ignoreLayout = true;
        child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
        child.pivot = new Vector2(0.5f, 0.5f);
        child.localScale = Vector3.one;
        child.anchoredPosition = position;
    }

    private static void CreateNodeText(RectTransform parent, string text, float y, int fontSize, FontStyle style)
    {
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        var rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(NodeWidth - 8f, 9f);
        rect.anchoredPosition = new Vector2(0f, y);
        Text label = textObject.GetComponent<Text>();
        label.font = LocalizedTextManager.current_font;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.supportRichText = true;
        label.raycastTarget = false;
        label.color = Color.white;
        label.text = text;
    }

    // 分区标题：一条深色细条，左侧图标 + 金色标题(原来是一整块灰底大字)
    private static void AddSectionHeader(AutoVertLayoutGroup space, string text, params string[] icons)
    {
        var bar = space.BeginHoriGroup(new Vector2(204f, 15f), TextAnchor.MiddleCenter, 3);
        UIHelper.AddInsetBackground(bar, new Vector2(204f, 15f));
        UIHelper.AddLayoutIcon(bar.transform, UIHelper.FirstSprite(icons), 11f);
        var label = bar.AddTextIntoHoriLayout(text.ColorString("#F3C34A"), true, TextAnchor.MiddleLeft,
            new Vector2(180f, 13f));
        label.UseFixedFontSize(9, HorizontalWrapMode.Overflow);
    }

    private static void AddSubHeader(AutoVertLayoutGroup space, string text)
    {
        var label = space.AddTextIntoVertLayout(text, true, TextAnchor.MiddleCenter, new Vector2(204f, 11f));
        label.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
    }

    private void ChangeOfficer(OfficeObject o=null, Kingdom province=null)
    {
        ConfigData.CURRENT_SELECTED_OFFICE = o;
        SelectedMetas.selected_city = null;
        ScrollWindow.showWindow(nameof(ChangeUnitWindow));
    }
}
