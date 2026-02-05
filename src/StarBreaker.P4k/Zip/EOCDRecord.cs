using System.Runtime.InteropServices;

namespace StarBreaker.P4k;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct EOCDRecord
{
    public static ReadOnlySpan<byte> Magic => [0x50, 0x4b, 0x05, 0x06 ];

    public readonly uint Signature;
    public readonly ushort DiskNumber; // is 0xFFFF if Zip64
    public readonly ushort StartDiskNumber; // is 0xFFFF if Zip64
    public readonly ushort EntriesOnDisk; // is 0xFFFF if Zip64
    public readonly ushort TotalEntries; // is 0xFFFF if Zip64
    public readonly uint CentralDirectorySize; // is 0xFFFFFFFF if Zip64
    public readonly uint CentralDirectoryOffset; // is 0xFFFFFFFF if Zip64
    public readonly ushort CommentLength;
    
    public bool IsZip64 => DiskNumber == 0xFFFF || 
                           StartDiskNumber == 0xFFFF || 
                           EntriesOnDisk == 0xFFFF || 
                           TotalEntries == 0xFFFF || 
                           CentralDirectorySize == 0xFFFFFFFF || 
                           CentralDirectoryOffset == 0xFFFFFFFF;
}