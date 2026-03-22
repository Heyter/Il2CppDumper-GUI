using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static Il2CppDumper.ArmUtils;

namespace Il2CppDumper;

public sealed class Macho64 : Il2Cpp
{
    private static readonly byte[] FeatureBytes1 = { 0x2, 0x0, 0x80, 0xD2 }; //MOV X2, #0
    private static readonly byte[] FeatureBytes2 = { 0x3, 0x0, 0x80, 0x52 }; //MOV W3, #0
    private readonly List<MachoSection64Bit> sections = new();
    private readonly ulong vmaddr;
    private Dictionary<ulong, ulong> chainedFixups = new();

    public Macho64(Stream stream) : base(stream)
    {
        Position += 16; //skip magic, cputype, cpusubtype, filetype
        var ncmds = ReadUInt32();
        Position += 12; //skip sizeofcmds, flags, reserved
        uint dyldChainedFixupOffset = 0;
        for (var i = 0; i < ncmds; i++)
        {
            var pos = Position;
            var cmd = ReadUInt32();
            var cmdsize = ReadUInt32();
            switch (cmd)
            {
                case 0x19: //LC_SEGMENT_64
                    // Preserve for more accurately locate LC_DYLD_CHAINED_FIXUPS which needs __LINKEDIT segment
                    var segment = new MachoSegment64Bit
                    {
                        name = Encoding.UTF8.GetString(ReadBytes(16)).TrimEnd('\0'),
                        vmaddr = ReadUInt64(),
                        vmsize = ReadUInt64(),
                        fileoff = ReadUInt64(),
                        filesize = ReadUInt64()
                    };
                    if (segment.name == "__TEXT") //__PAGEZERO
                    {
                        vmaddr = segment.vmaddr;
                    }
                    Position += 8; //skip maxprot, initprot
                    var nsects = ReadUInt32();
                    Position += 4; //skip flags
                    for (var j = 0; j < nsects; j++)
                    {
                        var section = new MachoSection64Bit();
                        sections.Add(section);
                        section.sectname = Encoding.UTF8.GetString(ReadBytes(16)).TrimEnd('\0');
                        Position += 16; //skip segname
                        section.addr = ReadUInt64();
                        section.size = ReadUInt64();
                        section.offset = ReadUInt32();
                        Position += 12; //skip align, reloff, nreloc
                        section.flags = ReadUInt32();
                        Position += 12; //skip reserved1, reserved2, reserved3
                    }

                    break;
                case 0x2C: //LC_ENCRYPTION_INFO_64
                    Position += 8;
                    var cryptID = ReadUInt32();
                    if (cryptID != 0)
                    {
                        Console.WriteLine("ERROR: This Mach-O executable is encrypted and cannot be processed.");
                    }

                    break;
                case 0x80000034: // LC_DYLD_CHAINED_FIXUPS
                    dyldChainedFixupOffset = ReadUInt32();
                    var dyldChainedFixupSize = ReadUInt32();
                    break;
            }

            Position = pos + cmdsize; //skip
        }

        if (dyldChainedFixupOffset != 0)
        {
            ProcessChainedFixups(dyldChainedFixupOffset);
        }
    }

