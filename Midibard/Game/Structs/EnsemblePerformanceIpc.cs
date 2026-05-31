using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace MidiBard.Structs;

[StructLayout(LayoutKind.Sequential)]
struct EnsemblePerformanceIpc
{
    public uint unk1;
    private readonly short pad1;
    public ushort WorldId;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public EnsembleCharacterData[] EnsembleCharacterDatas;

    public uint[] Ids => EnsembleCharacterDatas.Select(i => i.EntityId).Where(i => i != 0xE000_0000).ToArray();
    public override string ToString() => string.Join(", ", EnsembleCharacterDatas.Select(i => $"{i.EntityId:X}:{i.NoteNumbers.Count(j => j != 0)}"));
}

[StructLayout(LayoutKind.Sequential)]
struct EnsembleCharacterData
{
    public bool IsValid => EntityId is not (0 or 0xE000_0000);

    /// <summary>
    /// source entityId, if null it's 0xE000_0000
    /// </summary>
    public uint EntityId;

    /// <summary>
    /// 3C or 00 for null actor
    /// </summary>
    public byte noteCount;

    /// <summary>
    /// 60 note numbers for 3 seconds sample.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 60)]
    public byte[] NoteNumbers;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 60)]
    public byte[] ToneNumbers;

    private readonly byte pad1;
    private readonly byte pad2;
    private readonly byte pad3;
}
