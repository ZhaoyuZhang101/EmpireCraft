using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Layer;
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
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;
public class CultureSpeciesPairWindow : AutoLayoutWindow<CultureSpeciesPairWindow>
{
    public ListPool<GameObject> gameObjects = new ListPool<GameObject>();
    TextInput searchInput;
    public string _currentSelectedSpecies = "";
    public Dictionary<string, TextInput> TextInputs = new Dictionary<string, TextInput>();
    public AutoGridLayoutGroup _gridGroup;
    protected override void Init()
    {
        TextInputs = new Dictionary<string, TextInput>();
        AutoHoriLayoutGroup hGroup = this.BeginHoriGroup();
        SimpleText Text1 = Instantiate(SimpleText.Prefab);
        Text1.Setup(LM.Get("current_exist_culture"), pAlignment: TextAnchor.MiddleCenter, new Vector2(100, 20));
        SimpleButton insertAllCulture = Instantiate(SimpleButton.Prefab);
        insertAllCulture.Setup(InsertAllCulture, SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"), LM.Get("insert_all_culture"), new Vector2(35, 20));
        insertAllCulture.Button.OnHover(() => 
        {
            Tooltip.show(gameObjects, "normal", new TooltipData()
            {
                tip_name = "insert_all_culture",
                tip_description = "insert_all_culture_description"
            });
        });
        insertAllCulture.Button.OnHoverOut(Tooltip.hideTooltip);
        hGroup.AddChild(Text1.gameObject);
        hGroup.AddChild(insertAllCulture.gameObject);

        _gridGroup = this.BeginGridGroup(6, pCellSize:new Vector2(25, 14));
        AddChild(hGroup.gameObject);
        AddChild(_gridGroup.gameObject);
        searchInput = Instantiate(TextInput.Prefab);
        searchInput.Setup(LM.Get("input_species"), StartSearch);
        searchInput.SetSize(new Vector2(180, 20));
        AddChild(searchInput.gameObject);

        Show(ConfigData.AllCivSpecies);
    }

    public override void OnFirstEnable()
    {
        base.OnFirstEnable();
        foreach (var culture in ConfigData.currentExistCulture)
        {
            var language = PlayerConfig.detectLanguage();
            var tc = OnomasticsRule.ALL_CULTURE_TRANSLATE[culture];
            var translate = "";
            switch (language)
            {
                case "ch":
                    translate = tc.ch;
                    break;
                case "en":
                    translate = tc.en;
                    break;
                case "cz":
                    translate = tc.cz;
                    break;
                default:
                    translate = tc.en;
                    break;
            }
            translate = string.IsNullOrEmpty(translate) ? tc.en : translate;
            _gridGroup.AddButtonIntoGirdLayout(culture, translate, ()=>SetCulture(culture), size:new Vector2(20, 14));
        }
    }

    public void SetCulture(string cultureName)
    {
        if (!string.IsNullOrEmpty(_currentSelectedSpecies))
        {
            TextInputs[_currentSelectedSpecies].input.text = cultureName;
            ConfigData.speciesCulturePair[_currentSelectedSpecies] = cultureName;
            WorldTip.showNow("set_culture_complete", true, "top", 3f, "#F3961F");
            _currentSelectedSpecies = "";
            try
            {
                string SCP = JsonConvert.SerializeObject(ConfigData.speciesCulturePair, Formatting.Indented);
                string parentFolder = Directory.GetParent(ModClass._declare.FolderPath)?.FullName;
                if (parentFolder != null)
                {
                    string path = Path.Combine(parentFolder, "CultureSpeciesPairPlayerConfig.json");

                    File.WriteAllText(path, SCP);
                    LogService.LogInfo("储存用户文化配置数据成功");
                }

            }
            catch (Exception e)
            {
                LogService.LogInfo("储存用户文化配置数据失败");
            }
            return;
        }
        WorldTip.showNow("please_select_species_first", true, "top", 3f, "#F3961F");
    }

    public void InsertAllCulture()
    {
        foreach (Culture culture in World.world.cultures) 
        {
            if (culture.species_id!="")
            {
                string cultureName = ConfigData.speciesCulturePair.TryGetValue(culture.species_id, out string name) ? name : "Western";
                CulturePatch.insertCultureTemplate(culture, cultureName);
            }
        }
    }

    public void ChangeCulture(string input, string civSpecies, TextInput inputText)
    {
        if (!ConfigData.currentExistCulture.Contains(input)) 
        {
            inputText.input.text = "";
            return;
        }
        ConfigData.speciesCulturePair[civSpecies] = input;
        try
        {
            string SCP = JsonConvert.SerializeObject(ConfigData.speciesCulturePair, Formatting.Indented);
            string parentFolder = Directory.GetParent(ModClass._declare.FolderPath)?.FullName;
            if (parentFolder != null)
            {
                string path = Path.Combine(parentFolder, "CultureSpeciesPairPlayerConfig.json");

                File.WriteAllText(path, SCP);
                LogService.LogInfo("储存用户文化配置数据成功");
            }
        }
        catch (Exception e)
        {
            LogService.LogInfo("储存用户文化配置数据失败");
        }
    }

    public void StartSearch(string input) 
    {
        if (input=="")
        {
            searchInput.input.text = LM.Get("input_species");
        }
        Clear();
        List<ActorAsset> species = ConfigData.AllCivSpecies.FindAll(a=>a.id.Contains(input)||a.getLocaleID().Contains(input)||a.getLocalizedDescription().Contains(input)||a.getLocalizedName().Contains(input));
        Show(species);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        Clear();
        Show(ConfigData.AllCivSpecies);
    }

    public void StartSelectCulture(string species)
    {
        _currentSelectedSpecies = species;
        WorldTip.showNow("speciesSelected", true, "top", 3f, "#F3961F");
    }
    public void Show(List<ActorAsset> species)
    {
        foreach (var civSpecies in species)
        {
            AutoVertLayoutGroup wholeView = this.BeginVertGroup(pSpacing: 3);
            // Create a horizontal layout group for each civSpecies
            AutoHoriLayoutGroup pairGroup = this.BeginHoriGroup(pSpacing: 3);
            var button = pairGroup.AddButtonIntoHoriLayout("icon", "", () => StartSelectCulture(civSpecies.id), civSpecies.getSpriteIcon(), hideBackground:true, size: new Vector2(15, 15));
            button.Background.enabled = false;
            // Create a new SimpleText instance for each civSpecies
            SimpleText SpeciesText = Instantiate(SimpleText.Prefab);
            SpeciesText.Setup(civSpecies.getLocalizedName()+":", pSize: new Vector2(40, 15));
            SpeciesText.background.enabled = false;

            TextInput inputField = Instantiate(TextInput.Prefab);
            inputField.Setup(ConfigData.speciesCulturePair.TryGetValue(civSpecies.id, out string culture) ? culture : "", newValue => ChangeCulture(newValue, civSpecies.id, inputField));
            inputField.SetSize(new Vector2(100, 18));
            pairGroup.AddChild(SpeciesText.gameObject);
            pairGroup.AddChild(inputField.gameObject);
            TextInputs[civSpecies.id] = inputField;
            ////设置按钮
            //AutoHoriLayoutGroup settingGroup = this.BeginHoriGroup(pSpacing: 3);

            //AutoVertLayoutGroup singleGroup1 = this.BeginVertGroup(pSpacing: 3);
            //SimpleText settingName1 = Instantiate(SimpleText.Prefab);
            //settingName1.Setup("姓名对调", TextAnchor.MiddleCenter);
            //SimpleButton toggle1 = UIHelper.CreateToggleButton(() => ToggleInverseName(civSpecies));

            


            wholeView.AddChild(pairGroup.gameObject);



            gameObjects.Add(wholeView.gameObject);
        }
    }

    public static void setToggle(bool toggle, SimpleButton button)
    {
        if (toggle)
        {
            button.Icon.sprite = SpriteTextureLoader.getSprite("ui/toggle_open");
        }
        else
        {
            button.Icon.sprite = SpriteTextureLoader.getSprite("ui/toggle_close");
        }
    }

    private static void ToggleInverseName(string species)
    {
        //if (ConfigData.speciesCulturePair.TryGetValue(species, out string culture)) 
        //{
        //    bool togV = false;
        //    OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting rule);
        //    setToggle(togV, button);
        //}
    }

    public void Clear()
    {
        float delay = 0.005f;
        foreach(GameObject go in gameObjects)
        {
            go.SetActive(false);
            GameObject.Destroy(go, delay);
            delay += 0.005f;
        }
        gameObjects.Clear();
    }
}
