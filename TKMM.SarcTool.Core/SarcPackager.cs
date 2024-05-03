using SarcLibrary;
using System.Diagnostics;
using System.Text.Json;
using TotkCommon;
using TotkCommon.Extensions;

namespace TKMM.SarcTool.Core;

/// <summary>
/// Packages changes in SARC archives, flat BYML files, and GameDataList into
/// version-independent changelogs that can be applied using <see cref="SarcMerger"/>.
/// </summary>
public class SarcPackager {
    private readonly Totk config;
    private readonly ZsCompression compression;
    private readonly ChecksumLookup checksumLookup;
    private readonly HandlerManager handlerManager;
    private readonly int[] versions;
    private readonly string outputPath, modRomfsPath;
    
    internal static readonly string[] SupportedExtensions = new[] {
        ".bfarc", ".bkres", ".blarc", ".genvb", ".pack", ".ta",
        ".bfarc.zs", ".bkres.zs", ".blarc.zs", ".genvb.zs", ".pack.zs", ".ta.zs"
    };

    /// <summary>
    /// Creates a new instance of the <see cref="SarcPackager"/> class
    /// </summary>
    /// <param name="outputPath">The full path to the location to save the packaged changelogs.</param>
    /// <param name="modPath">
    ///     The full path to the location of the mod to package. This folder should contain the
    ///     "romfs" folder.
    /// </param>
    /// <param name="configPath">
    ///     The path to the location of the "config.json" file in standard NX Toolbox format, or
    ///     null to use the default location in local app data.
    /// </param>
    /// <param name="checksumPath">
    ///     The path to the location of the "checksums.bin" file in standard TKMM format, or null
    ///     to use the default location in local app data.
    /// </param>
    /// <param name="checkVersions">
    ///     An integer array of game versions to check, or null to check for all of them. The elements
    ///     should be in the following format: 100 for 1.0, 110 for 1.1, and so on.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if any of the required parameters are null.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown if the configuration file or checksum files are not found, or if the compression
    ///     dictionary is missing.
    /// </exception>
    public SarcPackager(string outputPath, string modPath, string? configPath = null, string? checksumPath = null, int[]? checkVersions = null) {
        this.handlerManager = new HandlerManager();
        this.outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        this.modRomfsPath = modPath ?? throw new ArgumentNullException(nameof(modPath));
        
        configPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Totk", "config.json");
        
        checksumPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "Totk", "checksums.bin");

        checkVersions ??= new[] {100, 110, 111, 112, 120, 121};

        if (!File.Exists(configPath))
            throw new Exception($"{configPath} not found");

        if (!File.Exists(checksumPath))
            throw new Exception($"{checksumPath} not found");

        using FileStream fs = File.OpenRead(configPath);
        this.config = JsonSerializer.Deserialize<Totk>(fs)
            ?? new();

        if (String.IsNullOrWhiteSpace(this.config.GamePath))
            throw new Exception("Game path is not defined in config.json");

        var compressionPath = Path.Combine(this.config.GamePath, "Pack", "ZsDic.pack.zs");
        if (!File.Exists(compressionPath)) {
            throw new Exception($"Compression package not found: {this.config.GamePath}");
        }

        compression = new ZsCompression(compressionPath);
        checksumLookup = new ChecksumLookup(checksumPath);

