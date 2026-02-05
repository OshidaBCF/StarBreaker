using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using StarBreaker.Common;
using StarBreaker.CryXmlB;
using StarBreaker.DataCore;
using StarBreaker.Dds;
using StarBreaker.P4k;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ZstdSharp;

namespace StarBreaker.Cli;

[Command("diff", Description = "Dumps game information into plain text files for comparison")]
public class DiffCommand : ICommand
{
    [CommandOption("game", 'g', Description = "Path to the game folder", EnvironmentVariable = "GAME_FOLDER")]
    public required string GameFolder { get; init; }

    [CommandOption("output", 'o', Description = "Path to the output directory", EnvironmentVariable = "OUTPUT_FOLDER")]
    public required string OutputFolder { get; init; }

    string OutputDirectory = "";

    [CommandOption("use-game-version-as-path", Description = "Use the game version as the output name (eg:\"sc-alpha-4.4.0-10633661\") and use the output path as the parent folder", EnvironmentVariable = "USE_VERSION_AS_PATH")]
    public bool UseGameVersionAsPath { get; init; }

    [CommandOption("keep", 'k', Description = "Keep old files in the output directory", EnvironmentVariable = "KEEP_OLD")]
    public bool KeepOld { get; init; }

    [CommandOption("format", 'f', Description = "Output format", EnvironmentVariable = "TEXT_FORMAT")]
    public string TextFormat { get; init; } = "xml";

    [CommandOption("extract-dds", Description = "Extract DDS", EnvironmentVariable = "EXTRACT_DDS")]
    public bool ExtractDds { get; init; }

    [CommandOption("convert-dds", Description = "Convert DDS files to PNG", EnvironmentVariable = "CONVERT_DDS")]
    public bool ConvertDDSToPNG { get; init; }

    [CommandOption("use-parallel-convertion", Description = "Extract/Convert DDS files using paralellism", EnvironmentVariable = "USE_PARALLEL")]
    public bool UseParallelConvertion { get; init; }

    [CommandOption("save-archive", Description = "Create Compressed Archive of the game exe and DataCore", EnvironmentVariable = "SAVE_ARCHIVE")]
    public bool SaveCompressedArchive { get; init; }

    [CommandOption("diff-against", Description = "Path to previous P4K file or output directory to compare against for extracting only new/modified DDS files", EnvironmentVariable = "DIFF_AGAINST")]
    public string? DiffAgainst { get; init; }

