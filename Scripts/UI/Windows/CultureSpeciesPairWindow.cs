using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
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
        foreach (var civSpecies in ConfigData.AllCivSpecies)
        {
            // Create a horizontal layout group for each civSpecies
            AutoHoriLayoutGroup pairGroup = this.BeginHoriGroup(pSpacing:3);

            // Create a new SimpleText instance for each civSpecies
            SimpleText SpeciesText = Instantiate(SimpleText.Prefab);
            SpeciesText.Setup(civSpecies);

            TextInput inputField = Instantiate(TextInput.Prefab);
            inputField.Setup(ConfigData.speciesCulturePair.TryGetValue(civSpecies, out string culture)?culture:"", ChangeCulture);
        }
    }

    public void ChangeCulture(string input)
    {

    }
}
