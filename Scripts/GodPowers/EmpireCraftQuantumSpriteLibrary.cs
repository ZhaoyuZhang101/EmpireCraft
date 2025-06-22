using EmpireCraft.Scripts.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;
public static class EmpireCraftQuantumSpriteLibrary
{
    public static void init ()
    {
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "city_line",
            id_prefab = "p_mapArrow_line",
            base_scale = 0.5f,
            draw_call = drawCityLine,
            render_map = true,
            render_gameplay = true,
            color = new Color(0.4f, 0.4f, 1f, 0.9f)
        });
        AssetManager.quantum_sprites.add(new QuantumSpriteAsset
        {
            id = "empire_line",
            id_prefab = "p_mapArrow_line",
            base_scale = 0.5f,
            draw_call = drawKingdomLine,
            render_map = true,
            render_gameplay = true,
            color = new Color(0.4f, 0.4f, 1f, 0.9f)
        });
    }


    private static void drawCityLine(QuantumSpriteAsset pAsset)
    {
        if (!InputHelpers.mouseSupported || World.world.isBusyWithUI() || !World.world.isSelectedPower("add_title"))
        {
            return;
        }
        City unity_A = ConfigData.selected_cityA;
        if (unity_A == null)
        {
            return;
        }
        Vector2 mousePos = World.world.getMousePos();
        Color pColor = unity_A.getColor().getColorMain2();
        QuantumSpriteLibrary.drawArrowQuantumSprite(pAsset, unity_A.getTile().posV, mousePos, ref pColor);
    }


    private static void drawKingdomLine(QuantumSpriteAsset pAsset)
    {
        if (!InputHelpers.mouseSupported || World.world.isBusyWithUI() || !World.world.isSelectedPower("create_empire"))
        {
            return;
        }
        Kingdom unity_A = Config.unity_A;
        if (unity_A == null)
        {
            return;
        }
        Vector2 mousePos = World.world.getMousePos();
        foreach(City city in unity_A.cities)
        {
            Color pColor = city.getColor().getColorMain2();
            QuantumSpriteLibrary.drawArrowQuantumSprite(pAsset, city.getTile().posV, mousePos, ref pColor);
        }

    }


}