    [CommandOption("replace-tags", Description = "Replace tags in Datacore with their actual value from TagDatabase (only in JSON text format)", EnvironmentVariable = "REPLACE_TAGS")]
    public string? ReplaceTagsInDatacore { get; init; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        //
        if (UseGameVersionAsPath)
        {
            var build_manifestPath = Path.Combine(GameFolder, "build_manifest.id");
            using var stream = File.OpenRead(build_manifestPath);
            var jsonObject = JsonNode.Parse(stream);
            if (jsonObject != null)
            {
                // use argument path as "parent" path
                OutputDirectory = Path.Combine(OutputFolder, $"{jsonObject["Data"]["Branch"]}-{jsonObject["Data"]["RequestedP4ChangeNum"]}");
            }
        }
        else
        {
            // Set general output director as input
            OutputDirectory = OutputFolder;
        }

        var swTotal = Stopwatch.StartNew();
        var sw = Stopwatch.StartNew();
        if (Path.Exists(OutputDirectory))
        {
            var continueQuestionUnanswered = true;
            var continueExtraction = false;
            await console.Output.WriteLineAsync($"Version {OutputDirectory.Split("\\").Last()} already exist");
            await console.Output.WriteLineAsync($"Do you wish to continue ? Y/N");
            while (continueQuestionUnanswered)
            {
                var input = await console.Input.ReadLineAsync();
                if (!input.Equals("y", StringComparison.CurrentCultureIgnoreCase) && !input.Equals("n", StringComparison.CurrentCultureIgnoreCase))
                {
                    console.CursorLeft = 0;
                    await console.Output.WriteLineAsync($"                                                                  ");
                    console.CursorLeft = 0;
                    console.CursorTop -= 2;
                    await console.Output.WriteLineAsync($"                                                                  ");
                    console.CursorLeft = 0;
                    console.CursorTop -= 1;
                }
                else 
                {
                    continueExtraction = input.Equals("y", StringComparison.CurrentCultureIgnoreCase);
                    continueQuestionUnanswered = false;
                }

            }
            if (!continueExtraction) return;
            console.Clear();
        }
        if (!KeepOld && Path.Exists(OutputDirectory))
        {
            await console.Output.WriteLineAsync("Deleting old files...");
            List<string> deleteFolder =
            [
                Path.Combine(OutputDirectory, "DataCore"),
                Path.Combine(OutputDirectory, "DataCoreTypes"),
                Path.Combine(OutputDirectory, "DataCoreEnums"),
                Path.Combine(OutputDirectory, "P4k"),
                Path.Combine(OutputDirectory, "P4kContents"),
                Path.Combine(OutputDirectory, "Localization"),
                Path.Combine(OutputDirectory, "TagDatabase"),
                Path.Combine(OutputDirectory, "Protobuf"),
                Path.Combine(OutputDirectory, "DDS_Files"),
            ];

            List<string> deleteFile =
            [
                Path.Combine(OutputDirectory, "build_manifest.json"),
                Path.Combine(OutputDirectory, "DataCore.dcb.zst"),
                Path.Combine(OutputDirectory, "StarCitizen.exe.zst"),
            ];

            foreach (var folder in deleteFolder.Where(Directory.Exists))
                Directory.Delete(folder, true);

            foreach (var file in deleteFile.Where(File.Exists))
                File.Delete(file);

            await console.Output.WriteLineAsync("Old files deleted in " + sw.Elapsed);
            sw.Restart();
        }

        // Hide output from subcommands
        var fakeConsole = new FakeConsole();

        var p4kFile = Path.Combine(GameFolder, "Data.p4k");
        var exeFile = Path.Combine(GameFolder, "Bin64", "StarCitizen.exe");

        await console.Output.WriteLineAsync("Extracting TagDatabase...");
        await ExtractTagDatabase(p4kFile, fakeConsole);
        await console.Output.WriteLineAsync("TagDatabase extracted in " + sw.Elapsed);
        sw.Restart();
        
        await console.Output.WriteLineAsync("Extracting DataCore...");
        var dcbExtract = new DataCoreExtractCommand
        {
            P4kFile = p4kFile,
            OutputDirectory = Path.Combine(OutputDirectory, "DataCore"),
            OutputFolderTypes = Path.Combine(OutputDirectory, "DataCoreTypes"),
            OutputFolderEnums = Path.Combine(OutputDirectory, "DataCoreEnums"),
            TextFormat = TextFormat
        };
        await dcbExtract.ExecuteAsync(fakeConsole);
        await console.Output.WriteLineAsync("DataCore extracted in " + sw.Elapsed);
        sw.Restart();

        if (ExtractDds)
        {
            await console.Output.WriteLineAsync("Extracting DDS files...");
            await ExtractDdsFiles(p4kFile, console, DiffAgainst);
            await console.Output.WriteLineAsync("DDS files extracted in " + sw.Elapsed);
            sw.Restart();
        }

        await console.Output.WriteLineAsync("Extracting Localization...");
        await ExtractLocalization(p4kFile, console);
        await console.Output.WriteLineAsync("Localization extracted in " + sw.Elapsed);
        sw.Restart();


        await console.Output.WriteLineAsync("Extracting P4k...");
        var dumpP4k = new DumpP4kCommand
        {
            P4kFile = p4kFile,
            OutputDirectory = Path.Combine(OutputDirectory, "P4k"),
            TextFormat = TextFormat,
        };
        await dumpP4k.ExecuteAsync(fakeConsole);
        await console.Output.WriteLineAsync("P4k extracted in " + sw.Elapsed);
        sw.Restart();

        await console.Output.WriteLineAsync("Extracting P4K Content files...");
        await ExtractP4kXmlFiles(p4kFile, console);
        await console.Output.WriteLineAsync("P4K Content files extracted in " + sw.Elapsed);
        sw.Restart();
        
        await console.Output.WriteLineAsync("Extracting Protobuf definitions...");
        var extractProtobufs = new ExtractProtobufsCommand
        {
            Input = exeFile,
            Output = Path.Combine(OutputDirectory, "Protobuf")
        };
        await extractProtobufs.ExecuteAsync(fakeConsole);
        await console.Output.WriteLineAsync("Protobuf definitions extracted in " + sw.Elapsed);
        sw.Restart();

        await console.Output.WriteLineAsync("Extracting Protobuf descriptor set...");
        var extractDescriptor = new ExtractDescriptorSetCommand
        {
            Input = exeFile,
            Output = Path.Combine(OutputDirectory, "Protobuf", "descriptor_set.bin")
        };
        await extractDescriptor.ExecuteAsync(fakeConsole);
        await console.Output.WriteLineAsync("Protobuf descriptor set extracted in " + sw.Elapsed);
        sw.Restart();

        if (SaveCompressedArchive)
        {
            await console.Output.WriteLineAsync("Creating Compressed archives...");
            await ExtractDataCoreIntoZip(p4kFile, Path.Combine(OutputDirectory, "DataCore.dcb.zst"));
            await ExtractExecutableIntoZip(exeFile, Path.Combine(OutputDirectory, "StarCitizen.exe.zst"));
            await console.Output.WriteLineAsync("Compressed archives created in " + sw.Elapsed);
            sw.Restart();
        }

        var buildManifestSource = Path.Combine(GameFolder, "build_manifest.id");
        var buildManifestTarget = Path.Combine(OutputDirectory, "build_manifest.json");
        if (File.Exists(buildManifestSource))
        {
            File.Copy(buildManifestSource, buildManifestTarget, true);
        }
        
        await console.Output.WriteLineAsync($"Done in {swTotal.Elapsed}");
    }

