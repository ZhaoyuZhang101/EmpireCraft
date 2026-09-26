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
                sprite: SpriteTextureLoader.getSprite("ui/iconOptions"));
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
        InitialTopPartInfoLvLing();
        virtualPeeragesSpace = this.BeginVertGroup();
        var heading = Instantiate(SimpleText.Prefab);
        heading.Setup(LM.Get("empire_virtual_peerages"), TextAnchor.MiddleCenter);
        heading.UseFixedFontSize(13);
        virtualPeeragesSpace.AddChild(heading.gameObject);
        var legalHeading = Instantiate(SimpleText.Prefab);
        legalHeading.Setup(LM.Get("empire_legal_virtual_peerages").ColorString(pColor: new Color(0.35f, 0.85f, 1f)), TextAnchor.MiddleCenter);
        virtualPeeragesSpace.AddChild(legalHeading.gameObject);
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
                                   holder.GetSpecificClan() == _empire.EmpireSpecificClan;
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

        var honoraryHeading = Instantiate(SimpleText.Prefab);
        honoraryHeading.Setup(LM.Get("empire_honorary_virtual_peerages").ColorString(pColor: new Color(1f, 0.75f, 0.2f)), TextAnchor.MiddleCenter);
        virtualPeeragesSpace.AddChild(honoraryHeading.gameObject);
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
                                       actor.GetSpecificClan() == _empire.EmpireSpecificClan;
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
        var card = this.BeginHoriGroup(pSpacing: -10, pAlignment: TextAnchor.MiddleCenter, pSize: new Vector2(100, 70));
        var avatar = this.BeginVertGroup(pSpacing: -3, pAlignment: TextAnchor.MiddleCenter);
        Color titleColor = isVacant ? Color.gray : honorary ? new Color(1f, 0.75f, 0.2f) : new Color(0.35f, 0.85f, 1f);
        avatar.AddTextIntoVertLayout(displayName.ColorString(pColor: titleColor), true, TextAnchor.MiddleCenter);
        avatar.AddActorViewIntoVertLayout(actor);
        card.AddChild(avatar.gameObject);

        var details = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        string holderName = actor?.getName() ?? LM.Get("label_vacant");
        string fiefText = honorary ? LM.Get("label_honorary") : fief;
        string fiefStatus = hasLandedFief ? LM.Get("label_landed_fief") : LM.Get("label_unlanded_fief");
        details.AddTextIntoVertLayout(
            $"{LM.Get("i_name")}: {holderName.ColorString(pColor: isVacant ? Color.gray : new Color(0.25f, 0.9f, 0.55f))}\n" +
            $"{LM.Get("OfficialLevel")}: {peerage.ColorString(pColor: titleColor)}\n" +
            $"{LM.Get("label_fief")}: {fiefText.ColorString(pColor: honorary ? new Color(1f, 0.6f, 0.35f) : new Color(0.35f, 0.85f, 1f))}" +
            (honorary ? "" : $"\n{LM.Get("label_fief_status")}: {fiefStatus.ColorString(pColor: hasLandedFief ? new Color(0.25f, 0.9f, 0.55f) : Color.gray)}"),
            true, TextAnchor.MiddleCenter, new Vector2(40, 25));
        card.AddChild(details.gameObject);
        parent.AddChild(card.gameObject);
        card.transform.AddStretchBackground("FactionFrame", size: new Vector2(100, 55));
        pool.Add(card.gameObject);
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
        topSpace = this.BeginHoriGroup();
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
            UIHelper.AddFactionCard(dominateFaction, _empire.CoreKingdom, parentH:topSpace);
        }
        
        topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset:new Vector2(0, 0));
    }
    [Hotfixable]
    private void InitialTopPartInfoFeudalism()
    {
        //总容器
        topSpace = this.BeginHoriGroup();
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
            UIHelper.AddFactionCard(dominate, _empire.CoreKingdom, parentH:topSpace);
        }
        
        topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset:new Vector2(0, 0));
    }
    [Hotfixable]
    private void InitialTopPartInfoNormal()
    {
        //总容器
        topSpace = this.BeginHoriGroup();
        topSpace.transform.AddStretchBackground("clanFrame", new Vector2(220, 100));

        var centerPart = topSpace.BeginVertGroup(pSpacing:-3);
        centerPart.AddTextIntoVertLayout(
            $"{LM.Get("empire_bureau_no_cabinet")}\n{LM.Get("empire_bureau_direct_rule")}",
            true, TextAnchor.MiddleCenter, new Vector2(80, 40));
        var dominate = _empire.CoreKingdom.GetRegime().GetDominateFaction();
        if (dominate != null)
        {
            UIHelper.AddFactionCard(dominate, _empire.CoreKingdom, parentH:topSpace);
        }
        topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset:new Vector2(0, 0));
    }
    public void ShowCoreSpace()
    {
        coreOfficeSpace = this.BeginVertGroup();
        //中央核心部门
        SimpleText coreOfficeTitle = Instantiate(SimpleText.Prefab);
        coreOfficeTitle.Setup(LM.Get("CoreOffice"), TextAnchor.MiddleCenter);
        coreOfficeSpace.AddChild(coreOfficeTitle.gameObject);

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
        SimpleText divisionsTitle = Instantiate(SimpleText.Prefab);
        divisionsTitle.Setup(LM.Get("Divisions"), TextAnchor.MiddleCenter);
        divisionsSpace.AddChild(divisionsTitle.gameObject);

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
        SimpleText haremsTitle = Instantiate(SimpleText.Prefab);
        haremsTitle.Setup(LM.Get("Harems"), TextAnchor.MiddleCenter);
        haremsSpace.AddChild(haremsTitle.gameObject);

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
        SimpleText provinceTitle = Instantiate(SimpleText.Prefab);
        provinceTitle.Setup(LM.Get("province"), TextAnchor.MiddleCenter);
        provincesSpace.AddChild(provinceTitle.gameObject);

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
        topSpace = this.BeginHoriGroup();
        topSpace.transform.AddStretchBackground("clanFrame", new Vector2(220, 100));

        var centerPart = topSpace.BeginVertGroup(pSpacing: -3);
        // 总理大臣：议会选举产生
        string government = string.IsNullOrWhiteSpace(view.GovernmentType)
            ? LM.Get("prime_minister_vacant")
            : string.Format(LM.Get("prime_minister_status"), LM.Get($"parliament_government_{view.GovernmentType}"),
                view.GovernmentSeats, view.TotalSeats);
        centerPart.AddTextIntoVertLayout($"{LM.Get("prime_minister_title")} · {government}", true,
            TextAnchor.MiddleCenter, new Vector2(110, 10));
        centerPart.AddActorViewIntoVertLayout(view.PrimeMinister,
            description: view.PrimeMinisterFaction == null
                ? LM.Get("label_none")
                : view.PrimeMinisterFaction.Name.ColorString(pColor: GoverningColor));

        // 议员：按议席顺序排列，每行最多 5 人
        centerPart.AddTextIntoVertLayout(string.Format(LM.Get("parliament_members_title"), view.Term,
            view.YearsUntilElection), true, TextAnchor.MiddleCenter, new Vector2(110, 10));
        const int perRow = 5;
        for (int start = 0; start < view.Seats.Count; start += perRow)
        {
            var row = centerPart.BeginHoriGroup(pSpacing: -5);
            foreach (GeneralSystems.ParliamentSeatView seat in view.Seats.Skip(start).Take(perRow))
            {
                string label = seat.Faction?.Name ?? LM.Get("label_none");
                if (seat.Representative == null) label += $"({LM.Get("parliament_seat_vacant")})";
                row.AddActorViewIntoHoriLayout(seat.Representative,
                    description: label.ColorString(pColor: GetSeatColor(seat.Governing, seat.Constitutionalist)));
            }
        }

        // 说明：悬停查看议员与总理如何产生
        SimpleText hint = centerPart.AddTextIntoVertLayout(LM.Get("parliament_how_hint").ColorString("#8FA0A8"),
            false, TextAnchor.MiddleCenter, new Vector2(110, 10));
        ConstitutionConfig config = GeneralSystems.InstitutionDefinitionRegistry.Global.constitution;
        UIHelper.AttachTextTooltip(hint.gameObject, $"parliament_how_{_empire.id}", LM.Get("parliament_title"),
            string.Format(LM.Get("parliament_how_body"), config.parliament_seats, config.parliament_term_years) +
            (view.ResponsibleGovernment ? "\n" + LM.Get("parliament_responsible_government_body") : ""));

        // 右侧：议席分布与图例
        var seatPart = topSpace.BeginVertGroup(new Vector2(60, 90), pSpacing: 0, pAlignment: TextAnchor.MiddleCenter);
        seatPart.AddTextIntoVertLayout(LM.Get("parliament_seat_distribution"), true, TextAnchor.MiddleCenter,
            new Vector2(60, 10));
        foreach (GeneralSystems.ParliamentFactionView faction in view.Factions)
        {
            string stance = LM.Get(faction.Constitutionalist ? "parliament_stance_pro" : "parliament_stance_anti");
            seatPart.AddTextIntoVertLayout(
                string.Format(LM.Get("parliament_faction_line"), faction.Faction.Name, faction.Seats,
                    faction.CentralRatio, stance).ColorString(pColor: GetSeatColor(faction.Governing, faction.Constitutionalist)),
                true, TextAnchor.MiddleCenter, new Vector2(60, 10));
        }
        seatPart.AddTextIntoVertLayout(LM.Get("parliament_legend"), true, TextAnchor.MiddleCenter, new Vector2(60, 20));
        if (GeneralSystems.ParliamentSystem.KeepsCabinet(_empire))
        {
            int electors = _empire.GetCabinetMembers().Count(actor => actor != null && !actor.isRekt());
            seatPart.AddTextIntoVertLayout(string.Format(LM.Get("parliament_electoral_college_note"), electors), true,
                TextAnchor.MiddleCenter, new Vector2(60, 10));
        }

        topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset: new Vector2(0, 0));
    }

    private void BuildBureauPage()
    {
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
        if (_empire.data.centerOffice.Harems.Count > 0) ShowHaremSpace();
        if (_empire.data.centerOffice.CoreOffices.Count > 0) ShowCoreSpace();
        if (_empire.data.centerOffice.Divisions.Count > 0) ShowDivisionSpace();
        ShowProvincesSpace();
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
        //寻找存在的官制
        if (!OfficeManager.Offices.TryGetValue(oid, out var officeObject))
        {
            return;
        }
        AutoHoriLayoutGroup officePositionGroup = this.BeginHoriGroup(pSpacing:-10, pAlignment: TextAnchor.MiddleCenter, pSize:new (100, 70));

        //右边头像
        AutoVertLayoutGroup avatarLayoutGroup = this.BeginVertGroup(pSpacing:-3, pAlignment: TextAnchor.MiddleCenter);
        avatarLayoutGroup.AddTextIntoVertLayout(officeObject.GetOfficeName(o)+$"({officeObject.history_officers.Count})", true, TextAnchor.MiddleCenter);
        avatarLayoutGroup.AddActorViewIntoVertLayout(officeObject.GetActor());

        SimpleButton changeAvatar = Instantiate(SimpleButton.Prefab);
        changeAvatar.Setup(() => ChangeOfficer(officeObject), SpriteTextureLoader.getSprite("ui/changeOfficer"), pSize: new Vector2(20, 10));
        
        avatarLayoutGroup.AddChild(changeAvatar.gameObject);
        
        officePositionGroup.AddChild(avatarLayoutGroup.gameObject);

        //左边信息栏
        AutoVertLayoutGroup leftVertGroup = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);

        var content =
            $"{LM.Get("i_name")}: {(officeObject.GetActor() == null ? "-" : officeObject.GetActor().data.name)}\n" +
            $"{LM.Get("OfficialLevel").ColorString(pColor: new Color(0.2f, 0.7f, 0.4f))}: {officeObject.GetName(o)}\n" +
            $"{LM.Get("i_on_office_time")}: {officeObject.GetOnTime()}";
        
        leftVertGroup.AddTextIntoVertLayout(content, true, TextAnchor.MiddleCenter, new Vector2(40, 25));
        var powerContent = "<权能>\n";
        if (officeObject.powers.Count <= 0)
        {
            powerContent += "无";
        }
        else
        {
            foreach (var power in officeObject.powers)
            {
                if (OfficeManager.AllPower.Contains(power))
                {
                    powerContent += $"{power}({officeObject.GetActor()?.CalcPower(power, _empire).addition[power]??0})".ColorString(pColor:new Color(0.0f, 1, 0.5f))+"\n";
                }
                else
                {
                    powerContent += power.ToString().ColorString(pColor:new Color(0.0f, 1, 0.5f))+"\n";
                }
            }
        }
        leftVertGroup.AddTextIntoVertLayout(powerContent, true, TextAnchor.MiddleCenter, new Vector2(40, 20));
        
        leftVertGroup.transform.localPosition = Vector3.zero;
        officePositionGroup.AddChild(leftVertGroup.gameObject);

        parent.AddChild(officePositionGroup.gameObject);
        
        officePositionGroup.transform.AddStretchBackground("FactionFrame", size:new Vector2(100, 55));
        pool.Add(officePositionGroup.gameObject);
    }

    private void ChangeOfficer(OfficeObject o=null, Kingdom province=null)
    {
        ConfigData.CURRENT_SELECTED_OFFICE = o;
        SelectedMetas.selected_city = null;
        ScrollWindow.showWindow(nameof(ChangeUnitWindow));
    }
}