    private void ProcessChainedFixups(ulong fixupDataOffset)
    {
        Position = fixupDataOffset;
        var header = new dyld_chained_fixups_header
        {
            fixups_version = ReadUInt32(),
            starts_offset = ReadUInt32(),
            imports_offset = ReadUInt32(),
            symbols_offset = ReadUInt32(),
            imports_count = ReadUInt32(),
            symbols_format = ReadUInt32(),
            imports_format = ReadUInt32()
        };

        if (header.starts_offset == 0) return;

        Position = fixupDataOffset + header.starts_offset;
        var seg_count = ReadUInt32();
        var seg_info_offsets = new uint[seg_count];
        for (int i = 0; i < seg_count; i++)
        {
            seg_info_offsets[i] = ReadUInt32();
        }

        for (int i = 0; i < seg_count; i++)
        {
            if (seg_info_offsets[i] == 0) continue;

            Position = fixupDataOffset + header.starts_offset + seg_info_offsets[i];
            var seg_info = new dyld_chained_starts_in_segment
            {
                size = ReadUInt32(),
                page_size = ReadUInt16(),
                pointer_format = ReadUInt16(),
                segment_offset = ReadUInt64(),
                max_valid_pointer = ReadUInt32(),
                page_count = ReadUInt16()
            };

            var page_starts = new ushort[seg_info.page_count];
            for (int j = 0; j < seg_info.page_count; j++)
            {
                page_starts[j] = ReadUInt16();
            }

            for (int j = 0; j < seg_info.page_count; j++)
            {
                ushort offsetInPage = page_starts[j];
                if (offsetInPage == 0xFFFF /* DYLD_CHAINED_PTR_START_NONE */) continue;

                ulong chainAddress = seg_info.segment_offset + (ulong)j * seg_info.page_size + offsetInPage;
                bool chainEnd = false;

                while (!chainEnd)
                {
                    ulong rva = chainAddress;
                    var fileOffset = MapVATR(rva + vmaddr);
                    if (fileOffset == 0) break;

                    Position = fileOffset;
                    ulong bindData = ReadUInt64();

                    ulong next = 0;
                    ulong target = 0;
                    bool isBind = false;

                    // 根据指针格式解析
                    switch ((DYLD_CHAINED_PTR_FORMAT)seg_info.pointer_format)
                    {
                        case DYLD_CHAINED_PTR_FORMAT.DYLD_CHAINED_PTR_64:
                        case DYLD_CHAINED_PTR_FORMAT.DYLD_CHAINED_PTR_64_OFFSET:
                            isBind = ((bindData >> 62) & 1) == 1;
                            if (!isBind) // rebase
                            {
                                ulong targetRva = bindData & 0xFFFFFFFFF; // target is top 36 bits
                                target = targetRva + vmaddr;
                                ulong high8 = (bindData >> 36) & 0xFF; // high 8 bits
                                if (high8 != 0)
                                {
                                    // 处理高8位附加信息 (如果有)
                                }
                                next = (bindData >> 51) & 0x7FF; // 11 bits stride
                            }
                            else
                            {
                                // bind (外部符号导入)
                                next = (bindData >> 51) & 0x7FF;
                            }
                            break;
                        case DYLD_CHAINED_PTR_FORMAT.DYLD_CHAINED_PTR_ARM64E:
                        case DYLD_CHAINED_PTR_FORMAT.DYLD_CHAINED_PTR_ARM64E_USERLAND:
                        case DYLD_CHAINED_PTR_FORMAT.DYLD_CHAINED_PTR_ARM64E_USERLAND24:
                            isBind = ((bindData >> 62) & 1) == 1;
                            if (!isBind) // rebase
                            {
                                ulong targetRva = bindData & 0xFFFFFFFF; // 32位 RVA
                                target = targetRva + vmaddr;
                                next = (bindData >> 51) & 0x7FF;
                            }
                            else
                            {
                                next = (bindData >> 51) & 0x7FF;
                            }
                            break;
                        default:
                            chainEnd = true;
                            break;
                    }

                    if (!chainEnd && !isBind)
                    {
                        // 记录真实的偏移
                        chainedFixups[bindData] = target;
                    }

                    if (next == 0)
                    {
                        chainEnd = true;
                    }
                    else
                    {
                        chainAddress += next * 4; // next is a stride in uint32_t (4 bytes)
                    }
                }
            }
        }
    }

    public ulong MapPointer(ulong pointer)
    {
        if (chainedFixups.TryGetValue(pointer, out ulong targetAddr))
        {
            return targetAddr;
        }
        return pointer;
    }