    private async Task ExtractLocalization(string p4kFile, IConsole console)
    {
        var p4kFileSystem = new P4kFileSystem(P4kFile.FromFile(p4kFile));
        var outputDir = Path.Combine(OutputDirectory, "Localization");

        string[] localizationPaths = [
            "Data/Localization/english/global.ini",
            "Data\\Localization\\english\\global.ini"
        ];

        foreach (var path in localizationPaths)
        {
            if (p4kFileSystem.FileExists(path))
            {
                using var stream = p4kFileSystem.OpenRead(path);
                var outputPath = Path.Combine(outputDir, Path.GetFileName(path));
                Directory.CreateDirectory(outputDir);
                
                await using var outputFile = File.Create(outputPath);
                await stream.CopyToAsync(outputFile);
                
                await console.Output.WriteLineAsync($"Extracted: {Path.GetFileName(path)}");
                return;
            }
        }

        await console.Output.WriteLineAsync("Localization file not found.");
    }

    private async Task ExtractTagDatabase(string p4kFile, IConsole console)
    {
        var p4k = P4kFile.FromFile(p4kFile);
        var outputDir = Path.Combine(OutputDirectory, "TagDatabase");

        var tagDbEntry = p4k.Entries.FirstOrDefault(e => 
            e.Name.Contains("TagDatabase.TagDatabase.xml", StringComparison.OrdinalIgnoreCase));

        if (tagDbEntry == null)
        {
            await console.Output.WriteLineAsync("TagDatabase not found in P4K.");
            return;
        }

        using var entryStream = p4k.OpenStream(tagDbEntry);
        var ms = new MemoryStream();
        entryStream.CopyTo(ms);

        ms.Position = 0;
        var entryPath = Path.Combine(outputDir, tagDbEntry.RelativeOutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
        if (CryXml.IsCryXmlB(ms))
        {
            ms.Position = 0;
            if (CryXml.TryOpen(ms, out var cryXml))
            {
                File.WriteAllText(entryPath, cryXml.ToString());
                await console.Output.WriteLineAsync("TagDatabase extracted and converted from CryXML.");
            }
            else
            {
                using var fs = File.Create(entryPath);
                ms.Position = 0;
                ms.CopyTo(fs);
                await console.Output.WriteLineAsync("TagDatabase extracted (binary format).");
            }
        }
        else
        {
            using var fs = File.Create(entryPath);
            ms.Position = 0;
            ms.CopyTo(fs);
            await console.Output.WriteLineAsync("TagDatabase extracted.");
        }
    }

    private async Task ExtractP4kXmlFiles(string p4kFile, IConsole console)
    {
        var p4k = P4kFile.FromFile(p4kFile);
        var outputDir = Path.Combine(OutputDirectory, "P4kContents");
        Directory.CreateDirectory(outputDir);

        var mainXmlEntries = p4k.Entries
            .Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var socpakEntries = p4k.Entries
            .Where(e => (e.Name.EndsWith(".socpak", StringComparison.OrdinalIgnoreCase) || 
                         e.Name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) &&
                        !e.Name.Contains("shadercache_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var socpakXmlEntries = new List<(P4kEntry entry, string socpakPath, P4kFile socpak)>();
        foreach (var socpakEntry in socpakEntries)
        {
            try
            {
                var socpak = P4kFile.FromP4kEntry(p4k, socpakEntry);
                var xmlsInSocpak = socpak.Entries
                    .Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .Select(e => (entry: e, socpakPath: socpakEntry.RelativeOutputPath, socpak: socpak))
                    .ToList();
                
                socpakXmlEntries.AddRange(xmlsInSocpak);
            }
            catch
            {
                // Skip problematic SOCPAK files
            }
        }

        var totalFiles = mainXmlEntries.Count + socpakXmlEntries.Count;
        
        if (totalFiles == 0)
        {
            await console.Output.WriteLineAsync("No XML files found in P4K or SOCPAKs.");
            return;
        }

        foreach (var entry in mainXmlEntries)
        {
            ExtractXmlEntry(p4k, entry, outputDir, entry.RelativeOutputPath);
        }

        foreach (var (entry, socpakPath, socpak) in socpakXmlEntries)
        {
            var socpakDir = Path.GetDirectoryName(socpakPath) ?? "";
            var socpakName = Path.GetFileNameWithoutExtension(socpakPath);
            var fullOutputPath = Path.Combine(socpakDir, socpakName, entry.RelativeOutputPath);
            
            ExtractXmlEntry(socpak, entry, outputDir, fullOutputPath);
        }

        await console.Output.WriteLineAsync($"Extracted {mainXmlEntries.Count} XML files from P4K and {socpakXmlEntries.Count} from SOCPAKs.");
    }

    private static void ExtractXmlEntry(P4kFile p4k, P4kEntry entry, string baseOutputDir, string relativePath)
    {
        using var entryStream = p4k.OpenStream(entry);
        var ms = new MemoryStream();
        entryStream.CopyTo(ms);
        ms.Position = 0;

        var entryPath = Path.Combine(baseOutputDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);

        if (CryXml.IsCryXmlB(ms))
        {
            ms.Position = 0;
            if (CryXml.TryOpen(ms, out var cryXml))
            {
                File.WriteAllText(entryPath, cryXml.ToString());
            }
            else
            {
                using var fs = File.Create(entryPath);
                ms.Position = 0;
                ms.CopyTo(fs);
            }
        }
        else
        {
            using var fs = File.Create(entryPath);
            ms.Position = 0;
            ms.CopyTo(fs);
        }
    }

    private static async Task ExtractDataCoreIntoZip(string p4kFile, string zipPath)
    {
        var p4k = new P4kFileSystem(P4kFile.FromFile(p4kFile));
        Stream? input = null;
        foreach (var file in DataCoreUtils.KnownPaths)
        {
            if (!p4k.FileExists(file)) continue;
            input = p4k.OpenRead(file);
            break;
        }

        if (input == null)
            throw new InvalidOperationException("DataCore not found.");

        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        await using var output = File.OpenWrite(zipPath);
        await using var compressionStream = new CompressionStream(output, leaveOpen: false);
        await input.CopyToAsync(compressionStream);
    }

    private static async Task ExtractExecutableIntoZip(string exeFile, string zipPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        await using var input = File.OpenRead(exeFile);
        await using var output = File.OpenWrite(zipPath);

        await using var compressionStream = new CompressionStream(output, leaveOpen: false);
        await input.CopyToAsync(compressionStream);
    }

    private async Task ExtractDdsFiles(string p4kFile, IConsole console, string? diffAgainst)
    {
        try
        {
            var p4k = P4kFile.FromFile(p4kFile);
            var outputDir = Path.Combine(OutputDirectory, "DDS_Files");
            Directory.CreateDirectory(outputDir);

            var p4kFileSystem = new P4kFileSystem(p4k);

            IEnumerable<P4kEntry> ddsEntriesToExtract;

            if (!string.IsNullOrWhiteSpace(diffAgainst))
            {
                // Extract only new/modified DDS files by comparing with previous version
                var previousP4kPath = GetPreviousP4kPath(diffAgainst);
                if (File.Exists(previousP4kPath))
                {
                    var previousP4k = P4kFile.FromFile(previousP4kPath);
                    var comparisonRoot = P4kComparison.Compare(previousP4k, p4k);
                    
                    var allFiles = comparisonRoot.GetAllFiles().ToList();
                    var ddsFiles = allFiles
                        .Where(f => f.Status == P4kComparisonStatus.Added || f.Status == P4kComparisonStatus.Modified)
                        .Where(f => Path.GetFileName(f.FullPath).Contains(".dds", StringComparison.OrdinalIgnoreCase))
                        .Where(f => f.RightEntry != null)
                        .Where(f => !char.IsDigit(Path.GetFileName(f.FullPath)[^1])) // Filter out mipmap files
                        .Select(f => f.RightEntry!)
                        .ToList();
                    
                    ddsEntriesToExtract = ddsFiles;
                    await console.Output.WriteLineAsync($"Found {ddsFiles.Count} new/modified DDS files to extract.");
                }
                else
                {
                    await console.Output.WriteLineAsync($"Previous P4K not found at {previousP4kPath}. Extracting all DDS files.");
                    ddsEntriesToExtract = GetBaseDdsEntries(p4k);
                }
            }
            else
            {
                // Extract all DDS files if no comparison specified
                ddsEntriesToExtract = GetBaseDdsEntries(p4k);
            }

            //ddsEntriesToExtract = ddsEntriesToExtract.Take(1000);

            var totalCount = ddsEntriesToExtract.Count();
            var progressPercentage = totalCount / 10;

            var processedCount = 0;
            var failedCount = 0;
            if (ConvertDDSToPNG)
            {
                if (UseParallelConvertion)
                {
                    Parallel.ForEach(ddsEntriesToExtract, entry =>
                    {
                        try
                        {
                            using var ms = DdsFile.MergeToStream(entry.Name, p4kFileSystem);
                            var ddsBytes = ms.ToArray();
                            using var pngStream = DdsFile.ConvertToPng(ddsBytes, true, true);
                            var pngBytes = pngStream.ToArray();

                            var relPath = entry.Name.Substring(0, entry.Name.LastIndexOf('\\'));
                            var pngOutputFolderPath = Path.Combine(outputDir, relPath);
                            var pngOutputPath = Path.Combine(outputDir, entry.Name).Replace(".dds", ".png");

                            Directory.CreateDirectory(pngOutputFolderPath);
                            File.WriteAllBytes(pngOutputPath, pngBytes);
                            var safeProcessedCount = Interlocked.Increment(ref processedCount);

                            if (safeProcessedCount % progressPercentage == 0)
                            {
                                console.Output.Write($"Progress: {safeProcessedCount} / {totalCount} {safeProcessedCount / progressPercentage * 10}%\r");
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failedCount);
                            console.Output.WriteLine($"Failed to save DDS as PNG: {entry.Name} - {ex.Message}");
                        }
                    });
                }
                else
                {
                    foreach (var entry in ddsEntriesToExtract)
                    {
                        try
                        {
                            using var ms = DdsFile.MergeToStream(entry.Name, p4kFileSystem);
                            var ddsBytes = ms.ToArray();
                            using var pngStream = DdsFile.ConvertToPng(ddsBytes, true, true);
                            var pngBytes = pngStream.ToArray();

                            var relPath = entry.Name.Substring(0, entry.Name.LastIndexOf('\\'));
                            var pngOutputFolderPath = Path.Combine(outputDir, relPath);
                            var pngOutputPath = Path.Combine(outputDir, entry.Name).Replace(".dds", ".png");

                            Directory.CreateDirectory(pngOutputFolderPath);
                            File.WriteAllBytes(pngOutputPath, pngBytes);
                            processedCount++;

                            if (processedCount % progressPercentage == 0)
                            {
                                console.Output.Write($"Progress: {processedCount} / {totalCount} {processedCount / progressPercentage * 10}%\r");
                            }
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            console.Output.WriteLine($"Failed to save DDS as PNG: {entry.Name} - {ex.Message}");
                        }
                    }
                }
                console.Output.Write($"\r");
                await console.Output.WriteLineAsync($"Extracted {processedCount} PNG files ({failedCount} failed).");
            }
            else
            {
                if (UseParallelConvertion)
                {
                    Parallel.ForEach(ddsEntriesToExtract,entry =>
                    {
                        try
                        {
                            using var ms = DdsFile.MergeToStream(entry.Name, p4kFileSystem);
                            var ddsBytes = ms.ToArray();
                            var relPath = entry.Name.Substring(0, entry.Name.LastIndexOf('\\'));

                            var ddsOutputFolderPath = Path.Combine(outputDir, relPath);
                            var ddsOutputPath = Path.Combine(outputDir, entry.Name);

                            Directory.CreateDirectory(ddsOutputFolderPath);
                            File.WriteAllBytes(ddsOutputPath, ddsBytes);
                            var safeProcessedCount = Interlocked.Increment(ref processedCount);

                            if (safeProcessedCount % progressPercentage == 0)
                            {
                                console.Output.Write($"Progress: {safeProcessedCount} / {totalCount} {safeProcessedCount / progressPercentage * 10}%\r");
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failedCount);
                            console.Output.WriteLine($"Failed to extract DDS: {entry.Name} - {ex.Message}");
                        }
                    });
                }
                else
                {
                    foreach (var entry in ddsEntriesToExtract)
                    {
                        try
                        {
                            using var ms = DdsFile.MergeToStream(entry.Name, p4kFileSystem);
                            var ddsBytes = ms.ToArray();
                            var relPath = entry.Name.Substring(0, entry.Name.LastIndexOf('\\'));

                            var ddsOutputFolderPath = Path.Combine(outputDir, relPath);
                            var ddsOutputPath = Path.Combine(outputDir, entry.Name);

                            Directory.CreateDirectory(ddsOutputFolderPath);
                            File.WriteAllBytes(ddsOutputPath, ddsBytes);
                            processedCount++;
                            if (processedCount % progressPercentage == 0)
                            {
                                console.Output.WriteLine($"Progress: {processedCount} / {totalCount} {processedCount / progressPercentage * 10}%\r");
                            }
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            console.Output.WriteLine($"Failed to extract DDS: {entry.Name} - {ex.Message}");
                        }
                    }
                }
                console.Output.Write($"\r");
                await console.Output.WriteLineAsync($"Extracted {processedCount} DDS files ({failedCount} failed).");
            }
        }
        catch (Exception ex)
        {
            await console.Output.WriteLineAsync($"Error extracting DDS files: {ex.Message}");
        }
    }

    private static List<P4kEntry> GetBaseDdsEntries(P4kFile p4k)
    {
        /* return only dds and dds.a but not .ddna files (idk why because ddna files are written as _ddna.dds)
        return p4k.Entries
            .Where(e => e.Name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || 
                       e.Name.EndsWith(".dds.a", StringComparison.OrdinalIgnoreCase)) // Shouldn't be present as dds.a aren't processable
            .Where(e => !e.Name.EndsWith(".ddna.dds", StringComparison.OrdinalIgnoreCase) && // Doesn't filter anything as there is no .ddna.dds files, only _ddna.dds
                       !e.Name.EndsWith(".ddna.dds.n", StringComparison.OrdinalIgnoreCase)) // Doesn't filter anything as file ends with s or a, not n
            .Where(e => !char.IsDigit(e.Name[^1])) // Doesn't filter anything as file ends with s or a, not a number
            .ToList();
        */

                            // Only select .dds files, ignoring .dds.a or .dds.n
                            return p4k.Entries
            .Where(e => e.Name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }


    private static string GetPreviousP4kPath(string diffAgainst)
    {
        // If it's a file path, use it directly
        if (File.Exists(diffAgainst))
        {
            return diffAgainst;
        }
        
        // If it's a directory, check if it contains a previous diff output
        if (Directory.Exists(diffAgainst))
        {
            // First check for a .p4k file in the directory
            var p4kFile = Directory.GetFiles(diffAgainst, "*.p4k", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (p4kFile != null)
            {
                return p4kFile;
            }
            
            // Try to find P4k dump folder with the old structure
            var p4kDumpDir = Path.Combine(diffAgainst, "P4k");
            if (Directory.Exists(p4kDumpDir))
            {
                // Look for the latest dump file in the P4k directory
                var latestDump = Directory.GetFiles(p4kDumpDir, "*.p4k", SearchOption.AllDirectories)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .FirstOrDefault();
                if (latestDump != null)
                {
                    return latestDump;
                }
            }
        }
        
        return diffAgainst;
    }
}