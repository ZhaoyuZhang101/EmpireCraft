using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 教宗正统(用户 2026-10-27 选"单独的教宗正统")：教宗本人的正统(0~100)，只有普世教会的教宗有，界面、提示框随时现算：
//   基数 Base + 资质(同帝国正统的君主一项：贤明 +14、中主 +10、庸弱 +3、昏聩荒淫 −5、暴虐 −8)
//   + 选立(枢机推举 +Conclave、世俗强立 Imposed，旧档未记 0) + 在位(每 TenureYearsPerPoint 年 +1，至多 TenureCap)
//   + 圣战胜负(每胜 +CrusadePoint、每败 −CrusadePoint，至多 ±CrusadeCap) + 教廷之囚 Captivity + 对立教宗 Antipope；
//   效果：教权目标加 (正统 − 50)/5(见 PapacySystem.UpdateReligion)；正统低于 AntipopeBelow 每年 AntipopeChance 出对立教宗，
//   教廷脱困、不计对立教宗那一项回到 HealedAt 以上才弥合(见 PapacySystem.UpdateCaptivity)；低于 CoronationBelow 各帝国不赴其加冕；
//   号召圣战的响应机会 ±(正统 − 50)×RallyPerPoint(见 PapacySystem.Rally)。哈里发是帝国君主，看帝国正统，不另设。
public static class PopeLegitimacy
{
    public const int Base = 50, Conclave = 10, Imposed = -15, TenureYearsPerPoint = 4, TenureCap = 5;
    public const int CrusadePoint = 2, CrusadeCap = 6, Captivity = -15, Antipope = -15;
    public const int AntipopeBelow = 40, HealedAt = 50, CoronationBelow = 40;
    public const float AntipopeChance = 0.05f, RallyPerPoint = 0.004f;

    public static bool Applies(Religion religion) => ReligionFaith.IsPapal(religion) && PapacySystem.Pope(religion) != null;

    public static List<(string key, int value)> Breakdown(Religion religion, bool countSchism = true)
    {
        var items = new List<(string key, int value)>();
        Actor pope = PapacySystem.Pope(religion);
        if (pope == null || !ReligionFaith.IsPapal(religion)) return items;
        ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
        items.Add(("pope_legit_base", Base));
        items.Add(RulerQualitySystem.LegitimacyItem(pope));
        int chosen = data.LeaderID == pope.id ? data.PopeChosenBy : 0;
        if (chosen == PapalElection.ByConclave) items.Add(("pope_legit_conclave", Conclave));
        else if (chosen == PapalElection.ByImposition) items.Add(("pope_legit_imposed", Imposed));
        Kingdom see = PapacySystem.Papacy(religion);
        double since = see?.data?.timestamp_king_rule ?? -1d;
        int tenure = since >= 0d ? Mathf.Min(TenureCap, Date.getYearsSince(since) / TenureYearsPerPoint) : 0;
        if (tenure > 0) items.Add(("pope_legit_tenure", tenure));
        int active = PapacySystem.ActiveCrusade(religion) != null ? 1 : 0;
        int wins = Mathf.Max(0, data.CrusadeCount - data.CrusadeLosses - active);
        int crusades = Mathf.Clamp((wins - data.CrusadeLosses) * CrusadePoint, -CrusadeCap, CrusadeCap);
        if (crusades != 0) items.Add(("pope_legit_crusades", crusades));
        if (PapacySystem.IsCaptive(religion)) items.Add(("pope_legit_captive", Captivity));
        if (countSchism && data.Schism) items.Add(("pope_legit_antipope", Antipope));
        return items;
    }

    public static int Get(Religion religion, bool countSchism = true)
    {
        List<(string key, int value)> items = Breakdown(religion, countSchism);
        return items.Count == 0 ? Base : Mathf.Clamp(items.Sum(item => item.value), 0, 100);
    }
}
