using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using static EmpireCraft.Scripts.GameClassExtensions.ReligionExtension;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗教的加成与教义(用户 2026-11-06："本土宗教，宗教需要有自己的加成可以吸收其他宗教的特质；不改的话就会出现在古典时代全世界都信基督"；
// 问答全按推荐：加成"中"、教义用模组自己的教义表、一神教两段式、融合按类型分槽位)。参数与教义表在模组根目录 ReligionTenets.json。
// 三层：
//   · 类型底色：传教力、惰性、皈依意愿三张表照旧(见 ReligionFaith)，这里乘上教义的系数；
//   · 本土加成(任何宗教在自己的天下，即宗教的天下 = 城的文化天下)：惰性 ×1.5(BackgroundReligionSystem)；
//     国教是本天下本土宗教、国家文化也在本天下的君主，对外天下宗教的皈依意愿 ×0.3(ReligionConversionSystem)；
//     奉本天下正教、治下过半同教正统 +3(MonarchyLegitimacy)；与国教同教的本天下宗教之城稳定 +2(CityStabilitySystem)；
//   · 教义：本有的按类型、天下现算(不存档，随 ReligionTenets.json 改)；融来的记在 ReligionExtraData.Syncretic(见 SyncretismSystem)，
//     按 absorbed_strength(六成)生效。普世宗教(教会、伊斯兰、法教)只融"本地圣徒"，只在来源的天下加惰性(守得住、扩不快)；
//     以融合御同化：A 每从 B 一系融一条，B 对 A 之城的势能、A 国君主皈依 B 的意愿 ×0.85，至多 ×0.55(见 Pull)。
// 效果的挂点：传教力、惰性(ReligionFaith)，本土惰性与本地圣徒(BackgroundReligionSystem.YearlyPass)，抗皈依(ReligionConversionSystem)，
//   御侮(NativeDefenseSystem.CombatFactor)，庙宇科研(TechnologySystem)，圣地势能(BackgroundReligionSystem.Pressure)，
//   同教稳定(CityStabilitySystem)，正统(MonarchyLegitimacy)，圣战攻城不改宗(CityPatch)。
public static class ReligionTenets
{
    public const string KindDoctrine = "doctrine", KindEthic = "ethic", KindPractice = "practice", KindOrganization = "organization",
        KindSaint = "saint", KindStructure = "structure";
    public const string LocalSaints = "local_saints";
    private const string FileName = "ReligionTenets.json";

    // 一个宗教的教义合计(本有足额、融来的按六成)
    public sealed class Effects
    {
        public float Mission, Inertia, HomeInertia, Resist, Defense, TempleResearch, HolyCity, Concord, Openness;
        public int Slots;
        public bool CaptureImmune;
        // 融来的条数、其中有没有"经典/教阶"(升格用)
        public int AbsorbedCount;
        public bool AbsorbedOrganizes;
        // 正统：(值, 治下同教至少几成)
        public readonly List<(int value, float share)> Legitimacy = new();
        // 本地圣徒：天下 → 惰性加成
        public readonly Dictionary<string, float> Saints = new(StringComparer.Ordinal);
    }

    private static readonly Effects Empty = new();
    private static ReligionTenetFile _file;
    private static Dictionary<string, ReligionTenetDef> _defs = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<string>> InnateCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<long, (int frame, int version, Effects effects)> EffectCache = new();
    private static readonly Dictionary<long, (double at, long faith, float share)> ShareCache = new();
    private static int _version;

    public static void ResetWorldState()
    {
        EffectCache.Clear();
        ShareCache.Clear();
        _version++;
    }

    // 教义变了(融到一条、分出的教会抄了母宗教的)：缓存作废
    public static void Invalidate() => _version++;

    public static ReligionTenetFile Config
    {
        get
        {
            EnsureLoaded();
            return _file;
        }
    }

    public static ReligionHomeConfig Home => Config.home;
    public static ReligionSyncretismConfig Syncretism => Config.syncretism;
    public static ReligionRevelationConfig Revelation => Config.revelation;

    private static void EnsureLoaded()
    {
        if (_file != null) return;
        var file = new ReligionTenetFile();
        try
        {
            string path = global::System.IO.Path.Combine(ModClass._declare.FolderPath, FileName);
            if (global::System.IO.File.Exists(path))
                file = Newtonsoft.Json.JsonConvert.DeserializeObject<ReligionTenetFile>(global::System.IO.File.ReadAllText(path),
                    new Newtonsoft.Json.JsonSerializerSettings { ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace }) ?? file;
            else LogService.LogWarning($"[EmpireCraft][宗教] 没有 {FileName}，教义为空，本土加成等用默认值");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][宗教] 读取 {FileName} 失败，用默认值: {exception.Message}");
            file = new ReligionTenetFile();
        }
        file.home ??= new ReligionHomeConfig();
        file.syncretism ??= new ReligionSyncretismConfig();
        file.revelation ??= new ReligionRevelationConfig();
        file.tenets ??= new List<ReligionTenetDef>();
        file.syncretism.slots ??= new Dictionary<string, int>();
        file.syncretism.openness ??= new Dictionary<string, float>();
        file.revelation.techs ??= new List<string>();
        file.revelation.calendar_year ??= new Dictionary<string, int>();
        var defs = new Dictionary<string, ReligionTenetDef>(StringComparer.Ordinal);
        foreach (ReligionTenetDef def in file.tenets)
        {
            if (string.IsNullOrWhiteSpace(def?.id)) continue;
            def.faiths ??= new List<string>();
            def.spheres ??= new List<string>();
            def.kind = string.IsNullOrWhiteSpace(def.kind) ? KindPractice : def.kind.Trim();
            defs[def.id] = def;
        }
        _defs = defs;
        _file = file;
        InnateCache.Clear();
        _version++;
    }

    public static ReligionTenetDef Def(string id)
    {
        EnsureLoaded();
        return id != null && _defs.TryGetValue(id, out ReligionTenetDef def) ? def : null;
    }

    #region 本有与融来

    // 本有教义：按宗教类型、宗教的天下现算
    public static List<string> Innate(Religion religion)
    {
        if (religion?.data == null) return new List<string>();
        EnsureLoaded();
        string type = ReligionFaith.Of(religion).ToString();
        string sphere = ReligionSphere.SphereOf(religion);
        string key = type + "|" + sphere;
        if (InnateCache.TryGetValue(key, out List<string> list)) return list;
        list = new List<string>();
        foreach (ReligionTenetDef def in _file.tenets)
        {
            if (string.IsNullOrWhiteSpace(def?.id) || def.kind == KindSaint) continue;
            if (def.faiths.Contains(type) || sphere.Length > 0 && def.spheres.Contains(sphere)) list.Add(def.id);
        }
        InnateCache[key] = list;
        return list;
    }

    // 融来的教义；分出的教会头一次查时照抄母宗教的(宗教改革的改革者、诸侯分出的"正本清源"，不抄仪轨与本地圣徒)
    public static List<SyncreticTenetData> Absorbed(Religion religion) => Absorbed(religion, 0);

    private static List<SyncreticTenetData> Absorbed(Religion religion, int depth)
    {
        if (religion?.data == null) return new List<SyncreticTenetData>();
        ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
        if (data.Syncretic != null) return data.Syncretic;
        var list = new List<SyncreticTenetData>();
        Religion parent = data.ParentReligionId >= 0 && depth < 8 ? World.world?.religions?.get(data.ParentReligionId) : null;
        if (parent?.data != null && parent != religion && !parent.isRekt())
        {
            int origin = data.SplitOrigin?.kind ?? 0;
            bool purge = origin == PapacySystem.OriginPriest || origin == PapacySystem.OriginPrince;
            foreach (SyncreticTenetData entry in Absorbed(parent, depth + 1))
            {
                ReligionTenetDef def = Def(entry?.tenet);
                if (def == null || purge && def.kind is KindPractice or KindSaint) continue;
                list.Add(new SyncreticTenetData
                {
                    tenet = entry.tenet, from_id = entry.from_id, from_root = entry.from_root, from_name = entry.from_name,
                    sphere = entry.sphere, at = entry.at
                });
            }
        }
        data.Syncretic = list;
        if (list.Count > 0) Invalidate();
        return list;
    }

    public static bool Has(Religion religion, string tenet) =>
        Innate(religion).Contains(tenet) || Absorbed(religion).Any(entry => entry.tenet == tenet);

    #endregion

    #region 合计

    public static Effects Of(Religion religion)
    {
        if (religion?.data == null) return Empty;
        long id = religion.data.id;
        int frame = Time.frameCount;
        if (EffectCache.TryGetValue(id, out var cached) && cached.frame == frame && cached.version == _version) return cached.effects;
        Effects effects = Compute(religion);
        if (EffectCache.Count > 512) EffectCache.Clear();
        EffectCache[id] = (frame, _version, effects);
        return effects;
    }

    private static Effects Compute(Religion religion)
    {
        var effects = new Effects();
        foreach (string id in Innate(religion)) Apply(effects, Def(id), 1f, null, true);
        float strength = Syncretism.absorbed_strength;
        foreach (SyncreticTenetData entry in Absorbed(religion))
        {
            ReligionTenetDef def = Def(entry?.tenet);
            if (def == null) continue;
            effects.AbsorbedCount++;
            if (def.organizes) effects.AbsorbedOrganizes = true;
            Apply(effects, def, strength, entry.sphere, false);
        }
        return effects;
    }

    private static void Apply(Effects effects, ReligionTenetDef def, float k, string sphere, bool innate)
    {
        if (def == null || innate && !def.innate_effects) return;
        effects.Mission += def.mission * k;
        effects.Inertia += def.inertia * k;
        effects.HomeInertia += def.home_inertia * k;
        effects.Resist += def.resist * k;
        effects.Defense += def.defense * k;
        effects.TempleResearch += def.temple_research * k;
        effects.HolyCity += def.holy_city * k;
        effects.Concord += def.concord * k;
        // 槽位、开放度、会堂只算本有的
        if (innate)
        {
            effects.Openness += def.openness;
            effects.Slots += def.slots;
            if (def.capture_immune) effects.CaptureImmune = true;
        }
        if (def.legitimacy != 0) effects.Legitimacy.Add((Mathf.RoundToInt(def.legitimacy * k), def.legitimacy_share));
        if (def.saint_inertia > 0f && !string.IsNullOrEmpty(sphere))
        {
            effects.Saints.TryGetValue(sphere, out float current);
            effects.Saints[sphere] = current + def.saint_inertia * k;
        }
    }

    #endregion

    #region 效果

    // 传教力的倍数(升格的民族宗教另加)
    public static float MissionFactor(Religion religion)
    {
        if (religion?.data == null) return 1f;
        float factor = 1f + Of(religion).Mission;
        if (IsOrganized(religion)) factor += Syncretism.organized_mission;
        return Mathf.Max(0.1f, factor);
    }

    // 惰性的倍数(不看在哪个天下)
    public static float InertiaFactor(Religion religion) => religion?.data == null ? 1f : Mathf.Max(0.1f, 1f + Of(religion).Inertia);

    // 宗教的天下就是这个天下
    public static bool AtHome(Religion religion, string sphere) =>
        religion?.data != null && !string.IsNullOrEmpty(sphere) && ReligionSphere.SphereOf(religion) == sphere;

    // 本城惯性再乘的倍数：本天下 ×(1 + 0.5 + 祖灵护土等)，有本地圣徒的天下另加
    public static float HomeFactor(Religion religion, string citySphere)
    {
        if (religion?.data == null || string.IsNullOrEmpty(citySphere)) return 1f;
        Effects effects = Of(religion);
        float factor = 1f;
        if (AtHome(religion, citySphere)) factor += Home.inertia + effects.HomeInertia;
        if (effects.Saints.TryGetValue(citySphere, out float saint)) factor += saint;
        return factor;
    }

    // 本土抗皈依(神国)：意愿再乘 1 − 这个数
    public static float Resist(Religion religion) => religion?.data == null ? 0f : Mathf.Clamp(Of(religion).Resist, 0f, 0.9f);

    public static float HolyCityFactor(Religion religion) => religion?.data == null ? 1f : 1f + Of(religion).HolyCity;

    public static float TempleResearchFactor(Religion religion) => religion?.data == null ? 1f : 1f + Of(religion).TempleResearch;

    public static bool CaptureImmune(Religion religion) => religion?.data != null && Of(religion).CaptureImmune;

    // 以融合御同化：absorber 从 source 一系(同一宗系之首)融了 n 条，source 对它的势能与皈依意愿 ×0.85ⁿ，至多 ×0.55
    public static float Pull(Religion absorber, Religion source)
    {
        if (absorber?.data == null || source?.data == null || absorber == source) return 1f;
        List<SyncreticTenetData> list = Absorbed(absorber);
        if (list.Count == 0) return 1f;
        long root = RootId(source);
        int count = list.Count(entry => entry.from_id == source.data.id || root >= 0 && entry.from_root == root);
        if (count == 0) return 1f;
        ReligionSyncretismConfig config = Syncretism;
        return Mathf.Max(config.pull_floor, Mathf.Pow(config.pull_factor, count));
    }

    // 宗系之首：沿母宗教往上(母宗教亡了就停在这一辈)
    public static long RootId(Religion religion)
    {
        Religion current = religion;
        for (int depth = 0; depth < 8 && current?.data != null; depth++)
        {
            long parentId = current.GetOrCreate().ParentReligionId;
            Religion parent = parentId >= 0 ? World.world?.religions?.get(parentId) : null;
            if (parent?.data == null || parent == current || parent.isRekt()) return current.data.id;
            current = parent;
        }
        return current?.data?.id ?? -1L;
    }

    // 升格：民族宗教(民间、腾格里、神道)融到 organized_min 条以上、其中有"经典"或"仿效教阶"，在本天下内可当皈依候选，传教另加
    public static bool IsOrganized(Religion religion)
    {
        if (religion?.data == null) return false;
        if (ReligionFaith.Of(religion) is not (FaithType.Folk or FaithType.Tengri or FaithType.Shinto)) return false;
        Effects effects = Of(religion);
        return effects.AbsorbedOrganizes && effects.AbsorbedCount >= Syncretism.organized_min;
    }

    // 施行宽容制度(三教并立、神佛习合、游牧宗教宽容、罗马世俗化)的国家奉它为国教
    public static bool Tolerant(Religion religion)
    {
        if (religion?.data == null || World.world?.kingdoms == null) return false;
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom.religion != religion) continue;
            if (InstitutionSystem.HasFeature(CultureService.GetInstitutionCulture(kingdom), ReligiousTension.ToleranceFeature)) return true;
        }
        return false;
    }

    // 融合槽：类型底子 + 本有的结构教义 + 宽容制度
    public static int Slots(Religion religion, bool tolerant)
    {
        if (religion?.data == null) return 0;
        Syncretism.slots.TryGetValue(ReligionFaith.Of(religion).ToString(), out int slots);
        if (slots <= 0) return 0;
        slots += Of(religion).Slots;
        if (tolerant) slots += Syncretism.tolerance_slots;
        return Mathf.Max(0, slots);
    }

    public static float Openness(Religion religion, bool tolerant)
    {
        if (religion?.data == null) return 0f;
        Syncretism.openness.TryGetValue(ReligionFaith.Of(religion).ToString(), out float openness);
        openness += Of(religion).Openness;
        if (tolerant) openness *= Syncretism.tolerance_openness;
        return Mathf.Max(0f, openness);
    }

    // absorber 能从 source 融哪几条：普世宗教只融本地圣徒(按来源的天下，每个天下一条)；排他的只融仪轨；
    // 伦理、组织给不排他的；教理、结构不外传；已有的不再融
    public static List<ReligionTenetDef> Candidates(Religion absorber, Religion source)
    {
        var result = new List<ReligionTenetDef>();
        if (absorber?.data == null || source?.data == null || absorber == source) return result;
        if (ReligionFaith.IsUniversal(absorber))
        {
            string sphere = ReligionSphere.SphereOf(source);
            ReligionTenetDef saint = Def(LocalSaints);
            if (saint != null && sphere.Length > 0 && !Absorbed(absorber).Any(entry => entry.tenet == LocalSaints && entry.sphere == sphere))
                result.Add(saint);
            return result;
        }
        bool exclusive = ReligionFaith.IsExclusive(absorber);
        var own = new HashSet<string>(Innate(absorber), StringComparer.Ordinal);
        foreach (SyncreticTenetData entry in Absorbed(absorber)) own.Add(entry.tenet);
        foreach (string id in Innate(source))
        {
            ReligionTenetDef def = Def(id);
            if (def == null || own.Contains(id)) continue;
            bool fits = def.kind switch
            {
                KindPractice => true,
                KindEthic or KindOrganization => !exclusive,
                _ => false
            };
            if (fits) result.Add(def);
        }
        return result;
    }

    // 与国教同教之城的稳定加成：本天下宗教 +2，另加节庆等教义
    public static float ConcordBonus(City city)
    {
        if (city?.data == null || city.isRekt() || city.kingdom?.data == null || city.kingdom.wild || !city.hasReligion() ||
            city.kingdom.religion != city.religion) return 0f;
        Religion faith = city.religion;
        float bonus = Of(faith).Concord;
        if (AtHome(faith, ReligionSphere.SphereOfCity(city))) bonus += Home.concord;
        return bonus;
    }

    public static string DescribeConcord(City city) =>
        string.Format(LM.Get("stability_faith_concord"), city?.religion?.name ?? "", ConcordBonus(city));

    // 本土御侮：side 的国教在这座城的天下是本土宗教，又有"神国"之类的教义
    public static float DefenseFactor(Kingdom side, City city)
    {
        Religion faith = side?.data != null && side.hasReligion() ? side.religion : null;
        if (faith == null || city?.data == null) return 1f;
        float bonus = Of(faith).Defense;
        return bonus > 0f && AtHome(faith, ReligionSphere.SphereOfCity(city)) ? 1f + bonus : 1f;
    }

    // 正统(MonarchyLegitimacy)：奉本天下正教、治下过半同教 +3；礼教纲常、长生天命、敬天法祖等教义另加
    public static IEnumerable<(string key, int value)> LegitimacyItems(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (empire?.data == null || core?.data == null || core.isRekt() || !core.hasReligion()) yield break;
        Religion faith = core.religion;
        float share = Share(empire, core, faith);
        string sphere = ReligionSphere.SphereOfCulture(CultureService.GetRealmCulture(core));
        if (Home.legitimacy != 0 && sphere.Length > 0 && ReligionSphere.OrthodoxOf(sphere) == faith && share >= Home.legitimacy_share)
            yield return ("mandate_native_faith", Home.legitimacy);
        int tenets = 0;
        foreach ((int value, float need) in Of(faith).Legitimacy)
            if (share >= need) tenets += value;
        if (tenets != 0) yield return ("mandate_faith_tenets", tenets);
    }

    // 帝国(中央连同成员)人口里奉 faith 的比例；每月重算一次
    private static float Share(Empire empire, Kingdom core, Religion faith)
    {
        double now = World.world.getCurWorldTime();
        long key = empire.data.id;
        if (ShareCache.TryGetValue(key, out var cached) && cached.faith == faith.data.id && now >= cached.at && now - cached.at < 5d)
            return cached.share;
        float total = 0f, same = 0f;
        IEnumerable<Kingdom> members = empire.kingdoms_list != null ? empire.kingdoms_list.Append(core).Distinct() : new[] { core };
        foreach (Kingdom kingdom in members)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.cities == null) continue;
            foreach (City city in kingdom.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                float people = Mathf.Max(1, city.CountLivingPopulation());
                total += people;
                if (city.religion == faith) same += people;
            }
        }
        float share = total > 0f ? same / total : 0f;
        if (ShareCache.Count > 256) ShareCache.Clear();
        ShareCache[key] = (now, faith.data.id, share);
        return share;
    }

    #endregion
}
