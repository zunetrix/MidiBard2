using Lumina.Excel.Sheets;

using Melanchall.DryWetMidi.Common;

using MidiBard.Extensions.Dalamud;
using MidiBard.Extensions.DryWetMidi;

namespace MidiBard.Control;

public class Instrument
{
    public Instrument(Perform perform)
    {
        Row = perform;
        IconId = (uint)perform.Icon;
        GuitarTone = perform.GetGuitarTone();
        IsGuitar = perform.IsGuitar();
        ProgramNumber = perform.GetMidiProgramId();
        FFXIVDisplayName = perform.GetInstrumentDisplayName();
        FFXIVProgramName = perform.GetGameProgramName();
        GeneralMidiProgramName = ProgramNumber.GetGMProgramName();
        // InstrumentString = $"{(row.RowId == 0 ? "None" : $"{row.Instrument.ToDalamudString().TextValue} ({row.Name})")}";
        InstrumentString = $"{(perform.RowId == 0 ? "None" : $"{perform.GetInstrumentDisplayName()}")}";
    }

    public Perform Row { get; }
    public uint IconId { get; }
    public bool IsGuitar { get; }
    public int GuitarTone { get; }
    public SevenBitNumber ProgramNumber { get; }
    public string FFXIVDisplayName { get; }
    public string FFXIVProgramName { get; }
    public string GeneralMidiProgramName { get; }
    public readonly string InstrumentString;
    public override string ToString() => InstrumentString;
}
