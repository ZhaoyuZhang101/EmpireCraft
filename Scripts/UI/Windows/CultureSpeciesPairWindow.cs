using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 文化配置：给每个文明种族指定文化模板。
// 布局：顶部一行提示 + "注入文化"；文化面板(图标 + 名字)；搜索框；种族列表(种族 → 当前文化)。
// 用法：先点种族所在的一行(选中后高亮)，再点上方文化面板里的文化即完成分配。
public class CultureSpeciesPairWindow : AutoLayoutWindow<CultureSpeciesPairWindow>
{
    private const string Highlight = "#F3961F";
    private static readonly Vector2 CellSize = new(30, 30);

    private readonly List<GameObject> _rows = new();
    private TextInput _searchInput;
    private string _search = "";
    private string _selectedSpecies = "";
    private AutoGridLayoutGroup _palette;

    protected override void Init()
    {
        layout.spacing = 4;
        AutoHoriLayoutGroup header = this.BeginHoriGroup(pSpacing: 4, pAlignment: TextAnchor.MiddleCenter);
        SimpleText hint = Instantiate(SimpleText.Prefab);
        hint.Setup(LM.Get("culture_pair_hint"), TextAnchor.MiddleLeft, new Vector2(130, 16));
        hint.background.enabled = false;
        header.AddChild(hint.gameObject);
        SimpleButton insertAllCulture = Instantiate(SimpleButton.Prefab);
        insertAllCulture.Setup(InsertAllCulture, SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"),
            LM.Get("insert_all_culture"), new Vector2(45, 16));
        insertAllCulture.Button.OnHover(() => Tooltip.show(insertAllCulture.gameObject, "normal", new TooltipData
        {
            tip_name = "insert_all_culture",
            tip_description = "insert_all_culture_description"
        }));
        insertAllCulture.Button.OnHoverOut(Tooltip.hideTooltip);
        header.AddChild(insertAllCulture.gameObject);
        AddChild(header.gameObject);

        _palette = this.BeginGridGroup(6, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: CellSize,
            pSpacing: new Vector2(2, 2));
        AddChild(_palette.gameObject);

        _searchInput = Instantiate(TextInput.Prefab);
        _searchInput.Setup(LM.Get("input_species"), StartSearch);
        _searchInput.SetSize(new Vector2(180, 18));
        AddChild(_searchInput.gameObject);
    }

    public override void OnFirstEnable()
    {
        base.OnFirstEnable();
        foreach (string culture in ConfigData.currentExistCulture.OrderBy(c => c.GetCultureTranslate()))
        {
            string key = culture;
            AutoVertLayoutGroup cell = _palette.BeginVertGroup(pSize: CellSize, pSpacing: 0,
                pAlignment: TextAnchor.UpperCenter);
            AdvancedButton button = cell.AddButtonIntoVertLayout("culture_" + key, "", () => SetCulture(key),
                CultureIcons.Get(key), size: new Vector2(18, 18));
            button.Background.enabled = false;
            cell.AddTextIntoVertLayout(key.GetCultureTranslate(), true, TextAnchor.MiddleCenter, new Vector2(30, 10));
            _palette.AddChild(cell.gameObject);
        }
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        _selectedSpecies = "";
        Refresh();
    }

    private void StartSearch(string input)
    {
        _search = input == LM.Get("input_species") ? "" : input ?? "";
        Refresh();
    }

    private void SelectSpecies(string species)
    {
        _selectedSpecies = _selectedSpecies == species ? "" : species;
        if (!string.IsNullOrEmpty(_selectedSpecies))
            WorldTip.showNow("speciesSelected", true, "top", 3f, Highlight);
        Refresh();
    }

