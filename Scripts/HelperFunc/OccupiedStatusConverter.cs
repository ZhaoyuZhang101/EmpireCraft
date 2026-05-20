using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class OccupiedStatusConverter : Newtonsoft.Json.JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Dictionary<long, List<int>>);
    }

    public override object ReadJson(
        JsonReader reader,
        Type objectType,
        object existingValue,
        JsonSerializer serializer)
    {
        Dictionary<long, List<int>> result = new Dictionary<long, List<int>>();

        if (reader.TokenType == JsonToken.Null)
        {
            return result;
        }

        JObject obj = JObject.Load(reader);

        foreach (JProperty property in obj.Properties())
        {
            string keyText = property.Name;

            long kingdomId = ParseKingdomId(keyText);

            if (kingdomId <= 0)
            {
                continue;
            }

            List<int> zones = property.Value.ToObject<List<int>>(serializer);

            if (zones == null)
            {
                zones = new List<int>();
            }

            if (!result.TryGetValue(kingdomId, out List<int> existingZones))
            {
                existingZones = new List<int>();
                result[kingdomId] = existingZones;
            }

            foreach (int zoneId in zones)
            {
                if (!existingZones.Contains(zoneId))
                {
                    existingZones.Add(zoneId);
                }
            }
        }

        return result;
    }

    public override void WriteJson(
        JsonWriter writer,
        object value,
        JsonSerializer serializer)
    {
        Dictionary<long, List<int>> dict = value as Dictionary<long, List<int>>;

        writer.WriteStartObject();

        if (dict != null)
        {
            foreach (var pair in dict)
            {
                writer.WritePropertyName(pair.Key.ToString());
                serializer.Serialize(writer, pair.Value);
            }
        }

        writer.WriteEndObject();
    }

    private static long ParseKingdomId(string keyText)
    {
        if (string.IsNullOrEmpty(keyText))
        {
            return -1;
        }

        // 新格式："645"
        if (long.TryParse(keyText, out long directId))
        {
            return directId;
        }

        // 旧格式：
        // [Kingdom:645 "合浦 国" Cities:2 Units:462]
        const string prefix = "[Kingdom:";

        int start = keyText.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return -1;
        }

        start += prefix.Length;

        int end = keyText.IndexOf(' ', start);

        if (end < 0)
        {
            end = keyText.IndexOf(']', start);
        }

        if (end < 0 || end <= start)
        {
            return -1;
        }

        string idText = keyText.Substring(start, end - start);

        if (long.TryParse(idText, out long legacyId))
        {
            return legacyId;
        }

        return -1;
    }
}