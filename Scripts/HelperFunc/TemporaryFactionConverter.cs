using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Scripts.HelperFunc;

public sealed class TemporaryFactionConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
        => typeof(TemporaryFaction).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;

        var jo = JObject.Load(reader);
        // 1) 先取 "type"，没有再兼容 "$type"
        TemporaryFactionType typeEnum;
        if (jo.TryGetValue("type", out var t1))
        {
            typeEnum = t1.ToObject<TemporaryFactionType>();
        }
        else if (jo.TryGetValue("$type", out var t2))  // 兼容老存档
        {
            var full = t2.ToString();
            var cls  = full.Split(',')[0].Split('.').Last(); // TempFac_汉化
            var name = cls.StartsWith("TempFac_") ? cls.Substring("TempFac_".Length) : cls;
            if (!Enum.TryParse(name, out typeEnum)) return null; // 跳过这一项
        }
        else
        {
            return null; // 老存档完全没有类型信息：跳过这一项（或返回某个默认实现）
        }

        var className = "TempFac_" + typeEnum;
        var t = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(x => x != null
                              && x.Name == className
                              && typeof(TemporaryFaction).IsAssignableFrom(x)
                              && !x.IsAbstract);
        if (t == null)
            throw new JsonSerializationException($"TemporaryFaction 反序列化失败：未找到类型 {className}");

        var inst = Activator.CreateInstance(t);
        // 读取保存的数据
        var savedId   = jo["TargetID"]?.ToObject<long?>();       // 若你保存成别的名字，按实际取
        var savedType = jo["TargetType"]?.ToObject<MetaType?>();
        if (savedId.HasValue && savedType.HasValue)
        {
            // 用基类入口恢复（会记录日志）
            // 需要把方法设为 internal/protected internal 以便 converter 调用，或放到同一个类里
            (inst as TemporaryFaction)?.SetTargetFromSave(savedId.Value, savedType.Value, "json-load");
        }
        serializer.Populate(jo.CreateReader(), inst);
        return inst;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        var tf = (TemporaryFaction)value;

        // 手动构建 JObject，避免递归到本转换器
        var jo = new JObject
        {
            ["type"]        = JToken.FromObject(tf.type, serializer),
            ["kingdoms"]    = JArray.FromObject(tf.kingdoms ?? new List<long>(), serializer),
            ["factionType"] = JToken.FromObject(tf.factionType, serializer),
            ["EmpireID"]    = tf.EmpireID,
            ["targetID"]    = tf.TargetID,
            ["targetType"]  = JToken.FromObject(tf.TargetType, serializer),
            ["progress"]    = tf.progress,
            ["started"]    = tf.IsStarted(),
            ["timestamp"] = tf.timestamp,
            // 如果子类有额外**纯数据**字段，也在这里一并手动写出；
            // 对于会导致循环引用的对象（如 Regime/Kingdom/World 指针）务必不要写。
        };

        jo.WriteTo(writer);
    }
}