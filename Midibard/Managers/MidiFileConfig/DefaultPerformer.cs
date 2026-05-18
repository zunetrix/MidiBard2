using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MidiBard.Managers;

internal class DefaultPerformer
{
    [JsonConverter(typeof(SafeULongDictionaryConverter))]
    public Dictionary<ulong, List<int>> TrackMappingDict = new Dictionary<ulong, List<int>>();
}

// custom parser that handles invalid file values
public class SafeULongDictionaryConverter : JsonConverter<Dictionary<ulong, List<int>>>
{
    public override Dictionary<ulong, List<int>> ReadJson(
        JsonReader reader,
        Type objectType,
        Dictionary<ulong, List<int>>? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        var result = new Dictionary<ulong, List<int>>();
        var obj = JObject.Load(reader);

        foreach (var prop in obj.Properties())
        {
            if (!ulong.TryParse(prop.Name, out var key) || key == 0)
                continue;

            try
            {
                var list = prop.Value.ToObject<List<int>>(serializer) ?? new List<int>();
                result[key] = list;
            }
            catch
            {
                DalamudApi.PluginLog.Warning($"Invalid DefaultPerformer JSON value {key}: {result[key]}");
            }
        }

        return result;
    }

    public override void WriteJson(
    JsonWriter writer,
    Dictionary<ulong, List<int>>? value,
    JsonSerializer serializer)
    {
        writer.WriteStartObject();

        if (value != null)
        {
            foreach (var kvp in value)
            {
                if (kvp.Key == 0)
                    continue;

                writer.WritePropertyName(kvp.Key.ToString());
                serializer.Serialize(writer, kvp.Value);
            }
        }

        writer.WriteEndObject();
    }
}
