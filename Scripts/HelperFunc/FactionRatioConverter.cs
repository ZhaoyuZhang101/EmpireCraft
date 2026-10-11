using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Regimes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Scripts.HelperFunc;

/// <summary>
/// Persists faction ratios by stable faction ID instead of a runtime faction object.
/// Older saves wrote the object's type name as a JSON key, which cannot be restored
/// to a faction and must be rebuilt when the owning regime is loaded.
/// </summary>
public sealed class FactionRatioConverter : JsonConverter
{
    public static int DiscardedLegacyEntryCount { get; private set; }

    public static void ResetLegacyDiscardCount()
    {
        DiscardedLegacyEntryCount = 0;
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Dictionary<FixedFaction, int>);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
        JsonSerializer serializer)
    {
        var result = new Dictionary<FixedFaction, int>();
        if (reader.TokenType == JsonToken.Null) return result;

        JToken token = JToken.Load(reader);
        if (token is not JObject source) return result;

        foreach (JProperty entry in source.Properties())
        {
            string factionId = entry.Name?.Trim();
            if (!IsStableFactionId(factionId))
            {
                DiscardedLegacyEntryCount++;
                continue;
            }

            int ratio;
            try
            {
                ratio = entry.Value.Value<int>();
            }
            catch
            {
                continue;
            }

            // Regime instances do not exist during deserialization. The temporary
            // key is matched to the real faction by ID in ReconcileFactionRatios.
            result[new FixedFaction { _id = factionId }] = Math.Max(0, ratio);
        }

        return result;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        var ratios = value as Dictionary<FixedFaction, int>;
        if (ratios != null)
        {
            foreach (KeyValuePair<FixedFaction, int> entry in ratios)
            {
                string factionId = entry.Key?.GetID();
                if (!IsStableFactionId(factionId)) continue;
                writer.WritePropertyName(factionId);
                writer.WriteValue(Math.Max(0, entry.Value));
            }
        }

        writer.WriteEndObject();
    }

    private static bool IsStableFactionId(string factionId)
    {
        if (string.IsNullOrWhiteSpace(factionId)) return false;

        // This is how prior versions serialized Dictionary<FixedFaction, int> keys.
        // It contains no faction identity, so retaining it would attach a ratio to a
        // random faction after load. ReconcileFactionRatios safely recreates it.
        return !factionId.StartsWith("EmpireCraft.Scripts.Regimes.FixedFaction", StringComparison.Ordinal) &&
               !factionId.StartsWith("FixedFaction", StringComparison.Ordinal);
    }
}