        this.modRomfsPath = modPath;
        this.versions = checkVersions;


    }

    /// <summary>
    /// Perform packaging on the mod.
    /// </summary>
    public void Package() {
        InternalMakePackage();
    }

    /// <summary>
    /// Perform packaging on the mod asynchronously.
    /// </summary>
    /// <returns>A task that represents the packaging work queued on the task pool.</returns>
    public async Task PackageAsync() {
        await Task.Run(Package);
    }
    

    private void InternalMakePackage() {
        string[] filesInFolder = Directory.GetFiles(modRomfsPath, "*", SearchOption.AllDirectories);
        
        foreach (var filePath in filesInFolder.Where(file => SupportedExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))) {
            var pathRelativeToBase = Path.GetRelativePath(modRomfsPath, Path.GetDirectoryName(filePath)!);
            var destinationPath = Path.Combine(outputPath, "romfs", pathRelativeToBase);
            if (!Directory.Exists(destinationPath))
                Directory.CreateDirectory(destinationPath);

            var outputFilePath = Path.Combine(destinationPath, Path.GetFileName(filePath));
            
            try {
                var result = HandleArchive(filePath, pathRelativeToBase);

                if (result.Length == 0) {
                    Trace.TraceInformation("Omitting {0}: Same as vanilla");
                    continue;
                }

                // Copy to destination
                if (File.Exists(outputFilePath)) {
                    Trace.TraceWarning("Overwriting {0}", outputFilePath);
                    File.Delete(outputFilePath);
                }

                File.WriteAllBytes(outputFilePath, result.ToArray());

                Trace.TraceInformation("Packaged {0}", outputFilePath);
            } catch (Exception exc) {
                Trace.TraceError("Failed to package {0} - Error: {1} - skipping", filePath, exc.Message);
                
                if (File.Exists(outputFilePath)) {
                    Trace.TraceWarning("Overwriting {0}", outputFilePath);
                    File.Delete(outputFilePath);
                }

                File.Copy(filePath, outputFilePath, true);
            }
        }

        Trace.TraceInformation("Packaging flat files to {0}", outputPath);
        PackageFilesInMod();

        Trace.TraceInformation("Creating GDL changelog");
        PackageGameDataList();


    }

    private Span<byte> HandleArchive(string archivePath, string pathRelativeToBase) {
        
        var isCompressed = archivePath.EndsWith(".zs");
        var isPackFile = archivePath.Contains(".pack.");

        ReadOnlySpan<char> sarcCanonicalPath = archivePath.ToCanonical(modRomfsPath);
        ReadOnlySpan<char> sarcExtension = Path.GetExtension(sarcCanonicalPath);
        var fileContents = GetFileContents(archivePath, isCompressed, isPackFile);

        // Identical archives don't need to be processed or copied
        if (IsArchiveIdentical(sarcCanonicalPath, fileContents, out bool isVanillaFile)) {
            return Span<byte>.Empty;
        }

        var sarc = Sarc.FromBinary(fileContents.ToArray());
        var originalSarc = GetOriginalArchive(Path.GetFileName(archivePath), pathRelativeToBase, isCompressed, isPackFile);
        // var isVanillaFile = IsVanillaFile(GetArchiveRelativeFilename(Path.GetFileName(archivePath), pathRelativeToBase));
        var toRemove = new List<string>();
        var atLeastOneReplacement = false;

        foreach (var entry in sarc) {
            var filenameHashSource = sarcExtension switch {
                ".pack" => entry.Key,
                _ => $"{sarcCanonicalPath}/{entry.Key}"
            };
            
            // Remove identical items from the SARC
            if (IsFileIdentical(filenameHashSource, entry.Value)) {
                toRemove.Add(entry.Key);
            } else if (originalSarc != null) {
                // Perform merge against the original file if we have an archive in the dump
                
                if (!originalSarc.ContainsKey(entry.Key))
                    continue;

                // This is set regardless at this point
                atLeastOneReplacement = true;
                
                // Otherwise, reconcile with the handler
                var fileExtension = Path.GetExtension(entry.Key);

                if (String.IsNullOrWhiteSpace(fileExtension)) {
                    Trace.TraceWarning("{0} in {1} does not have a file extension! Including as-is", entry.Key, archivePath);
                    sarc[entry.Key] = entry.Value;
                    continue;
                }

                // Drop the . from the extension
                fileExtension = fileExtension.Substring(1);
                var handler = handlerManager.GetHandlerInstance(fileExtension);

                if (handler == null) {
                    Trace.TraceWarning("No handler for {0} {1} - overwriting contents", archivePath, entry.Key);
                    sarc[entry.Key] = entry.Value;
                    continue;
                }
                
                var result = handler.Package(entry.Key, new List<MergeFile>() {
                    new MergeFile(1, entry.Value),
                    new MergeFile(0, originalSarc[entry.Key])
                });

                sarc[entry.Key] = result.ToArray();
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

    private void PackageGameDataList() {
       var gdlFilePath = Path.Combine(modRomfsPath, "GameData");

        if (!Directory.Exists(gdlFilePath))
            return;
        
        var files = Directory.GetFiles(gdlFilePath);

        var gdlMerger = new GameDataListMerger();

        foreach (var gdlFile in files) {

            try {
                if (!Path.GetFileName(gdlFile).StartsWith("GameDataList.Product"))
                    continue;

                var isCompressed = gdlFile.EndsWith(".zs");

                var vanillaFilePath = Path.Combine(config!.GamePath!, "GameData", Path.GetFileName(gdlFile));

                if (!File.Exists(vanillaFilePath)) {
                    throw new Exception("Failed to find vanilla GameDataList file");
                }

                var isVanillaCompressed = vanillaFilePath.EndsWith(".zs");

                var vanillaFile = GetFlatFileContents(vanillaFilePath, isVanillaCompressed);
                var modFile = GetFlatFileContents(gdlFile, isCompressed);

                var changelog = gdlMerger.Package(vanillaFile, modFile);

                if (changelog.Length == 0) {
                    Trace.TraceInformation("No changes in GDL");
                    continue;
                }

                var targetFilePath = Path.Combine(outputPath, "romfs", "GameData", "GameDataList.gdlchangelog");

                if (!Directory.Exists(Path.GetDirectoryName(targetFilePath)))
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
                
                File.WriteAllBytes(targetFilePath, changelog.ToArray());

                Trace.TraceInformation("Created GDL changelog");
                
                // Only need one change log
                break;
            } catch {
                Trace.TraceError("Failed to create GDL changelog");
                throw;
            }

        }

    }

    private void PackageFilesInMod() {
        var filesInModFolder =
            Directory.GetFiles(this.modRomfsPath, "*", SearchOption.AllDirectories);

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

            var pathRelativeToBase = Path.GetRelativePath(this.modRomfsPath, Path.GetDirectoryName(filePath)!);

            PackageFile(filePath, pathRelativeToBase);

            Trace.TraceInformation("Created {0} in {1}", filePath, pathRelativeToBase);
        }
    }

    private void PackageFile(string filePath, string pathRelativeToBase) {
        var targetFilePath = Path.Combine(outputPath, "romfs", pathRelativeToBase, Path.GetFileName(filePath));
        var vanillaFilePath = Path.Combine(config!.GamePath!, pathRelativeToBase, Path.GetFileName(filePath));

        // If the vanilla file doesn't exist just copy it over and we're done
        if (!File.Exists(vanillaFilePath)) {
            File.Copy(filePath, targetFilePath, true);
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
            Trace.TraceWarning("No handler for {0} {1} - overwriting contents", Path.GetFileName(filePath), 
                               pathRelativeToBase);
            
            File.Copy(filePath, targetFilePath, true);
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

    private void WriteFileContents(string archivePath, Sarc sarc, bool isCompressed, bool isPackFile) {
        if (compression == null)
            throw new Exception("Compression not loaded");

        using var memoryStream = new MemoryStream();
        sarc.Write(memoryStream);

        if (isCompressed) {
            var type = CompressionType.Common;

            // Change compression type
            if (isPackFile)
                type = CompressionType.Pack;
            else if (archivePath.Contains("bcett", StringComparison.OrdinalIgnoreCase))
                type = CompressionType.Bcett;
            
            File.WriteAllBytes(archivePath,
                               compression.Compress(memoryStream.ToArray(), type)
                                          .ToArray());
        } else {
            File.WriteAllBytes(archivePath, memoryStream.ToArray());
        }
    }

    private Span<byte> GetFileContents(string archivePath, bool isCompressed, bool isPackFile) {
        if (compression == null)
            throw new Exception("Compression not loaded");

        Span<byte> sourceFileContents;
        if (isCompressed) {
            // Need to decompress the file first
            var type = CompressionType.Common;

            // Change compression type
            if (isPackFile)
                type = CompressionType.Pack;
            else if (archivePath.Contains("bcett", StringComparison.OrdinalIgnoreCase))
                type = CompressionType.Bcett;
            
            var compressedContents = File.ReadAllBytes(archivePath).AsSpan();
            sourceFileContents = compression.Decompress(compressedContents, type);
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
            var type = CompressionType.Common;

            // Change compression type
            if (filePath.Contains("bcett", StringComparison.OrdinalIgnoreCase))
                type = CompressionType.Bcett;
            
            var compressedContents = File.ReadAllBytes(filePath).AsSpan();
            sourceFileContents = compression.Decompress(compressedContents, type);
        } else {
            sourceFileContents = File.ReadAllBytes(filePath).AsSpan();
        }

        return new Memory<byte>(sourceFileContents.ToArray());
    }

    private void WriteFlatFileContents(string filePath, ReadOnlyMemory<byte> contents, bool isCompressed) {
        if (compression == null)
            throw new Exception("Compression not loaded");

        if (isCompressed) {
            var type = CompressionType.Common;

            // Change compression type
            if (filePath.Contains("bcett", StringComparison.OrdinalIgnoreCase))
                type = CompressionType.Bcett;
            
            File.WriteAllBytes(filePath,
                               compression.Compress(contents.ToArray(), type).ToArray());
        } else {
            File.WriteAllBytes(filePath, contents.ToArray());
        }
    }

    private bool IsFileIdentical(ReadOnlySpan<char> canonical, Span<byte> data) {
        foreach (int version in this.versions) {
            if (this.checksumLookup.IsVanillaFile(canonical, data, version, out _)) {
                return true;
            }
        }

        return false;
    }

    // private bool IsVanillaFile(string filename) {
    //     var filenameHash = Checksum.ComputeXxHash(filename);
    //     return checksumLookup!.GetChecksum(filenameHash) != null;
    // }

    private bool IsArchiveIdentical(ReadOnlySpan<char> canonical, Span<byte> data, out bool isEntryFound) {
        isEntryFound = false;
        foreach (int version in this.versions) {
            if (this.checksumLookup.IsVanillaFile(canonical, data, version, out isEntryFound)) {
                return true;
            }
        }

        return false;
    }

    // private static string GetArchiveRelativeFilename(string archivePath, string pathRelativeToBase) {
    //     // Relative filename
    //     var pathSeparator = Path.DirectorySeparatorChar;
    //     var archiveRelativeFilename = Path.Combine(pathRelativeToBase, Path.GetFileName(archivePath));
    //     
    //     // Replace the path separator with the one used by the Switch
    //     if (pathSeparator != '/')
    //         archiveRelativeFilename = archiveRelativeFilename.Replace(pathSeparator, '/');
    // 
    //     // Get rid of any romfs portion of the path
    //     archiveRelativeFilename = archiveRelativeFilename.Replace("/romfs/", "")
    //                                                      .Replace("romfs/", "");
    // 
    //     // Get rid of any .zs on the end if the file was originally compressed
    //     if (archiveRelativeFilename.EndsWith(".zs"))
    //         archiveRelativeFilename = archiveRelativeFilename.Substring(0, archiveRelativeFilename.Length - 3);
    //     return archiveRelativeFilename;
    // }

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
            var type = CompressionType.Common;

            // Change compression type
            if (isPackFile)
                type = CompressionType.Pack;
            else if (archivePath.Contains("bcett", StringComparison.OrdinalIgnoreCase))
                type = CompressionType.Bcett;
            
            var compressedContents = File.ReadAllBytes(archivePath).AsSpan();
            fileContents = compression!.Decompress(compressedContents, type);
        } else {
            fileContents = File.ReadAllBytes(archivePath).AsSpan();
        }

        return Sarc.FromBinary(fileContents.ToArray());
    }

}