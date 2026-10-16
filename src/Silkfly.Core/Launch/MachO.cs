using System.Buffers.Binary;
using System.IO.Abstractions;

namespace Silkfly.Core.Launch;

[Flags]
public enum MachOArch
{
    None = 0,
    X64 = 1,
    Arm64 = 2,
    Other = 4,
}

// CPU types of a Mach-O file, thin or universal, read from its header.
public static class MachO
{
    private const uint Magic32 = 0xFEEDFACE;
    private const uint Magic64 = 0xFEEDFACF;
    private const uint FatMagic32 = 0xCAFEBABE;
    private const uint FatMagic64 = 0xCAFEBABF;
    private const int CpuX64 = 0x01000007;
    private const int CpuArm64 = 0x0100000C;

    // Java class files also start with CAFEBABE, their version makes the count too big
    private const uint MaxFatArchs = 30;

    public static MachOArch Read(IFileSystem fs, string path)
    {
        try
        {
            if (!fs.File.Exists(path))
                return MachOArch.None;
            using var stream = fs.File.OpenRead(path);
            return Read(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return MachOArch.None;
        }
    }

    public static MachOArch Read(Stream stream)
    {
        Span<byte> header = stackalloc byte[8];
        if (!Fill(stream, header))
            return MachOArch.None;

        var little = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var big = BinaryPrimitives.ReadUInt32BigEndian(header);

        if (little is Magic32 or Magic64)
            return Of(BinaryPrimitives.ReadInt32LittleEndian(header[4..]));
        if (big is Magic32 or Magic64)
            return Of(BinaryPrimitives.ReadInt32BigEndian(header[4..]));
        if (big is FatMagic32 or FatMagic64)
            return ReadFat(stream, BinaryPrimitives.ReadUInt32BigEndian(header[4..]), big == FatMagic64, bigEndian: true);
        if (little is FatMagic32 or FatMagic64)
            return ReadFat(stream, BinaryPrimitives.ReadUInt32LittleEndian(header[4..]), little == FatMagic64, bigEndian: false);
        return MachOArch.None;
    }

    private static MachOArch ReadFat(Stream stream, uint count, bool wide, bool bigEndian)
    {
        if (count is 0 or > MaxFatArchs)
            return MachOArch.None;

        Span<byte> entry = stackalloc byte[wide ? 32 : 20];
        var result = MachOArch.None;
        for (var i = 0; i < count; i++)
        {
            if (!Fill(stream, entry))
                return MachOArch.None;
            result |= Of(bigEndian ? BinaryPrimitives.ReadInt32BigEndian(entry) : BinaryPrimitives.ReadInt32LittleEndian(entry));
        }

        return result;
    }

    private static MachOArch Of(int cpuType) => cpuType switch
    {
        CpuX64 => MachOArch.X64,
        CpuArm64 => MachOArch.Arm64,
        _ => MachOArch.Other,
    };

    private static bool Fill(Stream stream, Span<byte> buffer) =>
        stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
}
