using System.Text.RegularExpressions;

using Dalamud.Utility;

using Lumina.Excel.Sheets;

using Melanchall.DryWetMidi.Common;

namespace MidiBard.Extensions.Dalamud;

public static class PerformExtensions
{
    private static readonly Regex MidiProgramRegex = new(@"^([0-9]{3})(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses perform exd row's backing program number and names
    /// </summary>
    /// <param name="perform">perform sheet row</param>
    /// <param name="id">
    /// SevenBitNumber range is 0-127 (0 as Acoustic Grand Piano), but FFXIV is using 1-128 range, so we subtract FFXIV program numbers by 1
    /// and use 0-127 representations internally to avoid confusion. </param>
    /// <param name="name">FFXIV instrument program name</param>
    /// <returns>returns true if successful parsed</returns>
    public static bool GetMidiProgram(this Perform perform, out SevenBitNumber id, out string name)
    {
        id = SevenBitNumber.MinValue;
        if (perform.RowId == 0)
        {
            name = "None";
            return true;
        }

        Match match = MidiProgramRegex.Match(perform.Name.ToDalamudString().TextValue);
        if (match.Success)
        {
            if (SevenBitNumber.TryParse(match.Groups[1].Value, out var GameId))
            {
                id = new SevenBitNumber((byte)(GameId - 1));
                name = match.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(name))
                    return true;
            }
        }

        name = "";
        return false;
    }

    public static string GetInstrumentDisplayName(this Perform perform)
    {
        var insturmentName = perform.Instrument.ToDalamudString().TextValue;
        return Regex.Replace(insturmentName, "[^a-zA-Z]", "");
    }

    public static SevenBitNumber GetMidiProgramId(this Perform perform)
    {
        perform.GetMidiProgram(out SevenBitNumber id, out _);
        return id;
    }

    public static string GetGameProgramName(this Perform perform)
    {
        perform.GetMidiProgram(out _, out string name);
        return name;
    }

    public static bool IsGuitar(this Perform perform)
    {
        // instrumentId
        return perform.RowId is 24 or 25 or 26 or 27 or 28;
    }

    public static int GetGuitarTone(this Perform perform)
    {
        // instrumentId
        return perform.RowId switch
        {
            24 => 0,
            25 => 1,
            26 => 2,
            27 => 3,
            28 => 4,
            _ => -1
        };
    }
}
