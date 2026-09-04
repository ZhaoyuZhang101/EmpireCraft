using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.services;
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
        var t = SafeTypeDiscovery.GetConcreteDerivedTypes(typeof(TemporaryFaction),
                AppDomain.CurrentDomain.GetAssemblies(), message => LogService.LogWarning(message))
            .FirstOrDefault(x => x.Name == className);
        if (t == null)
            throw new JsonSerializationException($"TemporaryFaction 反序列化失败：未找到类型 {className}");

        var inst = Activator.CreateInstance(t);
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
            ["factionID"] = JToken.FromObject(tf.factionID, serializer),
            ["EmpireID"]    = tf.EmpireID,
            ["KingdomID"]   = tf.KingdomID,
            ["targetID"]    = tf.TargetID,
            ["targetType"]  = JToken.FromObject(tf.TargetType, serializer),
            ["progress"]    = tf.progress,
            ["started"]     = tf.StartedState,
            ["Hide"]        = tf.Hide,
            ["Active"]      = tf.Active,
            ["ShowAsPlot"]  = tf.ShowAsPlot,
            ["canBePushByLocal"] = tf.canBePushByLocal,
            ["pusherType"] = JToken.FromObject(tf.pusherType, serializer),
            ["progressMax"] = tf.progressMax,
            ["Acc"] = tf.Acc,
            ["CountDown"]   = tf.CountDown,
            ["timestamp"]   = tf.timestamp,
            ["countDownTimestamp"] = tf.countDownTimestamp,
            // 如果子类有额外**纯数据**字段，也在这里一并手动写出；
            // 对于会导致循环引用的对象（如 Regime/Kingdom/World 指针）务必不要写。
        };

        jo.WriteTo(writer);
    }
}