    public override ulong MapVATR(ulong addr)
    {
        addr = MapPointer(addr);

        var section = sections.First(x => addr >= x.addr && addr <= x.addr + x.size);
        if (section == null)
        {
            return 0;
        }
        if (section.sectname == "__bss")
        {
            throw new Exception();
        }

        return addr - section.addr + section.offset;
    }

    public override ulong MapRTVA(ulong addr)
    {
        var section = sections.FirstOrDefault(x => addr >= x.offset && addr <= x.offset + x.size);
        if (section == null)
        {
            return 0;
        }

        if (section.sectname == "__bss")
        {
            throw new Exception();
        }

        return addr - section.offset + section.addr;
    }

    public override bool Search()
    {
        // Skip Search when LC_DYLD_CHAINED_FIXUPS as this kind of MachO doesn't have __mod_init_func section
        if (chainedFixups.Count > 0)
        {
            return false;
        }

        var codeRegistration = 0ul;
        var metadataRegistration = 0ul;
        if (Version < 23)
        {
            var __mod_init_func = sections.First(x => x.sectname == "__mod_init_func");
            var addrs = ReadClassArray<ulong>(__mod_init_func.offset, (long)__mod_init_func.size / 8);
            foreach (var i in addrs)
            {
                if (i > 0)
                {
                    var flag = false;
                    var subaddr = 0ul;
                    Position = MapVATR(i);
                    var buff = ReadBytes(4);
                    if (FeatureBytes1.SequenceEqual(buff))
                    {
                        buff = ReadBytes(4);
                        if (FeatureBytes2.SequenceEqual(buff))
                        {
                            Position += 8;
                            var inst = ReadBytes(4);
                            if (IsAdr(inst))
                            {
                                subaddr = DecodeAdr(i + 16, inst);
                                flag = true;
                            }
                        }
                    }
                    else
                    {
                        Position += 0xc;
                        buff = ReadBytes(4);
                        if (FeatureBytes2.SequenceEqual(buff))
                        {
                            buff = ReadBytes(4);
                            if (FeatureBytes1.SequenceEqual(buff))
                            {
                                Position -= 0x10;
                                var inst = ReadBytes(4);
                                if (IsAdr(inst))
                                {
                                    subaddr = DecodeAdr(i + 8, inst);
                                    flag = true;
                                }
                            }
                        }
                    }

                    if (flag)
                    {
                        var rsubaddr = MapVATR(subaddr);
                        Position = rsubaddr;
                        codeRegistration = DecodeAdrp(subaddr, ReadBytes(4));
                        codeRegistration += DecodeAdd(ReadBytes(4));
                        Position = rsubaddr + 8;
                        metadataRegistration = DecodeAdrp(subaddr + 8, ReadBytes(4));
                        metadataRegistration += DecodeAdd(ReadBytes(4));
                    }
                }
            }
        }

        if (Version == 23)
        {
            /* ADRP X0, unk
             * ADD X0, X0, unk
             * ADR X1, sub
             * NOP
             * MOV X2, #0
             * MOV W3, #0
             * B sub
             */
            var __mod_init_func = sections.First(x => x.sectname == "__mod_init_func");
            var addrs = ReadClassArray<ulong>(__mod_init_func.offset, (long)__mod_init_func.size / 8);
            foreach (var i in addrs)
            {
                if (i > 0)
                {
                    Position = MapVATR(i) + 16;
                    var buff = ReadBytes(4);
                    if (FeatureBytes1.SequenceEqual(buff))
                    {
                        buff = ReadBytes(4);
                        if (FeatureBytes2.SequenceEqual(buff))
                        {
                            Position -= 16;
                            var subaddr = DecodeAdr(i + 8, ReadBytes(4));
                            var rsubaddr = MapVATR(subaddr);
                            Position = rsubaddr;
                            codeRegistration = DecodeAdrp(subaddr, ReadBytes(4));
                            codeRegistration += DecodeAdd(ReadBytes(4));
                            Position = rsubaddr + 8;
                            metadataRegistration = DecodeAdrp(subaddr + 8, ReadBytes(4));
                            metadataRegistration += DecodeAdd(ReadBytes(4));
                        }
                    }
                }
            }
        }

        if (Version >= 24)
        {
            /* ADRP X0, unk
             * ADD X0, X0, unk
             * ADR X1, sub
             * NOP
             * MOV W3, #0
             * MOV X2, #0
             * B sub
             */
            var __mod_init_func = sections.First(x => x.sectname == "__mod_init_func");
            var addrs = ReadClassArray<ulong>(__mod_init_func.offset, (long)__mod_init_func.size / 8);
            foreach (var i in addrs)
            {
                if (i > 0)
                {
                    Position = MapVATR(i) + 16;
                    var buff = ReadBytes(4);
                    if (FeatureBytes2.SequenceEqual(buff))
                    {
                        buff = ReadBytes(4);
                        if (FeatureBytes1.SequenceEqual(buff))
                        {
                            Position -= 16;
                            var subaddr = DecodeAdr(i + 8, ReadBytes(4));
                            var rsubaddr = MapVATR(subaddr);
                            Position = rsubaddr;
                            codeRegistration = DecodeAdrp(subaddr, ReadBytes(4));
                            codeRegistration += DecodeAdd(ReadBytes(4));
                            Position = rsubaddr + 8;
                            metadataRegistration = DecodeAdrp(subaddr + 8, ReadBytes(4));
                            metadataRegistration += DecodeAdd(ReadBytes(4));
                        }
                    }
                }
            }
        }

        if (codeRegistration != 0 && metadataRegistration != 0)
        {
            Console.WriteLine("CodeRegistration : {0:x}", codeRegistration);
            Console.WriteLine("MetadataRegistration : {0:x}", metadataRegistration);
            Init(codeRegistration, metadataRegistration);
            return true;
        }

        return false;
    }

