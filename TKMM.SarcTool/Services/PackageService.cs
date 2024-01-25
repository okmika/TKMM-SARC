using SarcLibrary;
using Spectre.Console;
using TKMM.SarcTool.Common;
using TKMM.SarcTool.Compression;
using TKMM.SarcTool.Special;

namespace TKMM.SarcTool.Services;

internal class PackageService {

    private readonly ConfigService configService;
    private readonly IHandlerManager handlerManager;

    private ConfigJson? config;
    private ZsCompression? compression;
    private ChecksumLookup? checksumLookup;
    private string[] versions = new string[0];
    private bool verboseOutput;
    
    private readonly string[] supportedExtensions = new[] {
        ".bars", ".bfarc", ".bkres", ".blarc", ".genvb", ".pack", ".ta",
        ".bars.zs", ".bfarc.zs", ".bkres.zs", ".blarc.zs", ".genvb.zs", ".pack.zs", ".ta.zs"
    };

    public PackageService(ConfigService configService, IHandlerManager handlerManager, IGlobals globals) {
        this.configService = configService;
        this.handlerManager = handlerManager;
        this.verboseOutput = globals.Verbose;
    }
    
    public int Execute(string outputPath, string modPath, string? configPath, string? checksumPath, string[] checkVersions) {
        if (String.IsNullOrWhiteSpace(configPath))
            configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "Totk");

        // Config path only expects the json
        configPath = Path.Combine(configPath, "config.json");
        
