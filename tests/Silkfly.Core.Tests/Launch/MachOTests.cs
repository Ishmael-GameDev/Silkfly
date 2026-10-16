using System.Buffers.Binary;
using System.IO.Abstractions.TestingHelpers;
using Silkfly.Core.Launch;

namespace Silkfly.Core.Tests.Launch;

public sealed class MachOTests
{
    private const int CpuX64 = 0x01000007;
    private const int CpuArm64 = 0x0100000C;
    private const int CpuArm = 12;

    [Fact]
    public void Thin_x86_64() => Read(Thin(0xFEEDFACF, CpuX64)).ShouldBe(MachOArch.X64);

    [Fact]
    public void Thin_arm64() => Read(Thin(0xFEEDFACF, CpuArm64)).ShouldBe(MachOArch.Arm64);

    [Fact]
    public void Thin_32_bit_is_other() => Read(Thin(0xFEEDFACE, CpuArm)).ShouldBe(MachOArch.Other);

    [Fact]
    public void Thin_big_endian_header()
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 0xFEEDFACF);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4), CpuX64);

        Read(bytes).ShouldBe(MachOArch.X64);
    }

    [Fact]
    public void Fat_with_both() => Read(Fat(wide: false, CpuX64, CpuArm64)).ShouldBe(MachOArch.X64 | MachOArch.Arm64);

    [Fact]
    public void Fat_64_with_both() => Read(Fat(wide: true, CpuArm64, CpuX64)).ShouldBe(MachOArch.X64 | MachOArch.Arm64);

    [Fact]
    public void Fat_with_x86_64_only() => Read(Fat(wide: false, CpuX64)).ShouldBe(MachOArch.X64);

    [Fact]
    public void Truncated_fat_header_is_none() => Read(Fat(wide: false, CpuX64, CpuArm64)[..20]).ShouldBe(MachOArch.None);

    [Fact]
    public void Java_class_file_is_not_mach_o() =>
        Read([0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0x00, 0x00, 0x34, 0x00, 0x10]).ShouldBe(MachOArch.None);

    [Fact]
    public void Elf_is_not_mach_o() => Read([0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 0, 0]).ShouldBe(MachOArch.None);

    [Fact]
    public void Short_file_is_none() => Read([0xCF, 0xFA]).ShouldBe(MachOArch.None);

    [Fact]
    public void Missing_file_is_none() => MachO.Read(new MockFileSystem(), TestPath.Of("/nowhere/libdoorstop.dylib")).ShouldBe(MachOArch.None);

    [Fact]
    public void Reads_from_a_file()
    {
        var fs = new MockFileSystem();
        var path = TestPath.Of("/game/libdoorstop.dylib");
        fs.AddFile(path, new MockFileData(Thin(0xFEEDFACF, CpuX64)));

        MachO.Read(fs, path).ShouldBe(MachOArch.X64);
    }

    private static MachOArch Read(byte[] bytes) => MachO.Read(new MemoryStream(bytes));

    // mach_header(_64) as written by a little-endian machine
    private static byte[] Thin(uint magic, int cpuType)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, magic);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), cpuType);
        return bytes;
    }

    // fat_header and fat_arch(_64) entries are always big-endian
    private static byte[] Fat(bool wide, params int[] cpuTypes)
    {
        var entrySize = wide ? 32 : 20;
        var bytes = new byte[8 + cpuTypes.Length * entrySize];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, wide ? 0xCAFEBABF : 0xCAFEBABE);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)cpuTypes.Length);
        for (var i = 0; i < cpuTypes.Length; i++)
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8 + i * entrySize), cpuTypes[i]);
        return bytes;
    }
}