    private void SetCulture(string cultureName)
    {
        if (string.IsNullOrEmpty(_selectedSpecies))
        {
            WorldTip.showNow("please_select_species_first", true, "top", 3f, Highlight);
            return;
        }
        ConfigData.speciesCulturePair[_selectedSpecies] = cultureName;
        SavePairs();
        WorldTip.showNow("set_culture_complete", true, "top", 3f, Highlight);
        _selectedSpecies = "";
        Refresh();
    }

    private static void SavePairs()
    {
        try
        {
            string parentFolder = Directory.GetParent(ModClass._declare.FolderPath)?.FullName;
            if (parentFolder == null) return;
            File.WriteAllText(Path.Combine(parentFolder, "CultureSpeciesPairPlayerConfig.json"),
                JsonConvert.SerializeObject(ConfigData.speciesCulturePair, Formatting.Indented));
            LogService.LogInfo("储存用户文化配置数据成功");
        }
        catch (Exception e)
        {
            LogService.LogError($"储存用户文化配置数据失败: {e}");
        }
    }

    public void InsertAllCulture()
    {
        foreach (Culture culture in World.world.cultures)
        {
            if (culture.species_id == "") continue;
            string cultureName = ConfigData.speciesCulturePair.TryGetValue(culture.species_id, out string name)
                ? name
                : "Western";
            CulturePatch.insertCultureTemplate(culture, cultureName);
        }
    }

    private void Refresh()
    {
        foreach (GameObject row in _rows)
        {
            row.SetActive(false);
            Destroy(row);
        }
        _rows.Clear();
        IEnumerable<ActorAsset> species = ConfigData.AllCivSpecies;
        if (!string.IsNullOrWhiteSpace(_search))
            species = species.Where(a => a.id.Contains(_search) || a.getLocalizedName().Contains(_search));
        foreach (ActorAsset civSpecies in species) AddRow(civSpecies);
    }

    // 一行：种族图标 种族名 → 文化图标 文化名；整行点哪里都是选中该种族
    private void AddRow(ActorAsset civSpecies)
    {
        string id = civSpecies.id;
        bool selected = id == _selectedSpecies;
        string culture = ConfigData.speciesCulturePair.TryGetValue(id, out string c) ? c : "";
        AutoHoriLayoutGroup row = this.BeginHoriGroup(pSpacing: 3, pAlignment: TextAnchor.MiddleLeft);

        AdvancedButton speciesIcon = row.AddButtonIntoHoriLayout("species_" + id, "", () => SelectSpecies(id),
            civSpecies.getSpriteIcon(), size: new Vector2(15, 15));
        speciesIcon.Background.enabled = false;
        AdvancedButton speciesName = row.AddButtonIntoHoriLayout("species_name_" + id,
            (selected ? "▶ " : "") + civSpecies.getLocalizedName(), () => SelectSpecies(id), size: new Vector2(55, 15),
            hideBackground: true);
        speciesName.Text.color = selected ? Toolbox.makeColor(Highlight) : Color.white;
        speciesName.Text.alignment = TextAnchor.MiddleLeft;

        row.AddTextIntoHoriLayout("→", true, TextAnchor.MiddleCenter, new Vector2(12, 15));
        AdvancedButton cultureIcon = row.AddButtonIntoHoriLayout("species_culture_icon_" + id, "",
            () => SelectSpecies(id), CultureIcons.Get(culture), size: new Vector2(15, 15));
        cultureIcon.Background.enabled = false;
        AdvancedButton cultureName = row.AddButtonIntoHoriLayout("species_culture_" + id,
            string.IsNullOrEmpty(culture) ? LM.Get("culture_pair_unset") : culture.GetCultureTranslate(),
            () => SelectSpecies(id), size: new Vector2(60, 15), hideBackground: true);
        cultureName.Text.color = selected ? Toolbox.makeColor(Highlight) : new Color(0.85f, 0.85f, 0.85f);
        cultureName.Text.alignment = TextAnchor.MiddleLeft;

        AddChild(row.gameObject);
        _rows.Add(row.gameObject);
    }
}
