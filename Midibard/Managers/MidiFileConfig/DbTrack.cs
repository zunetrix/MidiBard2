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
    // fix old json with not ulong values like -1
    [JsonConverter(typeof(ULongListIgnoringNegativeConverter))]
    public List<ulong> AssignedCids = new List<ulong>();
}

// internal class DbChannel
// {
//     public int Transpose;
//     public int Instrument;
//     public List<long> AssignedCids = new List<long>();
// }

public class ULongListIgnoringNegativeConverter : JsonConverter<List<ulong>>
{
    public override List<ulong> ReadJson(JsonReader reader, Type objectType, List<ulong> existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var list = new List<ulong>();

        var array = JArray.Load(reader);
        foreach (var token in array)
        {
            if (token.Type == JTokenType.Integer)
            {
                long value = token.Value<long>();

                if (value >= 0)
                    list.Add((ulong)value);
            }
        }

        return list;
    }

    public override void WriteJson(JsonWriter writer, List<ulong> value, JsonSerializer serializer)
    {
        serializer.Serialize(writer, value);
    }
}
