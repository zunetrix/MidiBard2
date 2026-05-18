using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MidiBard.Managers;

internal class DbTrack
{
    public int Index;
    public bool Enabled = true;
    public string Name;
    public int Transpose;
    public uint Instrument;
    [JsonConverter(typeof(SafeULongListConverter))]
    public List<ulong> AssignedCids = new List<ulong>();
}

// custom parser that handles invalid file values
public class SafeULongListConverter : JsonConverter<List<ulong>>
{
    public override List<ulong> ReadJson(
        JsonReader reader, Type objectType,
        List<ulong> existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        var list = new List<ulong>();
        var array = JArray.Load(reader);

        foreach (var token in array)
        {
            if (token.Type != JTokenType.Integer)
                continue;

            long cid = token.Value<long>();

            // ignores 0 and negative cids
            if (cid > 0)
                list.Add((ulong)cid);
        }

        return list;
    }

    public override void WriteJson(
        JsonWriter writer, List<ulong> value,
        JsonSerializer serializer)
    {
        writer.WriteStartArray();

        if (value != null)
        {
            foreach (var cid in value)
            {
                // ignores 0 and negative cids
                if (cid == 0)
                    continue;

                writer.WriteValue(cid);
            }
        }

        writer.WriteEndArray();
    }
}
