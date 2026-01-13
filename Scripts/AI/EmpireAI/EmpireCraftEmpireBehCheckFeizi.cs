using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NCMS.Extensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.EmpireAI;

public class EmpireCraftEmpireBehCheckFeizi: GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        if (!pKingdom.IsEmpire()) return BehResult.Stop;
        Empire empire = pKingdom.GetEmpire();
        if (!empire.HasEmperor()) return BehResult.Continue;
        var emperor = empire.Emperor;
        if (empire.IsNeedToChooseLovers())
        {
            var concubines = emperor.GetPersonalIdentity().concubines;
            var lives = concubines.Select(pValueTuple => SpecificClanManager.getPerson(pValueTuple.identity)).ToList()
                .FindAll(a => a.is_alive).Select(a=>a._actor).ToList();
            if (emperor.isAdult())
            {
                lives.ForEach(a =>
                {
                    Random rand = new Random();
                    var possibility = rand.NextDouble();
                    if (possibility < 0.3)
                    {
                        BabyMaker.makeBaby(emperor, a); 
                    }
                });
            }
            if (lives.Count < 3)
            {
                var lovers = empire.getUnits().ToList().FindAll(a =>
                    emperor.isSexMale() ? a.isSexFemale() : a.isSexMale() && a.isAdult() && a.age <= 25&&!a.hasLover()&&!lives.Contains(a)).Take(3-lives.Count).ToList();
                lovers.ForEach(a=>
                {
                    a.lover = emperor;
                    emperor.GetPersonalIdentity().setLover(a, isCus:true);
                    a.joinCity(emperor.city);
                });
            }
        }
        return base.execute(pKingdom);
    }
}