    public override bool PlusSearch(int methodCount, int typeDefinitionsCount, int imageCount)
    {
        var sectionHelper = GetSectionHelper(methodCount, typeDefinitionsCount, imageCount);
        var codeRegistration = sectionHelper.FindCodeRegistration();
        var metadataRegistration = sectionHelper.FindMetadataRegistration();
        return AutoPlusInit(codeRegistration, metadataRegistration);
    }

    public override bool SymbolSearch()
    {
        return false;
    }

    public override ulong GetRVA(ulong pointer)
    {
        return pointer - vmaddr;
    }

    public override SectionHelper GetSectionHelper(int methodCount, int typeDefinitionsCount, int imageCount)
    {
        var data = sections.Where(x => x.sectname == "__const" || x.sectname == "__cstring" || x.sectname == "__data")
            .ToArray();
        var code = sections.Where(x => x.flags == 0x80000400).ToArray();
        var bss = sections.Where(x => x.flags == 1u).ToArray();
        var sectionHelper = new SectionHelper(this, methodCount, typeDefinitionsCount, metadataUsagesCount, imageCount);
        sectionHelper.SetSection(SearchSectionType.Exec, code);
        sectionHelper.SetSection(SearchSectionType.Data, data);
        sectionHelper.SetSection(SearchSectionType.Bss, bss);
        return sectionHelper;
    }

    public override bool CheckDump()
    {
        return false;
    }

    public override ulong ReadUIntPtr()
    {
        var pointer = MapPointer(ReadUInt64());
        if (pointer > vmaddr + 0xFFFFFFFF)
        {
            var addr = Position;
            var section = sections.First(x => addr >= x.offset && addr <= x.offset + x.size);
            if (section.sectname == "__const" || section.sectname == "__data")
            {
                var rva = pointer - vmaddr;
                rva &= 0xFFFFFFFF;
                pointer = rva + vmaddr;
            }
        }

        return pointer;
    }
}
