using System.Collections.Generic;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

// —— 律法的存档数据与给界面的只读快照 ——
// 存档字段都是新增的可选字段：旧档没有这些字段时按空值读入(没有卷宗、没有前科、不在狱中)

// 结案的结果(卷宗、个人前科存序号；只追加，不改旧值)
public enum LawCaseOutcome
{
    定罪 = 0, // 按律论处
    减等 = 1, // 八议或宽仁之政减一等
    赎罪 = 2, // 纳金赎免笞杖徒流
    开释 = 3, // 查无实据或宽宥，不予处罚
    大赦 = 4, // 新君即位、改元、恩诏：赦免未发之罪、释放在押者(卷宗里记一条总的)
    从重 = 5  // 严苛之政加一等
}

// 一国的宽严
public enum LawStanceTier
{
    宽仁 = -1,
    适中 = 0,
    严苛 = 1
}

// 一国的执法记录(存在 KingdomExtraData.law_state；没判过案的国家为空)
public class LawRealmState
{
    // 最近结案的卷宗，旧的在前；至多 EmpireLawSystem.MaxCases 条
    public List<LawCaseRecord> cases = new();
    // 本国在押者(族谱身份 id)：刑满或大赦时释放
    public List<long> prisoners = new();
    // 上次大赦的时间：此前犯下、尚未发觉的寻常罪行一概赦免(查案时按这个时间作废，见 CleanupCrimeRecords)
    public double last_amnesty = -1d;
    // 近年处决的时间(只留五年内的)，滥刑损正统用
    public List<double> executions = new();
    public double last_purge_penalty = -1d;
    // 一年一次的狱政(刑满释放、宽仁之国恩赦)
    public double last_tick = -1d;
    // 本国(不在帝国里时)有株连子孙、禁锢出仕的判决：有才逐代查祖上(见 IsBarredFromOffice)
    public bool descendant_bans;

    public bool ShouldSerializecases() => cases != null && cases.Count > 0;
    public bool ShouldSerializeprisoners() => prisoners != null && prisoners.Count > 0;
    public bool ShouldSerializeexecutions() => executions != null && executions.Count > 0;
    public bool ShouldSerializelast_amnesty() => last_amnesty >= 0d;
    public bool ShouldSerializelast_purge_penalty() => last_purge_penalty >= 0d;
    public bool ShouldSerializedescendant_bans() => descendant_bans;
}

// 一桩结案的卷宗
public class LawCaseRecord
{
    public double timestamp = -1d;
    public string date = "";
    public long actor_id = -1L;
    public long identity_id = -1L;
    public string actor_name = "";
    // -1：大赦这类不涉具体罪名的记录
    public int law_type = -1;
    // 实际施行的刑罚
    public List<PunishmentLevel> punishments = new();
    public LawCaseOutcome outcome;
    // 案由(本地化键，Prosecute 的 reasonKey，如"兵败被擒")
    public string reason_key = "";
    // 量刑说明(本地化键，可带一个参数："键|参数")：八议、赎金、时代废刑、宽严……
    public List<string> notes = new();
    public long accuser_id = -1L;
    public string accuser_name = "";
    // 大赦：释放的在押者人数
    public int count;
    // 量刑天平(见 SentencingScale)：两边的砝码("键|倍数|参数")、两边的分量、加减了几等
    public List<string> scale_guilt;
    public List<string> scale_mercy;
    public float scale_guilt_weight = 1f;
    public float scale_mercy_weight = 1f;
    public int scale_shift;
    public bool scale_mercy_ignored;

    public bool ShouldSerializepunishments() => punishments != null && punishments.Count > 0;
    public bool ShouldSerializenotes() => notes != null && notes.Count > 0;
    public bool ShouldSerializereason_key() => !string.IsNullOrEmpty(reason_key);
    public bool ShouldSerializeaccuser_id() => accuser_id >= 0L;
    public bool ShouldSerializeaccuser_name() => !string.IsNullOrEmpty(accuser_name);
    public bool ShouldSerializecount() => count != 0;
    public bool ShouldSerializeidentity_id() => identity_id >= 0L;
    public bool ShouldSerializescale_guilt() => scale_guilt != null && scale_guilt.Count > 0;
    public bool ShouldSerializescale_mercy() => scale_mercy != null && scale_mercy.Count > 0;
    public bool ShouldSerializescale_guilt_weight() => ShouldSerializescale_guilt() || ShouldSerializescale_mercy();
    public bool ShouldSerializescale_mercy_weight() => ShouldSerializescale_guilt_weight();
    public bool ShouldSerializescale_shift() => scale_shift != 0;
    public bool ShouldSerializescale_mercy_ignored() => scale_mercy_ignored;

    [JsonIgnore] public LawType? Crime => law_type >= 0 ? (LawType)law_type : null;
}

