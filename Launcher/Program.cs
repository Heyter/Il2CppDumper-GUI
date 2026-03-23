using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

namespace Il2CppDumperLauncher;

internal enum LaunchArchMode
{
    Auto,
    Force32,
    Force64,
}

internal enum RuntimeBitness
{
    Bit32,
    Bit64,
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return;
        }

        if (args.Length == 1 && IsHelpSwitch(args[0]))
        {
            ShowHelp();
            return;
        }

        Environment.ExitCode = RunFromArgs(args);
    }

    internal static async Task<int> LaunchAsync(
        string inputPath,
        string metadataPath,
        string outputPath,
        LaunchArchMode mode,
        Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            log?.Invoke("ERROR: Specify a valid binary/APK path.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(metadataPath) || !File.Exists(metadataPath))
        {
            log?.Invoke("ERROR: Specify a valid global-metadata.dat path.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            log?.Invoke("ERROR: Specify an output folder.");
            return 1;
        }

        Directory.CreateDirectory(outputPath);

        PreparedInput? prepared = null;
        try
        {
            prepared = PrepareInput(inputPath, mode, log);
            var childDir = prepared.TargetBitness == RuntimeBitness.Bit64 ? "bin64bit" : "bin32bit";
            var childExe = Path.Combine(AppContext.BaseDirectory, childDir, "Il2CppDumper.exe");

            if (!File.Exists(childExe))
            {
                log?.Invoke($"ERROR: Inner launcher not found: {childExe}");
                return 1;
            }

            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(configPath))
            {
                log?.Invoke($"ERROR: config.json was not found next to the launcher: {configPath}");
                return 1;
            }

            log?.Invoke($"Selected runtime: {(prepared.TargetBitness == RuntimeBitness.Bit64 ? "64-bit" : "32-bit")}");
            log?.Invoke($"Input binary: {prepared.BinaryPath}");

            var startInfo = new ProcessStartInfo
            {
                FileName = childExe,
                WorkingDirectory = Path.GetDirectoryName(childExe)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            startInfo.Environment["IL2CPPDUMPER_CONFIG_PATH"] = configPath;
            startInfo.ArgumentList.Add(prepared.BinaryPath);
            startInfo.ArgumentList.Add(metadataPath);
            startInfo.ArgumentList.Add(outputPath);

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    log?.Invoke(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    log?.Invoke(e.Data);
                }
            };

            if (!process.Start())
            {
                log?.Invoke("ERROR: Failed to start the inner Il2CppDumper.");
                return 1;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            log?.Invoke(ex.ToString());
            return 1;
        }
        finally
        {
            prepared?.Dispose();
        }
    }

    private static int RunFromArgs(string[] args)
    {
        try
        {
            var mode = ParseArchMode(args, out var positional);
            if (positional.Count < 3)
            {
                ShowHelp();
                return 1;
            }

            return LaunchAsync(positional[0], positional[1], positional[2], mode, Console.WriteLine)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static List<string> ParseArchMode(string[] args, out LaunchArchMode mode)
    {
        mode = LaunchArchMode.Auto;
        var positional = new List<string>();

        foreach (var arg in args)
        {
            if (arg.Equals("--32", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/32", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-32", StringComparison.OrdinalIgnoreCase))
            {
                mode = LaunchArchMode.Force32;
                continue;
            }

            if (arg.Equals("--64", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/64", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-64", StringComparison.OrdinalIgnoreCase))
            {
                mode = LaunchArchMode.Force64;
                continue;
            }

            if (arg.StartsWith("--arch=", StringComparison.OrdinalIgnoreCase))
            {
                mode = ParseArchValue(arg.Substring("--arch=".Length));
                continue;
            }

            positional.Add(arg);
        }

        return positional;
    }

    private static LaunchArchMode ParseArchValue(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "32" or "x86" or "32bit" => LaunchArchMode.Force32,
            "64" or "x64" or "64bit" => LaunchArchMode.Force64,
            _ => LaunchArchMode.Auto,
        };
    }

    private static bool IsHelpSwitch(string arg)
    {
        return arg is "-h" or "--help" or "/?" or "/h";
    }

    private static void ShowHelp()
    {
        Console.WriteLine("usage: Il2CppDumper <binary-or-apk> <global-metadata.dat> <output-folder> [--arch=auto|32|64]");
        Console.WriteLine("If launched without arguments, a GUI window will open.");
        Console.WriteLine("APK auto mode: arm64-v8a/x86_64 => 64-bit, armeabi-v7a/x86 => 32-bit.");
    }

    private static PreparedInput PrepareInput(string inputPath, LaunchArchMode mode, Action<string>? log)
    {
        var extension = Path.GetExtension(inputPath);
        if (extension.Equals(".apk", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractFromApk(inputPath, mode, log);
        }

        var bitness = mode switch
        {
            LaunchArchMode.Force32 => RuntimeBitness.Bit32,
            LaunchArchMode.Force64 => RuntimeBitness.Bit64,
            _ => DetectBinaryBitness(inputPath),
        };

        log?.Invoke($"Detected input type: {DescribeInputType(inputPath)}");
        return new PreparedInput(inputPath, bitness, null);
    }

    private static string DescribeInputType(string inputPath)
    {
        using var fs = File.OpenRead(inputPath);
        using var br = new BinaryReader(fs);
        var magic = br.ReadUInt32();

        return magic switch
        {
            0x464C457F => "ELF",
            0x905A4D => "PE",
            0xCAFEBABE or 0xBEBAFECA or 0xFEEDFACE or 0xFEEDFACF => "Mach-O",
            _ => "binary",
        };
    }

    private static RuntimeBitness DetectBinaryBitness(string inputPath)
    {
        using var fs = File.OpenRead(inputPath);
        using var br = new BinaryReader(fs);
        var magic = br.ReadUInt32();

        return magic switch
        {
            0x464C457F => DetectElfBitness(fs, br),
            0x905A4D => DetectPeBitness(fs, br),
            0xCAFEBABE or 0xBEBAFECA => RuntimeBitness.Bit64,
            0xFEEDFACF => RuntimeBitness.Bit64,
            0xFEEDFACE => RuntimeBitness.Bit32,
            _ => throw new InvalidOperationException("Could not determine 32/64-bit automatically. Choose the mode manually."),
        };
    }

    private static RuntimeBitness DetectElfBitness(FileStream fs, BinaryReader br)
    {
        fs.Position = 4;
        var elfClass = br.ReadByte();
        return elfClass switch
        {
            1 => RuntimeBitness.Bit32,
            2 => RuntimeBitness.Bit64,
            _ => throw new InvalidOperationException("Unknown ELF class."),
        };
    }

    private static RuntimeBitness DetectPeBitness(FileStream fs, BinaryReader br)
    {
        fs.Position = 0x3C;
        var peOffset = br.ReadInt32();
        fs.Position = peOffset + 4;
        var machine = br.ReadUInt16();

        return machine switch
        {
            0x014c => RuntimeBitness.Bit32,
            0x8664 => RuntimeBitness.Bit64,
            _ => throw new InvalidOperationException($"Unknown PE machine: 0x{machine:X4}"),
        };
    }

    private static PreparedInput ExtractFromApk(string apkPath, LaunchArchMode mode, Action<string>? log)
    {
        using var stream = File.OpenRead(apkPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var candidates = new List<ApkLibCandidate>();
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.EndsWith("/libil2cpp.so", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryMapApkEntry(entry.FullName, out var bitness, out var abiName))
            {
                candidates.Add(new ApkLibCandidate(entry.FullName, bitness, abiName));
            }
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("No libil2cpp.so for a supported ABI was found in the APK.");
        }

        ApkLibCandidate selected = mode switch
        {
            LaunchArchMode.Force32 => candidates.FirstOrDefault(x => x.Bitness == RuntimeBitness.Bit32)
                                  ?? throw new InvalidOperationException("The APK does not contain a 32-bit libil2cpp.so."),
            LaunchArchMode.Force64 => candidates.FirstOrDefault(x => x.Bitness == RuntimeBitness.Bit64)
                                  ?? throw new InvalidOperationException("The APK does not contain a 64-bit libil2cpp.so."),
            _ => candidates.FirstOrDefault(x => x.Bitness == RuntimeBitness.Bit64)
                 ?? candidates.First(x => x.Bitness == RuntimeBitness.Bit32),
        };

        var tempRoot = Path.Combine(Path.GetTempPath(), "Il2CppDumperLauncher", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var extractedPath = Path.Combine(tempRoot, "libil2cpp.so");

        var selectedEntry = zip.GetEntry(selected.EntryPath)
            ?? throw new InvalidOperationException("Failed to open the selected libil2cpp.so inside the APK.");

        using (var entryStream = selectedEntry.Open())
        using (var output = File.Create(extractedPath))
        {
            entryStream.CopyTo(output);
        }

        log?.Invoke($"Detected APK ABI: {selected.AbiName}");
        log?.Invoke($"Extracted libil2cpp.so: {extractedPath}");

        return new PreparedInput(extractedPath, selected.Bitness, tempRoot);
    }

    private static bool TryMapApkEntry(string fullName, out RuntimeBitness bitness, out string abiName)
    {
        var normalized = fullName.Replace('\\', '/');

        if (normalized.Equals("lib/arm64-v8a/libil2cpp.so", StringComparison.OrdinalIgnoreCase))
        {
            bitness = RuntimeBitness.Bit64;
            abiName = "arm64-v8a";
            return true;
        }

        if (normalized.Equals("lib/armeabi-v7a/libil2cpp.so", StringComparison.OrdinalIgnoreCase))
        {
            bitness = RuntimeBitness.Bit32;
            abiName = "armeabi-v7a";
            return true;
        }

        if (normalized.Equals("lib/x86_64/libil2cpp.so", StringComparison.OrdinalIgnoreCase))
        {
            bitness = RuntimeBitness.Bit64;
            abiName = "x86_64";
            return true;
        }

        if (normalized.Equals("lib/x86/libil2cpp.so", StringComparison.OrdinalIgnoreCase))
        {
            bitness = RuntimeBitness.Bit32;
            abiName = "x86";
            return true;
        }

        bitness = default;
        abiName = string.Empty;
        return false;
    }

    private sealed class ApkLibCandidate
    {
        public ApkLibCandidate(string entryPath, RuntimeBitness bitness, string abiName)
        {
            EntryPath = entryPath;
            Bitness = bitness;
            AbiName = abiName;
        }

        public string EntryPath { get; }
        public RuntimeBitness Bitness { get; }
        public string AbiName { get; }
    }

    private sealed class PreparedInput : IDisposable
    {
        public PreparedInput(string binaryPath, RuntimeBitness targetBitness, string? tempDirectory)
        {
            BinaryPath = binaryPath;
            TargetBitness = targetBitness;
            TempDirectory = tempDirectory;
        }

        public string BinaryPath { get; }
        public RuntimeBitness TargetBitness { get; }
        public string? TempDirectory { get; }

        public void Dispose()
        {
            if (string.IsNullOrWhiteSpace(TempDirectory) || !Directory.Exists(TempDirectory))
            {
                return;
            }

            try
            {
                Directory.Delete(TempDirectory, true);
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }
}
