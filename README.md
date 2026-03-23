# Il2CppDumper

A bundled Il2CppDumper build with a launcher, shared configuration, shared helper scripts, and separate inner 32-bit / 64-bit runtimes.

This project is designed to make common dumping workflows easier:

- run a single launcher from the archive root
- keep one shared `config.json`
- keep helper scripts in one shared `scripts/` folder
- keep separate internal runtimes for 32-bit and 64-bit targets
- support both GUI and CLI workflows
- auto-select the correct runtime for many Android APK use cases

## What's new in this fork
* Fully supports MachO64 load command `LC_DYLD_CHAINED_FIXUPS` for Apps required iOS 15 and above (refer to [LIEF](https://github.com/lief-project/LIEF))

## What this bundle adds

Compared with a plain single-executable layout, this bundle adds:

- **Root launcher**
  - `Il2CppDumper.exe` in the archive root
  - launches the correct inner dumper automatically

- **Shared configuration**
  - one root-level `config.json`
  - used by the launcher and inner dumpers

- **Shared helper scripts**
  - one root-level `scripts/` directory
  - avoids duplicating the same Python scripts in both x86 and x64 runtimes

- **Separated inner runtimes**
  - `bin32bit/`
  - `bin64bit/`

- **Architecture-aware launching**
  - automatic runtime selection in **Auto** mode
  - manual override for **32-bit** or **64-bit**

- **Friendlier workflow**
  - GUI launcher for interactive use
  - CLI launcher for scripted use

## Disclaimer

This fork updates Il2CppDumper to correctly support Metadata v38 and above (up to v39/Unity 6000.x).

- Custom Attribute Enums: Fixed parsing logic for 38+ attributes.
- Validated and tested against Metadata v39.

## Features

* Complete DLL restore (except code), can be used to extract `MonoBehaviour` and `MonoScript`
* Supports ELF, ELF64, Mach-O, PE, NSO and WASM format
* Supports Unity 5.3 - 2022.2
* Supports generate IDA, Ghidra and Binary Ninja scripts to help them better analyze il2cpp files
* Supports generate structures header file
* Supports Android memory dumped `libil2cpp.so` file to bypass protection
* Support bypassing simple PE protection

## Usage

Run `Il2CppDumper.exe` and choose the il2cpp executable file and `global-metadata.dat` file, then enter the information as prompted

The program will then generate all the output files in current working directory

### Command-line

```
Il2CppDumper.exe <executable-file> <global-metadata> <output-directory>
Il2CppDumper.exe --auto <input-file> <global-metadata.dat> <output-directory>
Il2CppDumper.exe --32 <input-file> <global-metadata.dat> <output-directory>
Il2CppDumper.exe --64 <input-file> <global-metadata.dat> <output-directory>

Il2CppDumper.exe --arch auto <input-file> <global-metadata.dat> <output-directory>
Il2CppDumper.exe --arch 32   <input-file> <global-metadata.dat> <output-directory>
Il2CppDumper.exe --arch 64   <input-file> <global-metadata.dat> <output-directory>
```

### Outputs

#### DummyDll

Folder, containing all restored dll files

Use [dnSpy](https://github.com/0xd4d/dnSpy), [ILSpy](https://github.com/icsharpcode/ILSpy) or other .Net decompiler tools to view

Can be used to extract Unity `MonoBehaviour` and `MonoScript`, for [UtinyRipper](https://github.com/mafaca/UtinyRipper), [UABE](https://7daystodie.com/forums/showthread.php?22675-Unity-Assets-Bundle-Extractor)

#### ida.py

For IDA

#### ida_with_struct.py

For IDA, read il2cpp.h file and apply structure information in IDA

#### il2cpp.h

structure information header file

#### ghidra.py

For Ghidra

#### Il2CppBinaryNinja

For BinaryNinja

#### ghidra_wasm.py

For Ghidra, work with [ghidra-wasm-plugin](https://github.com/nneonneo/ghidra-wasm-plugin)

#### script.json

For ida.py, ghidra.py and Il2CppBinaryNinja

#### stringliteral.json

Contains all stringLiteral information

### Configuration

All the configuration options are located in `config.json`

Available options:

* `DumpMethod`, `DumpField`, `DumpProperty`, `DumpAttribute`, `DumpFieldOffset`, `DumpMethodOffset`, `DumpTypeDefIndex`
  * Whether to output these information to dump.cs

* `GenerateDummyDll`, `GenerateScript`
  * Whether to generate these things

* `DummyDllAddToken`
  * Whether to add token in DummyDll

* `ForceIl2CppVersion`, `ForceVersion`
  * If `ForceIl2CppVersion` is `true`, the program will use the version number specified in `ForceVersion` to choose parser for il2cpp binaries (does not affect the choice of metadata parser). This may be useful on some older il2cpp version (e.g. the program may need to use v16 parser on il2cpp v20 (Android) binaries in order to work properly)

* `ForceDump`
  * Force files to be treated as dumped

* `NoRedirectedPointer`
  * Treat pointers in dumped files as unredirected, This option needs to be `true` for files dumped from some devices

## Common errors

#### `ERROR: Metadata file supplied is not valid metadata file.`  

Make sure you choose the correct file. Sometimes games may obfuscate this file for content protection purposes and so on. Deobfuscating of such files is beyond the scope of this program, so please **DO NOT** file an issue regarding to deobfuscating.

If your file is `libil2cpp.so` and you have a rooted Android phone, you can try my other project [Zygisk-Il2CppDumper](https://github.com/Perfare/Zygisk-Il2CppDumper), it can bypass this protection.

#### `ERROR: Can't use auto mode to process file, try manual mode.`

Please note that the executable file for the PC platform is `GameAssembly.dll` or `*Assembly.dll`

You can open a new issue and upload the file, I will try to solve.

#### `ERROR: This file may be protected.`

Il2CppDumper detected that the executable file has been protected, use `GameGuardian` to dump `libil2cpp.so` from the game memory, then use Il2CppDumper to load and follow the prompts, can bypass most protections.

If you have a rooted Android phone, you can try my other project [Zygisk-Il2CppDumper](https://github.com/Perfare/Zygisk-Il2CppDumper), it can bypass almost all protections.

## Archive layout

```text
Il2CppDumper.exe
config.json
scripts/
  ghidra.py
  ghidra_wasm.py
  ghidra_with_struct.py
  ida.py
  ida_py3.py
  ida_with_struct.py
  ida_with_struct_py3.py
  il2cpp_header_to_ghidra.py
bin32bit/
  Il2CppDumper.exe
bin64bit/
  Il2CppDumper.exe

## Credits

- Jumboperson - [Il2CppDumper](https://github.com/Jumboperson/Il2CppDumper)