// 个人的前科(记在族谱身份上，与未发的 crime_records 分开；至多 EmpireLawSystem.MaxConvictions 条)
public class LawConvictionRecord
{
    public double timestamp = -1d;
    public string date = "";
    public long kingdom_id = -1L;
    public string kingdom_name = "";
    public int law_type = -1;
    public List<PunishmentLevel> punishments = new();
    public LawCaseOutcome outcome;

    public bool ShouldSerializepunishments() => punishments != null && punishments.Count > 0;

    [JsonIgnore] public LawType? Crime => law_type >= 0 ? (LawType)law_type : null;
}

// 在押：下狱的时间与刑期(年)。刑满或大赦释放；在押期间不得出任官职(见 ActorExtension.CanServeOffice)
public class LawImprisonment
{
    public double since = -1d;
    public int years;
    public long kingdom_id = -1L;
    public int law_type = -1;
    // 十恶之罪：不在大赦之列
    public bool grave;
    // 下狱时是官员、有爵位封号的人：出狱时上公屏
    public bool notable;
}

// 神力加在量刑天平上的一个砝码(见 EmpireLawSystem.Divine)：名目(law_custom_ 后半)、哪一边、倍数
public class LawCustomWeight
{
    public string key = "";
    public bool guilt;
    public float factor = 1f;
}

// 保释(见 EmpireLawSystem.Divine)：条件(bond 保金 / guarantor 担保人 / house 软禁 / redeem 戴罪立功)、担保人(族谱身份)、保金；
// revoked：保释期间再犯，取保作废
public class LawBail
{
    public double since = -1d;
    public List<string> conditions = new();
    public long guarantor = -1L;
    public int bond;
    public bool revoked;
}

// —— 给界面的只读快照 ——

// 一项构成：Key 为本地化键(可带一个 {0} 参数 Arg)，Value 为数值(倍数或加减)
public class LawFactor
{
    public string Key;
    public string Arg;
    public float Value;

    public LawFactor(string key, float value, string arg = null)
    {
        Key = key;
        Value = value;
        Arg = arg;
    }

    public string Label
    {
        get
        {
            string text = LM.Has(Key) ? LM.Get(Key) : Key;
            if (string.IsNullOrEmpty(Arg)) return text;
            try { return string.Format(text, Arg); }
            catch (global::System.FormatException) { return text + " " + Arg; }
        }
    }
}

// 一国的宽严：Severity -1(宽仁) ~ +1(严苛)
public class LawStanceInfo
{
    public float Severity;
    public LawStanceTier Tier = LawStanceTier.适中;
    // 犯罪几率倍数(严则慑、宽则纵)
    public float CrimeRateFactor = 1f;
    // 罚金、刑期的倍数
    public float FineFactor = 1f;
    public float PrisonFactor = 1f;
    // 宽仁之国每年恩赦的机会(距上次大赦满十年后)
    public float AmnestyChance;
    public List<LawFactor> Breakdown = new();
}

public class JudicialOfficeInfo
{
    public OfficeObject Office;
    public Actor Holder;
    // 0~1：空缺为 0，在任者按才干、操守
    public float Score;
}

// 一国司法的力量：Factor 为查出罪行的倍数(1 = 寻常)
public class JudicialStrengthInfo
{
    public float Factor = 1f;
    public bool HasJudicialOffices;
    public int Filled;
    public int Total;
    public List<JudicialOfficeInfo> Offices = new();
    public List<LawFactor> Breakdown = new();
}

// 量刑天平的读数(见 SentencingScale、EmpireLawSystem.GetScale)：罪责一边、情面一边的砝码与分量，天平 = 罪责 ÷ 情面
public class SentencingScaleInfo
{
    public List<LawFactor> Guilt = new();
    public List<LawFactor> Mercy = new();
    public float GuiltWeight = 1f;
    public float MercyWeight = 1f;
    public float Balance = 1f;
    // 加减几等(正加负减)
    public int Shift;
    // 十恶、暴君：情面不上秤
    public bool Grave;
    public bool Tyrant;
    public bool MercyIgnored => Grave || Tyrant;
    // 皇室身份的砝码键(law_scale_heir / law_scale_royal_close / law_scale_royal_kin)，不是皇室为空
    public string RoyalKey = "";
    public bool Royal => !string.IsNullOrEmpty(RoyalKey);
    public bool Heir => RoyalKey == "law_scale_heir";
}

// 某人某年罪行被查出的机会与构成
public class DiscoveryChanceInfo
{
    public float Chance;
    public List<LawFactor> Breakdown = new();
}

// 一国现行的一条律法：本来的刑罚与按时代、政体调整后的刑罚
public class EffectiveLawInfo
{
    public LawType Type;
    public Law Law;
    public LawCategory Category;
    public string Name;
    public string Description;
    // 十恶：不减等、不许赎、不在大赦之列
    public bool Grave;
    public List<PunishmentLevel> BasePunishments = new();
    public List<PunishmentLevel> EffectivePunishments = new();
    // 调整说明(本地化键)
    public List<string> AdaptationNotes = new();
}
