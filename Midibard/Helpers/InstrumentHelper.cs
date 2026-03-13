using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Dalamud.Utility;

using Lumina.Excel;
using Lumina.Excel.Sheets;

using Melanchall.DryWetMidi.Common;

using MidiBard.Control;

namespace MidiBard.Util;

internal static class InstrumentHelper
{
    // Populated once at plugin startup by Initialize().
    public static ExcelSheet<Perform> InstrumentSheet { get; private set; }
    public static Instrument[] Instruments { get; private set; }
    public static Instrument[] Guitars { get; private set; }
    public static string[] InstrumentStrings { get; private set; }
    public static readonly byte[] GuitarGroup = { 24, 25, 26, 27, 28 };
    public static IDictionary<SevenBitNumber, uint> ProgramInstruments { get; private set; }
    // Maps instrument rowId → sanitized display name (letters only). Used for compensation dict keys.
    public static Dictionary<int, string> RowIdToName { get; private set; }

    // Call once during plugin initialisation to load all instrument data from the game sheets.
    internal static void Initialize()
    {
        InstrumentSheet = DalamudApi.DataManager.Excel.GetSheet<Perform>();
        Instruments = InstrumentSheet!
            .Where(i => !string.IsNullOrWhiteSpace(i.Instrument.ToDalamudString().TextValue) || i.RowId == 0)
            .Select(i => new Instrument(i))
            .ToArray();

        Guitars = Instruments.Where(i => i.IsGuitar).ToArray();
        InstrumentStrings = Instruments.Select(i => i.InstrumentString).ToArray();

        ProgramInstruments = new Dictionary<SevenBitNumber, uint>();
        foreach (var (programNumber, instrument) in Instruments.Select((i, index) => (i.ProgramNumber, index)))
            ProgramInstruments[programNumber] = (uint)instrument;

        RowIdToName = Instruments
            .Where(i => i.Row.RowId != 0)
            .ToDictionary(i => (int)i.Row.RowId, i => SanitizeName(i.FFXIVDisplayName));
    }

    // Strips all non-letter characters. Used as the key format for compensation dictionaries.
    public static string SanitizeName(string input) => Regex.Replace(input, "[^a-zA-Z]", "");

    // Returns the FFXIV display name for an instrument rowId from the game sheet.
    public static string GetDisplayName(uint rowId) =>
        InstrumentSheet.GetRow(rowId).Instrument.ToDalamudString().TextValue;

    internal static bool IsGuitar(uint instrumentId) => instrumentId is 24 or 25 or 26 or 27 or 28;

    internal static int GetGuitarTone(uint instrumentId) => instrumentId switch
    {
        24 => 0,
        25 => 1,
        26 => 2,
        27 => 3,
        28 => 4,
        _ => -1
    };
}
