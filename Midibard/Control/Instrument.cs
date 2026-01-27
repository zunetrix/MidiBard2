using Dalamud.Interface.Textures;
using Dalamud.Utility;

using Lumina.Excel.Sheets;

using Melanchall.DryWetMidi.Common;

using MidiBard.Managers;
using MidiBard.Util;

namespace MidiBard.Control;

public class Instrument
{
    public Instrument(Perform row)
    {
        Row = row;
        GuitarTone = InstrumentHelper.GetGuitarTone(row.RowId);
        IsGuitar = InstrumentHelper.IsGuitar(row.RowId);
        ProgramNumber = Row.GetMidiProgramId();
        FFXIVDisplayName = row.Instrument.ToDalamudString().TextValue;
        FFXIVProgramName = Row.GetGameProgramName();
        GeneralMidiProgramName = ProgramNumber.GetGMProgramName();
        // InstrumentString = $"{(row.RowId == 0 ? "None" : $"{row.Instrument.ToDalamudString().TextValue} ({row.Name})")}";
        InstrumentString = $"{(row.RowId == 0 ? "None" : $"{row.Instrument.ToDalamudString().TextValue}")}";
        IconTextureWrap = TextureManager.Get((uint)row.Icon);
    }
    public Perform Row { get; }
    public bool IsGuitar { get; }
    public int GuitarTone { get; }
    public SevenBitNumber ProgramNumber { get; }
    public string FFXIVDisplayName { get; }
    public string FFXIVProgramName { get; }
    public string GeneralMidiProgramName { get; }
    public readonly string InstrumentString;
    public override string ToString() => InstrumentString;
    public ISharedImmediateTexture IconTextureWrap { get; }
}
