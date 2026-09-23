using db;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;
using static EmpireCraft.Scripts.GameClassExtensions.FamilyExtension;
using static EmpireCraft.Scripts.GameClassExtensions.KingdomExtension;
using static EmpireCraft.Scripts.GameClassExtensions.WarExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ReligionExtension;

namespace EmpireCraft.Scripts.GamePatches;
public class DBManagerPatch:GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {

        //new Harmony(nameof(on_quit)).Patch(
        //    AccessTools.Method(typeof(DBManager), nameof(DBManager.clearAndClose)),
        //    prefix: new HarmonyMethod(GetType(), nameof(on_quit))
        //);

        //new Harmony(nameof(on_application_quit)).Patch(
        //    AccessTools.Method(typeof(DBManager), nameof(DBManager.OnApplicationQuit)),
        //    prefix: new HarmonyMethod(GetType(), nameof(on_application_quit))
        //);
    }

    public static void on_application_quit(DBManager __instance)
    {
        ModClass.IS_CLEAR = true;
        AllClear();
    }

    public static void on_quit(DBManager __instance)
    {
        ModClass.IS_CLEAR = true;
    }

    public static void AllClear()
    {
        // 文化制度状态是个 static 字典，只有读档路径（DataManager.LoadAll）会重置它。
        // 新建地图走的是 MapBox.clear_data → 这里，不清的话上一局的"已掌握制度/接触度/
        // 手动文明等级"会整份带进新世界。
        InstitutionSystem.ResetWorldState();
        ExtensionBase.Clear<Actor, ActorExtraData>();
        ExtensionBase.Clear<Family, FamilyExtraData>();
        ExtensionBase.Clear<War, WarExtraData>();
        ExtensionBase.Clear<Kingdom, KingdomExtraData>();
        ExtensionBase.Clear<City, CityExtraData>();
        ExtensionBase.Clear<Religion, ReligionExtraData>();
    }
}
