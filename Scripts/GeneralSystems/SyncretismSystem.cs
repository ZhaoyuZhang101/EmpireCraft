using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using static EmpireCraft.Scripts.GameClassExtensions.ReligionExtension;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 教义融合(用户 2026-11-06："宗教需要有自己的加成可以吸收其他宗教的特质"；问答选"按类型分槽位"，参数在 ReligionTenets.json)。
// 每年一次，挂在 ReligiousTension 的年度扫描上(地方教会并入之后、统治者皈依之前)，有无小人都结算：
//   · 接触渠道(每对"吸收方 A ← 来源 B"，每年取各渠道最大的一条，照制度吸收的口径)：
//       被统治 1.0：A 之城在奉 B 的国里，或 A 为国教的国里有 B 之城(两边都算)；
//       同国并存 0.6：一个政权(帝国按中央连同成员)境内两教的城都有；
//       相通 0.5 × 通达：两教之城相邻(原版邻城、只隔无人区的邻城)，按通达层的消息口径打折(来源一方的本事，见 BackgroundReligionSystem.Reach)；
//       朝贡、同盟 0.3；
//     另有皈依事件：本年君主皈依 B 的权重 ≥1 却没成，A 一次性 +10；A 国改奉 B 后，B 对 A 一次性 +20(普世宗教收本地圣徒)。
//   · 接触度每年 +10 × 渠道权重 × A 的开放度(满 100)，这一年没接触的每年 −2；满 40、接触满 10 年融一条，每教 20 年至多一条，融后这对清零。
//   · 槽位与开放度：民间 4/1.0、腾格里 3/1.0、三教 4/0.8、神道 3/0.9、法教 3/0.7、教会 1/0.3、伊斯兰 1/0.2、犹太 0；
//     奉它为国教的国家施行了宽容制度(三教并立、神佛习合等)的开放度 ×1.5、槽位 +1。
//   · 融哪条见 ReligionTenets.Candidates：普世宗教只融本地圣徒；排他的只融仪轨；教理、结构不外传。效果见 ReligionTenets。
//   · 史书、宗教大事记(融合类)记"某教吸收某教的某教义"；宗教窗口概况页有教义卡、融合进度卡。
public static class SyncretismSystem
{
    private static bool Alive(Religion religion) => religion?.data != null && !religion.isRekt();

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    public static void YearlyPass()
    {
        ReligionSyncretismConfig config = ReligionTenets.Syncretism;
        if (!config.enabled || World.world?.cities == null || World.world.religions == null || ModClass.IS_CLEAR) return;
        var contacts = new Dictionary<(Religion learner, Religion source), float>();

        void Link(Religion learner, Religion source, float weight)
        {
            if (weight <= 0f || !Alive(learner) || !Alive(source) || learner == source) return;
            if (!contacts.TryGetValue((learner, source), out float current) || weight > current) contacts[(learner, source)] = weight;
        }

        var realmFaiths = new Dictionary<Kingdom, HashSet<Religion>>();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.kingdom?.data == null || city.kingdom.wild) continue;
            Religion local = city.hasReligion() ? city.religion : null;
            Religion state = city.kingdom.hasReligion() ? city.kingdom.religion : null;
            if (local != null && state != null && local != state)
            {
                Link(local, state, config.ruled_weight);
                Link(state, local, config.ruled_weight);
            }
            Kingdom realm = Realm(city.kingdom);
            if (realm != null)
            {
                if (!realmFaiths.TryGetValue(realm, out HashSet<Religion> set)) realmFaiths[realm] = set = new HashSet<Religion>();
                if (local != null) set.Add(local);
            }
            if (local == null) continue;
            foreach (City other in BackgroundReligionSystem.Contacts(city))
            {
                if (other?.data == null || other.isRekt() || !other.hasReligion() || other.religion == local) continue;
                float reach = BackgroundReligionSystem.Reach(other, city, other.religion, ReligionSphere.SphereOfCity(city));
                if (reach > 0f) Link(local, other.religion, config.neighbour_weight * reach);
            }
        }
        foreach (HashSet<Religion> set in realmFaiths.Values)
        {
            if (set.Count < 2) continue;
            foreach (Religion a in set)
            foreach (Religion b in set)
                if (a != b) Link(a, b, config.same_realm_weight);
        }
        LinkTributaries(Link, config.tributary_weight);
        Accumulate(contacts, config);
    }

    // 朝贡、岁币之国与宗主，同盟各国
    private static void LinkTributaries(Action<Religion, Religion, float> link, float weight)
    {
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            Kingdom core = empire?.CoreKingdom;
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || core?.data == null || !core.hasReligion()) continue;
            foreach (long id in (empire.data.taken_Kingdoms ?? new List<long>()).Concat(empire.data.given_Kingdoms ?? new List<long>()))
            {
                Kingdom other = World.world.kingdoms.get(id);
                if (other?.data == null || other.isRekt() || !other.hasReligion()) continue;
                link(core.religion, other.religion, weight);
                link(other.religion, core.religion, weight);
            }
        }
        var alliances = new Dictionary<Alliance, List<Religion>>();
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || !kingdom.hasReligion() || !kingdom.hasAlliance()) continue;
            Alliance alliance = kingdom.getAlliance();
            if (alliance == null) continue;
            if (!alliances.TryGetValue(alliance, out List<Religion> list)) alliances[alliance] = list = new List<Religion>();
            list.Add(kingdom.religion);
        }
        foreach (List<Religion> list in alliances.Values)
            for (int i = 0; i < list.Count; i++)
            for (int j = 0; j < list.Count; j++)
                if (i != j) link(list[i], list[j], weight);
    }

    private static void Accumulate(Dictionary<(Religion learner, Religion source), float> contacts, ReligionSyncretismConfig config)
    {
        var tolerant = new Dictionary<Religion, bool>();
        bool Tolerant(Religion religion)
        {
            if (!tolerant.TryGetValue(religion, out bool value)) tolerant[religion] = value = ReligionTenets.Tolerant(religion);
            return value;
        }

        var touched = new Dictionary<Religion, HashSet<long>>();
        foreach (KeyValuePair<(Religion learner, Religion source), float> pair in contacts)
        {
            Religion learner = pair.Key.learner, source = pair.Key.source;
            bool open = Tolerant(learner);
            if (ReligionTenets.Absorbed(learner).Count >= ReligionTenets.Slots(learner, open)) continue;
            if (ReligionTenets.Candidates(learner, source).Count == 0) continue;
            float gain = config.exposure_per_year * pair.Value * ReligionTenets.Openness(learner, open);
            if (gain <= 0f) continue;
            ReligionExtension.ReligionExtraData data = learner.GetOrCreate();
            data.Contact ??= new Dictionary<long, float>();
            data.ContactYears ??= new Dictionary<long, int>();
            long id = source.data.id;
            data.Contact.TryGetValue(id, out float current);
            data.Contact[id] = Mathf.Min(config.max_exposure, current + gain);
            data.ContactYears.TryGetValue(id, out int years);
            data.ContactYears[id] = years + 1;
            if (!touched.TryGetValue(learner, out HashSet<long> set)) touched[learner] = set = new HashSet<long>();
            set.Add(id);
        }
        foreach (Religion religion in World.world.religions.list.ToList())
        {
            if (!Alive(religion)) continue;
            ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
            if (data.Contact == null || data.Contact.Count == 0) continue;
            data.ContactYears ??= new Dictionary<long, int>();
            touched.TryGetValue(religion, out HashSet<long> hit);
            foreach (long id in data.Contact.Keys.ToList())
            {
                if (!Alive(World.world.religions.get(id)))
                {
                    Forget(data, id);
                    continue;
                }
                if (hit != null && hit.Contains(id)) continue;
                float value = data.Contact[id] - config.decay_per_year;
                if (value <= 0f) Forget(data, id);
                else data.Contact[id] = value;
            }
            if (data.Contact.Count > config.max_sources)
                foreach (long id in data.Contact.OrderBy(pair => pair.Value).Take(data.Contact.Count - config.max_sources).Select(pair => pair.Key)
                             .ToList())
                    Forget(data, id);
            TryAbsorb(religion, Tolerant(religion), config);
        }
    }

    private static void Forget(ReligionExtension.ReligionExtraData data, long id)
    {
        data.Contact.Remove(id);
        data.ContactYears?.Remove(id);
    }

    private static void TryAbsorb(Religion religion, bool tolerant, ReligionSyncretismConfig config)
    {
        ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
        double now = World.world.getCurWorldTime();
        if (data.LastSyncretismAt >= 0d && now >= data.LastSyncretismAt && Date.getYearsSince(data.LastSyncretismAt) < config.cooldown_years)
            return;
        if (ReligionTenets.Absorbed(religion).Count >= ReligionTenets.Slots(religion, tolerant)) return;
        foreach (KeyValuePair<long, float> pair in data.Contact.OrderByDescending(pair => pair.Value).ToList())
        {
            if (pair.Value < config.min_exposure) break;
            data.ContactYears.TryGetValue(pair.Key, out int years);
            if (years < config.min_years) continue;
            Religion source = World.world.religions.get(pair.Key);
            if (!Alive(source)) continue;
            List<ReligionTenetDef> candidates = ReligionTenets.Candidates(religion, source);
            if (candidates.Count == 0) continue;
            Absorb(religion, source, candidates[UnityEngine.Random.Range(0, candidates.Count)], now);
            return;
        }
    }

    private static void Absorb(Religion religion, Religion source, ReligionTenetDef def, double now)
    {
        ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
        string sphere = def.kind == ReligionTenets.KindSaint ? ReligionSphere.SphereOf(source) : "";
        ReligionTenets.Absorbed(religion).Add(new SyncreticTenetData
        {
            tenet = def.id, from_id = source.data.id, from_root = ReligionTenets.RootId(source), from_name = source.name, sphere = sphere,
            at = now
        });
        Forget(data, source.data.id);
        data.LastSyncretismAt = now;
        ReligionTenets.Invalidate();
        string text = string.Format(LM.Get("syncretism_log"), religion.name, source.name, TenetName(def.id, sphere));
        TranslateHelper.LogEventMessage(text, PapacySystem.HolyCity(religion)?.kingdom);
        ReligionChronicle.Record(religion, source, ReligionChronicle.KindSyncretism, text);
        ReligionDiagnostics.Add(ReligionDiagnostics.Syncretism);
        LogService.LogInfo($"[EmpireCraft][宗教] {text}");
    }

    // 教义名：本地圣徒带上来源的天下
    public static string TenetName(string id, string sphere)
    {
        string name = LM.Get("tenet_" + id);
        return id == ReligionTenets.LocalSaints && !string.IsNullOrEmpty(sphere) ? string.Format(name, SphereLabel(sphere)) : name;
    }

    // 天下的名字：语言里有 religion_sphere_<标识> 就用，没有就取同天下一个文化的名字
    public static string SphereLabel(string sphere)
    {
        if (string.IsNullOrEmpty(sphere)) return "—";
        string key = "religion_sphere_" + sphere;
        string text = LM.Get(key);
        if (!string.IsNullOrEmpty(text) && text != key) return text;
        string culture = OnomasticsRule.ALL_CULTURE_RULE.Keys.FirstOrDefault(name => name.GetTianxiaId() == sphere);
        return culture != null ? culture.GetCultureTranslate() : sphere;
    }

    #region 皈依事件

    // 君主皈依 target 的权重 ≥1 却没成：current 取其仪轨
    public static void NoteFailedConversion(Religion current, Religion target) =>
        Bump(current, target, ReligionTenets.Syncretism.failed_conversion);

    // 改奉之后：新教收旧教的本地圣徒、节庆
    public static void NoteConversion(Religion old, Religion faith) => Bump(faith, old, ReligionTenets.Syncretism.after_conversion);

    private static void Bump(Religion learner, Religion source, float amount)
    {
        ReligionSyncretismConfig config = ReligionTenets.Syncretism;
        if (!config.enabled || amount <= 0f || !Alive(learner) || !Alive(source) || learner == source) return;
        if (ReligionTenets.Candidates(learner, source).Count == 0) return;
        ReligionExtension.ReligionExtraData data = learner.GetOrCreate();
        data.Contact ??= new Dictionary<long, float>();
        data.ContactYears ??= new Dictionary<long, int>();
        data.Contact.TryGetValue(source.data.id, out float current);
        data.Contact[source.data.id] = Mathf.Min(config.max_exposure, current + amount);
    }

    #endregion

    // 融合进度(宗教窗口)：接触度最高的几个来源
    public static List<(Religion source, float exposure, int years)> Progress(Religion religion, int count)
    {
        var list = new List<(Religion, float, int)>();
        if (!Alive(religion)) return list;
        ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
        if (data.Contact == null) return list;
        foreach (KeyValuePair<long, float> pair in data.Contact.OrderByDescending(pair => pair.Value))
        {
            Religion source = World.world.religions.get(pair.Key);
            if (!Alive(source)) continue;
            int years = data.ContactYears != null && data.ContactYears.TryGetValue(pair.Key, out int value) ? value : 0;
            list.Add((source, pair.Value, years));
            if (list.Count >= count) break;
        }
        return list;
    }
}
