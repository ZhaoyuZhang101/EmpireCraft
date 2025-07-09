using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;
public class CultureSpeciesPairWindow : AutoLayoutWindow<CultureSpeciesPairWindow>
{
    protected override void Init()
    {
        SimpleText Text1 = Instantiate(SimpleText.Prefab);
        Text1.Setup(LM.Get("current_exist_cuture"));

        SimpleText Text2 = Instantiate(SimpleText.Prefab);
        string content = String.Join(",", ConfigData.currentExistCulture);
        Text2.Setup(content);
        AddChild(Text1.gameObject);
        AddChild(Text2.gameObject);
        foreach (var civSpecies in ConfigData.AllCivSpecies)
        {
            // Create a horizontal layout group for each civSpecies
            AutoHoriLayoutGroup pairGroup = this.BeginHoriGroup(pSpacing:3);

            // Create a new SimpleText instance for each civSpecies
            SimpleText SpeciesText = Instantiate(SimpleText.Prefab);
            SpeciesText.Setup(civSpecies);

            TextInput inputField = Instantiate(TextInput.Prefab);
            inputField.Setup(ConfigData.speciesCulturePair.TryGetValue(civSpecies, out string culture) ? culture : "", newValue =>ChangeCulture(newValue, civSpecies));
            pairGroup.AddChild(SpeciesText.gameObject);
            pairGroup.AddChild(inputField.gameObject);
            AddChild(pairGroup.gameObject);
        }
    }

    public void ChangeCulture(string input, string civSpecies)
    {
        ConfigData.speciesCulturePair[civSpecies] = input;
    }
}