        if (String.IsNullOrWhiteSpace(checksumPath))
            checksumPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                        "Totk", "checksums.bin");

        if (!File.Exists(configPath)) {
            AnsiConsole.MarkupLineInterpolated($"[red]Could not find configuration: {configPath}\n[bold]Abort.[/][/]");
            return -1;
        }

        if (!File.Exists(checksumPath)) {
            AnsiConsole.MarkupLineInterpolated($"[red]Could not find checksum database: {checksumPath}\n[bold]Abort.[/][/]");
            return -1;
        }

        if (!Initialize(configPath, checksumPath))
            return -1;

        this.versions = checkVersions;
        InternalMakePackage(modPath, outputPath);

        AnsiConsole.MarkupLine("[green][bold]Packaging completed successfully.[/][/]");
        
        return 0;
    }

    private void InternalMakePackage(string modPath, string outputPath) {
        string[] filesInFolder = new string[0];

        AnsiConsole.Status()
                   .Spinner(Spinner.Known.Dots2)
                   .Start("Getting list of archives to check", _ => {
                       filesInFolder = Directory.GetFiles(modPath, "*", SearchOption.AllDirectories);
                   });

        AnsiConsole.MarkupLineInterpolated($"[bold]Packaging SARCs in {modPath} to {outputPath}[/]");

        AnsiConsole.Status()
                   .Spinner(Spinner.Known.Dots2)
                   .Start("Preparing", context => {
            
                        foreach (var filePath in filesInFolder.Where(file => supportedExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))) {
                            var pathRelativeToBase = Path.GetRelativePath(modPath, Path.GetDirectoryName(filePath)!);
                            var destinationPath = Path.Combine(outputPath, pathRelativeToBase);
                            if (!Directory.Exists(destinationPath))
                                Directory.CreateDirectory(destinationPath);

                            var outputFilePath = Path.Combine(destinationPath, Path.GetFileName(filePath));
                            
                            try {
                                context.Status($"Processing {filePath}");
                                var result = HandleArchive(filePath, pathRelativeToBase);

                                if (result.Length == 0) {
                                    AnsiConsole.MarkupLineInterpolated($"! [yellow]Omitting {modPath
                                    } because file is same as vanilla or no changes made[/]");
                                    continue;
                                }

                                // Copy to destination
                                if (File.Exists(outputFilePath)) {
                                    AnsiConsole.MarkupLineInterpolated($"! [yellow]Overwriting existing file in output: {outputFilePath}[/]");
                                    File.Delete(outputFilePath);
                                }

                                File.WriteAllBytes(outputFilePath, result.ToArray());

                                AnsiConsole.MarkupLineInterpolated($"» [green]Wrote {outputFilePath}[/]");
                            } catch (Exception exc) {
                                AnsiConsole.WriteException(exc, ExceptionFormats.ShortenEverything);
                                AnsiConsole.MarkupLineInterpolated($"X [red]Failed to package {filePath} - skipping[/]");
                                
                                if (File.Exists(outputFilePath)) {
                                    AnsiConsole.MarkupLineInterpolated($"! [yellow]Overwriting existing file in output: {outputFilePath}[/]");
                                    File.Delete(outputFilePath);
                                }

                                File.Copy(filePath, outputFilePath);
                            }
                        }

                        AnsiConsole.MarkupLineInterpolated($"[bold]Packaging flat files in {modPath} to {outputPath}[/]");
                        context.Status("Packaging flat files...");
                        PackageFilesInMod(modPath, outputPath, context);

                        AnsiConsole.MarkupLineInterpolated($"[bold]Creating GameDataList changelog[/]");
                        context.Status("Handling GDL files...");
                        PackageGameDataList(modPath, outputPath, context);
                   });
        

    }

    private Span<byte> HandleArchive(string archivePath, string pathRelativeToBase) {

        if (compression == null)
            throw new Exception("Compression not loaded");

        if (checksumLookup == null)
            throw new Exception("Checksums not loaded");

        var isCompressed = archivePath.EndsWith(".zs");
        var isPackFile = archivePath.Contains(".pack.");

        var fileContents = GetFileContents(archivePath, isCompressed, isPackFile);

        var archiveHash = Checksum.ComputeXxHash(fileContents);
        
        // Identical archives don't need to be processed or copied
        if (IsArchiveIdentical(archivePath, pathRelativeToBase, archiveHash))
            return Span<byte>.Empty;

        var sarc = Sarc.FromBinary(fileContents);
        var originalSarc = GetOriginalArchive(Path.GetFileName(archivePath), pathRelativeToBase, isCompressed, isPackFile);
        var isVanillaFile = IsVanillaFile(GetArchiveRelativeFilename(Path.GetFileName(archivePath), pathRelativeToBase));
        var toRemove = new List<string>();
        var atLeastOneReplacement = false;

        foreach (var entry in sarc) {
            var fileHash = Checksum.ComputeXxHash(entry.Value);
            
            // Remove identical items from the SARC
            if (IsFileIdentical(entry.Key, fileHash)) {
                toRemove.Add(entry.Key);
            } else if (originalSarc != null) {
                // Perform merge against the original file if we have an archive in the dump
                
                if (!originalSarc.ContainsKey(entry.Key))
                    continue;
                
                // Otherwise, reconcile with the handler
                var fileExtension = Path.GetExtension(entry.Key).Substring(1);
                var handler = handlerManager.GetHandlerInstance(fileExtension);

                if (handler == null) {
                    if (verboseOutput)
                        AnsiConsole.MarkupLineInterpolated($"! [yellow]{archivePath} {entry.Value}: No handler for type {
                            fileExtension}, overwriting[/]");

                    sarc[entry.Key] = entry.Value;
                    continue;
                }

                if (verboseOutput)
                    AnsiConsole.MarkupLineInterpolated($"- {archiveHash}: Reconciling {entry.Key}");

                var result = handler.Package(entry.Key, new List<MergeFile>() {
                    new MergeFile(1, entry.Value),
                    new MergeFile(0, originalSarc[entry.Key])
                });

                sarc[entry.Key] = result.ToArray();
                atLeastOneReplacement = true;
            }
        }
        
        // Nothing to remove? We can skip it
        if (toRemove.Count == 0 && !atLeastOneReplacement && isVanillaFile)
            return Span<byte>.Empty;
        
        // Removals
        foreach (var entry in toRemove)
            sarc.Remove(entry);

        Span<byte> outputContents;
        
        if (isCompressed) {
            using var memoryStream = new MemoryStream();
            sarc.Write(memoryStream);
            outputContents = compression.Compress(memoryStream.ToArray(),
                                                  isPackFile ? CompressionType.Pack : CompressionType.Common);
        } else {
            using var memoryStream = new MemoryStream();
            sarc.Write(memoryStream);
            outputContents = memoryStream.ToArray();
        }

        return outputContents;
    }

    private void PackageGameDataList(string modPath, string outputPath, StatusContext statusContext) {
        statusContext.Status("Packaging GameDataList files");

        var gdlFilePath = Path.Combine(modPath, "romfs", "GameData");
        var files = Directory.GetFiles(gdlFilePath);

        var gdlMerger = new GameDataListMerger();

        foreach (var gdlFile in files) {

            try {
                if (!Path.GetFileName(gdlFile).StartsWith("GameDataList.Product"))
                    continue;

                var isCompressed = gdlFile.EndsWith(".zs");

                var vanillaFilePath = Path.Combine(config!.GamePath!, "GameData", Path.GetFileName(gdlFile));

                if (!File.Exists(vanillaFilePath)) {
                    AnsiConsole.MarkupLineInterpolated($"X [red]Cannot find GDL vanilla file {gdlFile} - abort[/]");
                    throw new Exception("Failed to find vanilla GameDataList file");
                }

                var isVanillaCompressed = vanillaFilePath.EndsWith(".zs");

                var vanillaFile = GetFlatFileContents(vanillaFilePath, isVanillaCompressed);
                var modFile = GetFlatFileContents(gdlFile, isCompressed);

                var changelog = gdlMerger.Package(vanillaFile, modFile);

                if (changelog.Length == 0) {
                    AnsiConsole.MarkupLineInterpolated($"- No changes in {gdlFile}");
                    continue;
                }

                var targetFilePath = Path.Combine(outputPath, "romfs", "GameData", "GameDataList.gdlchangelog");

                if (!Directory.Exists(Path.GetDirectoryName(targetFilePath)))
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
                
                File.WriteAllBytes(targetFilePath, changelog.ToArray());
                
                AnsiConsole.MarkupLineInterpolated($"» [green]Created {targetFilePath}[/]");
                
                // Only need one change log
                break;
            } catch {
                AnsiConsole.MarkupLineInterpolated($"X [red]Failed to process GDL file {gdlFile} - abort[/]");
                throw;
            }

        }

    }

    private void PackageFilesInMod(string modPath, string outputPath, StatusContext statusContext) {
        var filesInModFolder =
            Directory.GetFiles(modPath, "*", SearchOption.AllDirectories);

        var supportedFlatExtensions = handlerManager.GetSupportedExtensions().ToHashSet();
        supportedFlatExtensions =
            supportedFlatExtensions.Concat(supportedFlatExtensions.Select(l => $"{l}.zs")).ToHashSet();

        var folderExclusions = new[] {"RSDB"};
        var extensionExclusions = new[] {".rstbl.byml", ".rstbl.byml.zs"};
        var prefixExclusions = new[] {"GameDataList.Product"};

        foreach (var filePath in filesInModFolder) {
            if (!supportedFlatExtensions.Any(l => filePath.EndsWith(l)))
                continue;

            if (folderExclusions.Any(l => filePath.Contains(Path.DirectorySeparatorChar + l + Path.DirectorySeparatorChar)))
                continue;

            if (extensionExclusions.Any(l => filePath.EndsWith(l)))
                continue;

            if (prefixExclusions.Any(l => Path.GetFileName(filePath).StartsWith(l)))
                continue;

            statusContext.Status($"Processing {filePath}");

            var baseRomfs = Path.Combine(modPath, "romfs");
            var pathRelativeToBase = Path.GetRelativePath(baseRomfs, Path.GetDirectoryName(filePath)!);

            PackageFile(filePath, modPath, pathRelativeToBase, outputPath);

            AnsiConsole.MarkupLineInterpolated($"» [green]Merged {filePath} into {pathRelativeToBase}[/]");
        }
    }

    private void PackageFile(string filePath, string modPath, string pathRelativeToBase, string outputPath) {
        var targetFilePath = Path.Combine(outputPath, "romfs", pathRelativeToBase, Path.GetFileName(filePath));
        var vanillaFilePath = Path.Combine(config!.GamePath!, pathRelativeToBase, Path.GetFileName(filePath));

        // If the vanilla file doesn't exist just copy it over and we're done
        if (!File.Exists(vanillaFilePath)) {
            File.Copy(filePath, targetFilePath);
            return;
        }
        
        // Create the target
        if (!Directory.Exists(Path.GetDirectoryName(targetFilePath)))
            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);

        // Otherwise try to reconcile and merge
        var isCompressed = filePath.EndsWith(".zs");

        if (isCompressed && !targetFilePath.EndsWith(".zs"))
            targetFilePath += ".zs";

        var vanillaFileContents = GetFlatFileContents(vanillaFilePath, isCompressed);
        var targetFileContents = GetFlatFileContents(filePath, isCompressed);

        var fileExtension = Path.GetExtension(filePath).Substring(1).ToLower();
        var handler = handlerManager.GetHandlerInstance(fileExtension);

        if (handler == null) {
            if (verboseOutput)
                AnsiConsole.MarkupLineInterpolated($"! [yellow]{modPath}: No handler for type {fileExtension}, overwriting {Path.GetFileName(filePath)} in {pathRelativeToBase}[/]");

            File.Copy(filePath, targetFilePath);
        } else {
            var relativeFilename = Path.Combine(pathRelativeToBase, Path.GetFileName(filePath));

            if (Path.DirectorySeparatorChar != '/')
                relativeFilename = relativeFilename.Replace(Path.DirectorySeparatorChar, '/');

            var result = handler.Package(relativeFilename, new List<MergeFile>() {
                new MergeFile(0, vanillaFileContents),
                new MergeFile(1, targetFileContents)
            });

            WriteFlatFileContents(targetFilePath, result, isCompressed);
        }
    }

    internal void WriteFileContents(string archivePath, Sarc sarc, bool isCompressed, bool isPackFile) {
        if (compression == null)
            throw new Exception("Compression not loaded");

        using var memoryStream = new MemoryStream();
        sarc.Write(memoryStream);

        if (isCompressed) {
            File.WriteAllBytes(archivePath,
                               compression.Compress(memoryStream.ToArray(),
                                                    isPackFile ? CompressionType.Pack : CompressionType.Common)
                                          .ToArray());
        } else {
            File.WriteAllBytes(archivePath, memoryStream.ToArray());
        }
    }

    internal Span<byte> GetFileContents(string archivePath, bool isCompressed, bool isPackFile) {
        if (compression == null)
            throw new Exception("Compression not loaded");

        Span<byte> sourceFileContents;
        if (isCompressed) {
            // Need to decompress the file first
            var compressedContents = File.ReadAllBytes(archivePath).AsSpan();
            sourceFileContents = compression.Decompress(compressedContents,
                                                        isPackFile ? CompressionType.Pack : CompressionType.Common);
        } else {
            sourceFileContents = File.ReadAllBytes(archivePath).AsSpan();
        }

        return sourceFileContents;
    }

    private Memory<byte> GetFlatFileContents(string filePath, bool isCompressed) {
        if (compression == null)
            throw new Exception("Compression not loaded");

        Span<byte> sourceFileContents;
        if (isCompressed) {
            // Need to decompress the file first
            var compressedContents = File.ReadAllBytes(filePath).AsSpan();
            sourceFileContents = compression.Decompress(compressedContents, CompressionType.Common);
        } else {
            sourceFileContents = File.ReadAllBytes(filePath).AsSpan();
        }

        return new Memory<byte>(sourceFileContents.ToArray());
    }

    private void WriteFlatFileContents(string filePath, ReadOnlyMemory<byte> contents, bool isCompressed) {
        if (compression == null)
            throw new Exception("Compression not loaded");

        if (isCompressed) {
            File.WriteAllBytes(filePath,
                               compression.Compress(contents.ToArray(), CompressionType.Common).ToArray());
        } else {
            File.WriteAllBytes(filePath, contents.ToArray());
        }
    }

    private bool IsFileIdentical(string filename, ulong fileHash) {
        var filenameHash = Checksum.ComputeXxHash(filename);
        
        if (checksumLookup!.GetChecksum(filenameHash) == fileHash)
            return true;

        foreach (var version in versions) {
            var versionHash = Checksum.ComputeXxHash(filename + "#" + version);
            if (checksumLookup!.GetChecksum(versionHash) == fileHash)
                return true;
        }

        return false;
    }

    private bool IsVanillaFile(string filename) {
        var filenameHash = Checksum.ComputeXxHash(filename);
        return checksumLookup!.GetChecksum(filenameHash) != null;
    }

    private bool IsArchiveIdentical(string archivePath, string pathRelativeToBase, ulong archiveHash) {
        var archiveRelativeFilename = GetArchiveRelativeFilename(archivePath, pathRelativeToBase);

        // Hash of the filename and contents
        var filenameHash = Checksum.ComputeXxHash(archiveRelativeFilename);

        if (checksumLookup!.GetChecksum(filenameHash) == archiveHash)
            return true;

        foreach (var version in versions) {
            var versionHash = Checksum.ComputeXxHash(archiveRelativeFilename + "#" + version);
            if (checksumLookup!.GetChecksum(versionHash) == archiveHash)
                return true;
        }

        return false;

    }

    private static string GetArchiveRelativeFilename(string archivePath, string pathRelativeToBase) {
        // Relative filename
        var pathSeparator = Path.DirectorySeparatorChar;
        var archiveRelativeFilename = Path.Combine(pathRelativeToBase, Path.GetFileName(archivePath));
        
        // Replace the path separator with the one used by the Switch
        if (pathSeparator != '/')
            archiveRelativeFilename = archiveRelativeFilename.Replace(pathSeparator, '/');

        // Get rid of any romfs portion of the path
        archiveRelativeFilename = archiveRelativeFilename.Replace("/romfs/", "")
                                                         .Replace("romfs/", "");

        // Get rid of any .zs on the end if the file was originally compressed
        if (archiveRelativeFilename.EndsWith(".zs"))
            archiveRelativeFilename = archiveRelativeFilename.Substring(0, archiveRelativeFilename.Length - 3);
        return archiveRelativeFilename;
    }

    private Sarc? GetOriginalArchive(string archiveFile, string pathRelativeToBase, bool isCompressed, bool isPackFile) {

        // Get rid of /romfs/ in the path
        var directoryChar = Path.DirectorySeparatorChar;
        pathRelativeToBase = pathRelativeToBase.Replace($"romfs{directoryChar}", "")
                                               .Replace($"{directoryChar}romfs{directoryChar}", "");
        
        var archivePath = Path.Combine(config!.GamePath!, pathRelativeToBase, archiveFile);

        if (!File.Exists(archivePath))
            return null;
        
        Span<byte> fileContents;
        if (isCompressed) {
            // Need to decompress the file first
            var compressedContents = File.ReadAllBytes(archivePath).AsSpan();
            fileContents = compression!.Decompress(compressedContents,
                                                  isPackFile ? CompressionType.Pack : CompressionType.Common);
        } else {
            fileContents = File.ReadAllBytes(archivePath).AsSpan();
        }

        return Sarc.FromBinary(fileContents);
    }

    private bool Initialize(string configPath, string checksumPath) {
        config = configService.GetConfig(configPath);

        if (String.IsNullOrWhiteSpace(config.GamePath)) {
            AnsiConsole.MarkupInterpolated(
                $"[red]Config file does not include path to a dump of the game. [bold]Abort.[/][/]");
            return false;
        }
        
        // Try to init compression
        var compressionPath = Path.Combine(this.config.GamePath, "Pack", "ZsDic.pack.zs");
        if (!File.Exists(compressionPath)) {
            AnsiConsole.MarkupInterpolated($"[red]Could not find compression dictionary: {compressionPath}\n[bold]Abort.[/][/]");
            return false;
        }

        compression = new ZsCompression(compressionPath);
        
        // Init checksums
        checksumLookup = new ChecksumLookup(checksumPath);
        
        return true;
    }
    
}