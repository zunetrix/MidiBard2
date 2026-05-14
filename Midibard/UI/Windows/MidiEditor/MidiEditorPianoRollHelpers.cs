namespace MidiBard;

internal static class MidiEditorPencilNoteSizing
{
    private static readonly int[] Divisions = { 1, 2, 4, 8, 16, 32, 64, 128 };

    public static readonly string[] DivisionLabels = { "1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64", "1/128" };

    public static long GetDurationTicks(int ticksPerQuarterNote, int divisionIndex)
    {
        var ppqn = ticksPerQuarterNote > 0 ? ticksPerQuarterNote : 480;
        return 4L * ppqn / Divisions[divisionIndex];
    }
}
