using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NCMS.Extensions;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Profiling.LowLevel;

namespace EmpireCraft.Scripts.HelperFunc;
public static class ExamSystem
{
    public enum ExamType
    {
        City, //乡试
        Province, //会试
        Empire, //殿试
        Office //官员晋级
    }
    public static void startExam(ExamType type, NanoObject nano)
    {
        switch (type) 
        {
            case ExamType.City:
                cityExamPrepare(nano);
                break;
            case ExamType.Province:
                provinceExamPrepare(nano);
                break;
            case ExamType.Empire:
                empireExamPrepare(nano);
                break;
            default:
                break;
        }
    }
    public static void cityExamPrepare(NanoObject nano)
    {
        City city = (City)nano;
        Dictionary<Actor, double> MarksData = new Dictionary<Actor, double>();
        foreach(Actor actor in city.units)
        {
            double mark = 0;
            if (!actor.isAdult()) continue;
            if (actor.isOfficer()) continue;
            if (actor.isCityLeader()) continue;
            if (actor.isKing()) continue;
            if (actor.hasArmy()) continue;
            if (actor.intelligence < 15) continue;
            if (actor.hasTrait("jingshi")) continue;
            if (actor.hasTrait("juren")) continue;
            if (actor.hasTrait("gongshi")) continue;
            if (city.kingdom.GetEmpire().data.is_allow_normal_to_exam)
            { 
                mark = actor.startCityExam();
            } else
            {
                if (actor.hasClan())
                {
                    mark = actor.startCityExam();
                }
            }
            if (mark > 0)
            {
                MarksData.Add(actor, mark);
            }
        }
        var sorted = MarksData.OrderByDescending(kv=>kv.Value).ToList();
        //LogService.LogInfo($"参加{city.data.name}乡试有{sorted.Count}人");
        int takeNum = 3;
        if (city.GetExamPassPersonIDs().Count()< takeNum)
        {
            takeNum -= city.GetExamPassPersonIDs().Count();
        } else
        {
            takeNum = 1;
        }
        if (sorted.Count()> takeNum)
        {
            sorted.Take(takeNum).ForEach(item=>item.Key.addTrait("juren"));
            //LogService.LogInfo($"{takeNum}人中举");
        } else
        {
            sorted.ForEach(item => item.Key.addTrait("juren"));
            //LogService.LogInfo($"{sorted.Count}人中举");
        }
    }
    public static void provinceExamPrepare(NanoObject nano)
    {
        Province province = (Province)nano;
        Dictionary<Actor, double> MarksData = new Dictionary<Actor, double>();
        foreach(City city in province.city_list)
        {
            foreach (Actor actor in city.units.FindAll(a => a.hasTrait("juren")))
            {
                double mark = actor.startProvinceExam();
                MarksData.Add(actor, mark);
            }
        }
        var sorted = MarksData.OrderByDescending(kv => kv.Value).ToList();
        //LogService.LogInfo($"参加{province.data.name}会试有{sorted.Count}人");
        int takeNum = 1;
        if (sorted.Count() > takeNum)
        {
            sorted.Take(takeNum).ForEach(item => item.Key.addTrait("gongshi"));
            //LogService.LogInfo($"{takeNum}人成为贡士");
        }
        else
        {
            sorted.ForEach(item => item.Key.addTrait("gongshi"));
            //LogService.LogInfo($"{sorted.Count()}人成为贡士");
        }

    }

    public static void empireExamPrepare(NanoObject nano)
    {
        Empire empire = (Empire)nano;
        Dictionary<Actor, double> MarksData = new Dictionary<Actor, double>();
        foreach (Province province in empire.ProvinceList) 
        {
            if (province.IsTotalVassaled()) continue;
            foreach (Actor actor in province.allGongshi())
            {
                if (!MarksData.TryGetValue(actor, out double m))
                {
                    double mark = actor.startEmpireExam();
                    MarksData.Add(actor, mark);
                }
            }
        }
        var sorted = MarksData.OrderByDescending(kv => kv.Value).ToList();
        //LogService.LogInfo($"参加{empire.data.name}殿试有{sorted.Count}人");
        int takeNum = 1;
        if (sorted.Count() > takeNum)
        {
            sorted.Take(takeNum).ForEach(item => { 
                item.Key.addTrait("jingshi");

            });
        }
        else
        {
            sorted.ForEach(item => {
                item.Key.addTrait("jingshi");
            });
            //LogService.LogInfo($"{sorted.Count()}人成为进士");
        }
    }

    public static double startCityExam(this Actor actor) 
    {
        double mark = 0;
        //乡试
        ActorAbility ability = new ActorAbility(actor);
        mark = ability.intelligence;
        return mark;
    }

    public static double startProvinceExam(this Actor actor) 
    {
        double mark = 0;
        //会试
        ActorAbility ability = new ActorAbility(actor);
        mark = ability.intelligence + ability.diplomacy;
        return mark;
    }

    public static double startEmpireExam(this Actor actor) 
    {
        double mark = 0;
        //殿试
        ActorAbility ability = new ActorAbility(actor);
        mark = ability.intelligence + ability.diplomacy + ability.stewardship;
        return mark;
    }
}

public class ActorAbility
{
    public double intelligence;
    public double stewardship;
    public double diplomacy;
    public ActorAbility(Actor a)
    {
        diplomacy = a.stats["diplomacy"];
        stewardship = a.stats["stewardship"];
        intelligence = a.stats["intelligence"];
        //double warfare = a.stats["warfare"];
        //double mana = a.stats["mana"];
        //double stamina = a.stats["stamina"];
        //double skill_spell = a.stats["skill_spell"];
        //double skill_combat = a.stats["skill_combat"];
        //double range = a.stats["range"];
        //double damage = a.stats["damage"];
        //double scale = a.stats["scale"];
        //double offspring = a.stats["offspring"];
        //double mass = a.stats["mass"];
        //double throwing_range = a.stats["throwing_range"];
        //double lifespan = a.stats["lifespan"];
        //double mass_2 = a.stats["mass_2"];
        //double armor = a.stats["armor"];
        //double attack_speed = a.stats["attack_speed"];
    }
}
