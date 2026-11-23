using System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 质子(群友 2026-11-22 "某国闻大军压境，惊惧不已……朝贡/附庸/送质子，质子为继承人，居住在都城")：
//   · 兵临城下称臣的(见 WarMuster)、打输了被迫称臣纳贡或收作附庸的(见 WarGoalSystem 和约)，把储君送到宗主都城为质；
//     没立储的送君主最年长的儿女，膝下无人就不送；
//   · 质子在一天，这一国不求独立(FeudalVassalService.CanSeekIndependence)、不自行退出朝贡(KingdomBehCheckEmpire)；
//   · 质子回国即位：久居上国，在位头 AmityYears 年照旧不求独立、不退出朝贡；
//   · 质子身故、宗属关系没了(宗主亡国、被放、改换门庭)：质约作废，不再补送——质子只管得住一代人。
//   质子只是记在这一国的数据里(谁、在谁那里、哪年送的)，人不搬家：纯净地图下人物本来就不在地图上，
//   搬了反倒牵动继承(储君要在本国才立得上)。
public static class HostageSystem
{
    public const int AmityYears = 10;

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    private static Actor Living(long id)
    {
        Actor actor = id >= 0 ? World.world?.units?.get(id) : null;
        return actor?.data != null && !actor.isRekt() && actor.isAlive() ? actor : null;
    }

    // 眼下的宗主：封建附庸的领主，或朝贡的那个帝国的中央
    private static Kingdom Lord(Kingdom subject) =>
        FeudalVassalService.GetOverlord(subject) ?? subject.GetTakenAllianceEmpire()?.CoreKingdom;

    // 在别国为质的这个人是谁的质子(界面、个人提示用)；不是为 null
    public static Actor HostageOf(Kingdom subject) =>
        Alive(subject) ? Living(subject.GetOrCreate().hostage_actor_id) : null;

    // 质约还管着这一国：质子在宗主那里，或质子回国即位不满 AmityYears 年
    public static bool Binds(Kingdom subject)
    {
        if (!Alive(subject) || World.world == null) return false;
        var data = subject.GetOrCreate();
        if (data.hostage_holder_id < 0L || Lord(subject)?.id != data.hostage_holder_id) return false;
        Actor hostage = Living(data.hostage_actor_id);
        if (hostage != null && hostage != subject.king) return true;
        double crowned = data.hostage_crowned_at;
        return crowned >= 0d && World.world.getCurWorldTime() >= crowned && Date.getYearsSince(crowned) < AmityYears;
    }

    // 称臣时送质子；送成了返回 true
    public static bool Send(Kingdom lord, Kingdom subject)
    {
        if (!Alive(lord) || !Alive(subject) || lord == subject || World.world == null) return false;
        var data = subject.GetOrCreate();
        if (Living(data.hostage_actor_id) != null && data.hostage_holder_id == lord.id) return false;
        Actor king = subject.king;
        Actor hostage = subject.HasHeir() ? subject.GetHeir() : null;
        if (hostage?.data == null || hostage.isRekt() || !hostage.isAlive() || hostage == king)
            hostage = king?.getChildren(false)?.Where(child => child?.data != null && !child.isRekt() && child.isAlive() && child != king)
                .OrderByDescending(child => child.getAge()).FirstOrDefault();
        if (hostage == null) return false;
        data.hostage_actor_id = hostage.id;
        data.hostage_holder_id = lord.id;
        data.hostage_since = World.world.getCurWorldTime();
        data.hostage_crowned_at = -1d;
        string lordName = lord.GetEmpire()?.GetEmpireName() ?? lord.GetKingdomName();
        string seat = lord.capital?.GetCityName() ?? lordName;
        string text = string.Format(LM.Get("hostage_sent_history"), subject.GetKingdomName(), hostage.getName(), lordName, seat);
        EventRecorder.Record(lord, text);
        hostage.RecordPersonalHistory(string.Format(LM.Get("personal_history_hostage_sent"), lordName, seat));
        LogService.LogInfo($"[EmpireCraft][质子] {subject.GetKingdomName()} 送 {hostage.getName()} 入质 {lordName}");
        return true;
    }

    private static void Clear(KingdomExtension.KingdomExtraData data)
    {
        data.hostage_actor_id = -1L;
        data.hostage_holder_id = -1L;
        data.hostage_since = -1d;
        data.hostage_crowned_at = -1d;
    }

    // 每年(见 RoyalHouseSystem.Tick)：质子即位、身故、宗属关系没了
    public static void Yearly()
    {
        if (World.world?.kingdoms == null) return;
        foreach (Kingdom subject in World.world.kingdoms.list.ToList())
        {
            if (!Alive(subject)) continue;
            var data = subject.GetOrCreate();
            if (data.hostage_holder_id < 0L) continue;
            try
            {
                Kingdom lord = Lord(subject);
                Actor hostage = Living(data.hostage_actor_id);
                if (lord == null || lord.id != data.hostage_holder_id)
                {
                    // 宗属关系没了：放归
                    hostage?.RecordPersonalHistory(LM.Get("personal_history_hostage_released"));
                    Clear(data);
                    continue;
                }
                if (data.hostage_crowned_at >= 0d)
                {
                    if (World.world.getCurWorldTime() < data.hostage_crowned_at || Date.getYearsSince(data.hostage_crowned_at) >= AmityYears)
                        Clear(data);
                    continue;
                }
                if (hostage == null)
                {
                    Clear(data);
                    continue;
                }
                if (hostage != subject.king) continue;
                // 质子回国即位
                data.hostage_crowned_at = World.world.getCurWorldTime();
                string lordName = lord.GetEmpire()?.GetEmpireName() ?? lord.GetKingdomName();
                EventRecorder.Record(lord, string.Format(LM.Get("hostage_crowned_history"), hostage.getName(), lordName, subject.GetKingdomName()));
                hostage.RecordPersonalHistory(string.Format(LM.Get("personal_history_hostage_crowned"), lordName));
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][质子] {subject.GetKingdomName()} 年度结算失败: {exception.Message}");
            }
        }
    }
}
