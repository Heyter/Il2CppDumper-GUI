using JetBrains.Annotations;

namespace Il2CppDumper;

[NoReorder]
public class MachoSection
{
    public string sectname;
    public uint addr;
    public uint size;
    public uint offset;
    public uint flags;
}

[NoReorder]
public class MachoSection64Bit
{
    public string sectname;
    public ulong addr;
    public ulong size;
    public ulong offset;
    public uint flags;
}

[NoReorder]
public class Fat
{
    public uint offset;
    public uint size;
    public uint magic;
}

[NoReorder]
public class MachoSegment64Bit
{
    public string name;
    public ulong vmaddr;
    public ulong vmsize;
    public ulong fileoff;
    public ulong filesize;
}

// The classes below are copied from https://github.com/lief-project/LIEF/tree/main/src/MachO
// For new MachO load command LC_DYLD_CHAINED_FIXUPS in iOS 15+.
[NoReorder]
public class dyld_chained_fixups_header
{
    public uint fixups_version;
    public uint starts_offset;
    public uint imports_offset;
    public uint symbols_offset;
    public uint imports_count;
    public uint symbols_format;
    public uint imports_format;
}

[NoReorder]
public class dyld_chained_starts_in_image
{
    public uint seg_count;
    // uint seg_info_offset[1];
}

[NoReorder]
public class dyld_chained_starts_in_segment
{
    public uint size;
    public ushort page_size;
    public ushort pointer_format;
    public ulong segment_offset;
    public uint max_valid_pointer;
    public ushort page_count;
    // ushort page_start[1];
}

[NoReorder]
public enum DYLD_CHAINED_PTR_FORMAT : ushort
{
    DYLD_CHAINED_PTR_ARM64E = 1,
    DYLD_CHAINED_PTR_64 = 2,
    DYLD_CHAINED_PTR_32 = 3,
    DYLD_CHAINED_PTR_32_CACHE = 4,
    DYLD_CHAINED_PTR_32_FIRMWARE = 5,
    DYLD_CHAINED_PTR_64_OFFSET = 6,
    DYLD_CHAINED_PTR_ARM64E_KERNEL = 7,
    DYLD_CHAINED_PTR_64_KERNEL_CACHE = 8,
    DYLD_CHAINED_PTR_ARM64E_USERLAND = 9,
    DYLD_CHAINED_PTR_ARM64E_USERLAND24 = 10,
    DYLD_CHAINED_PTR_X86_64_KERNEL_CACHE = 11,
    DYLD_CHAINED_PTR_ARM64E_AUTH_IMPORTS = 12
}
