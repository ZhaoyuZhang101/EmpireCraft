using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 哈里发国只奉伊斯兰(用户 2026-11-04 转群友建议："哈里发国只会信仰伊斯兰教派，如果称帝为哈里发时全国境内没有一个伊斯兰教派的话
// 就创建一个新的，并且哈里发国的本国信仰吸引力更高"，问答全选推荐)。以前称帝、改行阿拉伯政体都不查宗教，奉别教的只是没有册封与天课，称号照旧：
//   · 哈里发国：阿拉伯政体的帝国，风俗文化属阿拉伯一线、不是法教传统(印度称帝国，不在此列)，不是共和；
//   · 定教：称帝(EmpireFormationService.CompleteFormation)、改行阿拉伯政体(EmirateSystem.OnRegimeChanged)时当场，
//     此外每年补查一次(旧档、核心易主、另立哈里发都由它兜住)。国教不是伊斯兰的，依次改奉境内信众最多的伊斯兰教派 → 本天下的伊斯兰
//     (一个天下一个正教，天下别处有就不另立，见 ReligionSphere) → 都没有才在都城立教。立教时世界上还没有伊斯兰的从名库取名，
//     否则以都城命名；本天下有别教的正教时记它为母宗教(不记就成了地方教会，没有哈里发座，还会被并掉)。
//     改奉走君主皈依(ReligionConversionSystem.Convert)：君主、国家、都城当场改，其余 40 年内按国教势能加倍陆续改；
//   · 不许改走：君主皈依、入主从俗、异文明复归、宗教融入诉求、圣战改宗都只放行伊斯兰(MayAdopt)，改奉别的伊斯兰教派不拦；
//     伊斯兰也不并进别教的正教(见 ReligionSphere.YearlyPass)；
//   · 吸引力：中央与奉同教的成员，境内的国教势能 ×AttractionFactor(见 BackgroundReligionSystem.StateFactor)；
//     有小人时没有势能，常年每年 ManualCityChance 逐城改宗(见 ReligionConversionSystem.ManualCityConversion)。只管境内，不向外加；
//   · 世界地图剧本开局就奉别教的古帝国(那时还没有伊斯兰)记 pre_caliphate_faith，不强改，也不称哈里发国、哈里发(用户同日选"改成帝国")：
//     称号文本键加 PreIslamicSuffix(见 DynastyNaming.CultureVariant)；日后自己皈依了伊斯兰就去掉这个记号，改回哈里发国。
public static class CaliphateFaith
{
    public const float AttractionFactor = 1.5f, ManualCityChance = 0.02f;
    public const string PreIslamicSuffix = "_preislamic";

    // 奉伊斯兰之前的古帝国的中央(成员不算：称号只有中央有)
    public static bool PreIslamic(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire?.data?.pre_caliphate_faith == true && empire.CoreKingdom == kingdom;
    }

    public static bool IsCaliphate(Empire empire) =>
        empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom?.data != null &&
        !empire.CoreKingdom.isRekt() && EmirateSystem.Applies(empire) && PapacySystem.IsCaliphal(empire) &&
        !RepublicSystem.IsRepublic(empire);

    // 受"只奉伊斯兰"约束的：哈里发国，剧本开局就奉别教的古帝国除外
    public static bool Bound(Empire empire) => IsCaliphate(empire) && !empire.data.pre_caliphate_faith;

    // 这个国家能不能改奉 faith：哈里发国的中央只能改奉伊斯兰，成员与别国不管
    public static bool MayAdopt(Kingdom realm, Religion faith)
    {
        Empire empire = realm?.GetEmpire();
        return empire == null || empire.CoreKingdom != realm || !Bound(empire) || ReligionFaith.IsCaliphal(faith);
    }

    // 国教吸引力更高的：哈里发国的中央，与奉同教的成员
    public static bool Attracts(Kingdom kingdom)
    {
        if (kingdom?.data == null || !kingdom.hasReligion() || !ReligionFaith.IsCaliphal(kingdom.religion)) return false;
        Empire empire = kingdom.GetEmpire();
        return IsCaliphate(empire) && PapacySystem.ReligionOf(empire) == kingdom.religion;
    }

    public static float StateFactor(Kingdom kingdom) => Attracts(kingdom) ? AttractionFactor : 1f;

    // 世界地图开局：阿拉伯一线的帝国奉着别教的记下来(见 WorldMapReligion.Apply)；法教传统不算，它们有自己的称谓(大君)
    public static void MarkScenarioFaith(Empire empire, Religion religion)
    {
        if (empire?.data == null || ReligionFaith.IsCaliphal(religion)) return;
        string culture = CultureService.GetRealmCulture(empire.CoreKingdom);
        if (InstitutionSystem.GetCultureLine(culture) == "Arabic" && !culture.HasPoliticalTrait(PapacySystem.DharmicTrait))
            SetPreIslamic(empire, true);
    }

    // 帝号的类别键是存着的(见 Empire.GetBaseEmpireFullName)，改记号时清掉，下次取国号时按新记号重取
    private static void SetPreIslamic(Empire empire, bool value)
    {
        empire.data.pre_caliphate_faith = value;
        empire.data.empire_type_key = "";
    }

    // 每年一次(挂在 ReligiousTension 的年度扫描上，排在统治者皈依前面)
    public static void YearlyPass()
    {
        if (ModClass.IS_CLEAR || ModClass.EMPIRE_MANAGER == null) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null) continue;
            if (empire.data.pre_caliphate_faith && ReligionFaith.IsCaliphal(empire.CoreKingdom?.religion))
                SetPreIslamic(empire, false);
            TrySettle(empire);
        }
    }

    // 这里出错不许打断称帝、改制(下一年还会补查)
    public static void TrySettle(Empire empire)
    {
        try
        {
            Settle(empire);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][宗教] {empire?.data?.name} 哈里发国定教失败: {exception.Message}");
        }
    }

    private static void Settle(Empire empire)
    {
        if (!Bound(empire)) return;
        Kingdom core = empire.CoreKingdom;
        if (AncientWarfareCompatibility.Owns(core)) return;
        Religion current = core.hasReligion() ? core.religion : null;
        if (ReligionFaith.IsCaliphal(current))
        {
            // 帝国国教还记着旧教时跟上：宗教同化别把成员推回去
            if (empire.Religion != null && empire.Religion != current) empire.Religion = current;
            return;
        }
        Actor ruler = core.king;
        City capital = core.capital;
        // 君主、都城一时空缺：下一年再补
        if (ruler?.data == null || !ruler.isAlive() || capital?.data == null || capital.isRekt()) return;
        string sphere = ReligionSphere.SphereOfCulture(CultureService.GetRealmCulture(core));
        Religion faith = LargestInRealm(empire) ?? InSphere(sphere, capital);
        string key = "caliph_adopts_faith_log";
        if (faith == null)
        {
            faith = Found(ruler, capital, sphere);
            key = "caliph_founds_sect_log";
        }
        if (faith == null) return;
        if (PapacySystem.HolyCity(faith) == null) faith.SetCity(capital);
        ReligionConversionSystem.Convert(core, faith, current, key);
        // 都城原是旧教的圣地：圣地迁到还奉它的城，不留在改了宗的都城
        if (current != null && current.GetCity() == capital) ReligionSphere.RelocateHolyCity(current, capital);
    }

    // 境内奉的伊斯兰里信众最多的(成员国的国教算境内有，城还没改过来也算)
    private static Religion LargestInRealm(Empire empire)
    {
        var people = new Dictionary<Religion, float>();
        foreach (City city in empire.getCities())
            if (city?.data != null && !city.isRekt() && city.hasReligion() && ReligionFaith.IsCaliphal(city.religion))
                Add(people, city.religion, Mathf.Max(1, city.CountLivingPopulation()));
        foreach (Kingdom member in empire.kingdoms_list)
            if (member?.data != null && !member.isRekt() && member.hasReligion() && ReligionFaith.IsCaliphal(member.religion))
                Add(people, member.religion, 1f);
        return Top(people);
    }

    // 本天下的伊斯兰：正教是伊斯兰就是它，否则取信众最多的；都没人奉了取本天下立过的，在都城复兴(同 NativeFaithSystem.Dormant，不另立)
    private static Religion InSphere(string sphere, City capital)
    {
        if (sphere.Length == 0) return null;
        Religion orthodox = ReligionSphere.OrthodoxOf(sphere);
        if (ReligionFaith.IsCaliphal(orthodox)) return orthodox;
        var people = new Dictionary<Religion, float>();
        foreach (City city in World.world.cities.list)
            if (city?.data != null && !city.isRekt() && city.hasReligion() && ReligionFaith.IsCaliphal(city.religion) &&
                ReligionSphere.SphereOf(city.religion) == sphere)
                Add(people, city.religion, Mathf.Max(1, city.CountLivingPopulation()));
        Religion followed = Top(people);
        if (followed != null) return followed;
        foreach (Religion religion in World.world.religions.list)
        {
            if (religion?.data == null || religion.isRekt() || !ReligionFaith.IsCaliphal(religion) ||
                ReligionSphere.SphereOf(religion) != sphere) continue;
            religion.SetCity(capital);
            return religion;
        }
        return null;
    }

    // 在都城立教：世界上还没有伊斯兰的从名库取名，否则以都城命名；本天下有别教的正教时记它为母宗教
    private static Religion Found(Actor ruler, City capital, string sphere)
    {
        bool first = !World.world.religions.list.Any(religion => religion?.data != null && !religion.isRekt() && ReligionFaith.IsCaliphal(religion));
        Religion parent = ReligionSphere.OrthodoxOf(sphere);
        if (ruler.city != capital) ruler.joinCity(capital);
        Religion sect = World.world.religions.newReligion(ruler, false);
        if (sect?.data == null) return null;
        sect.data.name = (first ? ReligionFaith.PickName(FaithType.Islam) : null) ??
                         string.Format(LM.Get("caliph_sect_name"), capital.GetCityName());
        sect.SetCity(capital);
        ReligionExtension.ReligionExtraData data = sect.GetOrCreate();
        data.Faith = FaithType.Islam.ToString();
        if (sphere.Length > 0) data.Sphere = sphere;
        if (parent?.data != null && parent != sect)
        {
            data.ParentReligionId = parent.data.id;
            data.SplitAt = World.world.getCurWorldTime();
        }
        // 创教时(ReligionPatch)记下的那条用的是改名前的名字，换成真名
        ReligionChronicle.Clear(sect);
        string text = string.Format(LM.Get("religion_founded_log"), ruler.getName(), capital.GetCityName(), sect.name);
        if (parent?.data != null && parent != sect) ReligionChronicle.Record(parent, sect, ReligionChronicle.KindSchism, text);
        else ReligionChronicle.Record(sect, ReligionChronicle.KindFounding, text);
        LogService.LogInfo($"[EmpireCraft][宗教] {capital.GetCityName()} 立{sect.name}(母宗教 {parent?.name ?? "无"})");
        return sect;
    }

    private static void Add(Dictionary<Religion, float> people, Religion religion, float count)
    {
        people.TryGetValue(religion, out float sum);
        people[religion] = sum + count;
    }

    private static Religion Top(Dictionary<Religion, float> people) =>
        people.Count == 0 ? null : people.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.data.id).First().Key;
}
