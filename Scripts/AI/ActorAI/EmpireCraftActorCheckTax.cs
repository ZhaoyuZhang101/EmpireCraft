using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckTax: GameAIActorBase
{
    public override Type OriginalBeh =>  typeof(BehActorGiveTax);

    public override BehResult execute(Actor pActor) => Submit(pActor);

    // 无小人模式由城市经济结算征收，保留人物不必先完成个人 AI 任务。
    public static void SettleVirtualCity(City city)
    {
        if (CityPopulationSystem.AbstractPopulationEnabled) SettleCity(city);
    }

    public static void SettleCity(City city)
    {
        if (!TreasurySystem.Enabled(city) || city.units == null) return;
        foreach (Actor actor in city.units)
            if (actor?.data != null && actor.isAlive() && !actor.isRekt() && actor.city == city)
                Submit(actor);
    }

    public static BehResult Submit(Actor pActor)
    {
        if (ModClass.IS_CLEAR || World.world == null) return BehResult.Continue;
        if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return BehResult.Continue;
        if (AncientWarfareCompatibility.OwnsObject(pActor)) return BehResult.Continue;
        if (!pActor.IsNeedToSubmitTax()) return BehResult.Continue;
        if (!pActor.hasCity()) return BehResult.Continue;
        if (!pActor.hasKingdom()) return BehResult.Continue;
        pActor.kingdom.CheckEmpire();
        var pTaxRate = pActor.kingdom.GetTaxRate();
        City city = pActor.getCity();
        if (city?.data == null || city.isRekt()) return BehResult.Continue;
        bool hadLoot = pActor.loot > 0;
        if (hadLoot)
        {
            //战利品
            int loot = pActor.loot;
            pActor.lootEmpty();
            if (pActor.kingdom.IsInEmpire())
            {
                Empire empire = pActor.kingdom.GetEmpire();
                loot += empire.data.additions.addition[OfficerPowerType.财政] / 2;
            }
            pActor.addMoney(loot);
        }
        // 背景人口按生产收入纳税；实体人物使用个人余额口径，各付自己的税。
        if (TreasurySystem.Enabled(city))
        {
            TreasuryCategory category = pActor.GetOrCreate().socialClass switch
            {
                SocialClass.Peasant or SocialClass.Landlord => TreasuryCategory.LandTax,
                SocialClass.Labour => TreasuryCategory.IndustrialTax,
                SocialClass.Merchant => TreasuryCategory.CommercialTax,
                _ => TreasuryCategory.ResidentTax
            };
            // 按上次纳税以来新增的钱(收入)纳税，不按存款余额：以前每年按全部余额征一次，
            // 全国税收九成以上来自官员攒下的存款(多是贪污来的)，跟人口与经济规模无关
            var extra = pActor.GetOrCreate();
            long income = extra.tax_money_baseline < 0 ? 0L : Math.Max(0L, Math.Max(0, pActor.money) - extra.tax_money_baseline);
            int tax = (int)Math.Min(int.MaxValue, income * SectorTaxRules.Rate(pTaxRate, category));
            if (tax > 0)
            {
                pActor.addMoney(-tax);
                TreasurySystem.CollectResidentTax(city, tax, category);
            }
            extra.tax_money_baseline = Math.Max(0, pActor.money);
        }
        else if (hadLoot)
        {
            // 不接入新财政的兼容对象保留原有战利品抽成。
            int num = (int)((float)pActor.money* pTaxRate);
            if (num <= 0)
            {
                num = 1;
            }
            pActor.addMoney(-num);
            city.AddMoney(num);
        }
        pActor.RecordTaxTime();
        return BehResult.Continue;
    }
}
