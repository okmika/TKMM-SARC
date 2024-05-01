using System.Diagnostics;
using SarcLibrary;
using TKMM.SarcTool.Core.Model;

namespace TKMM.SarcTool.Core;

public class SarcMerger {
    private readonly ConfigJson config;
    private readonly ZsCompression compression;
    private readonly List<ShopsJsonEntry> shops;

    private readonly string outputPath, basePath;
    private readonly string[] modsList;
    private readonly HandlerManager handlerManager;

    public SarcMerger(IEnumerable<string> modsList, string basePath, string outputPath, string? configPath = null, string? shopsPath = null) {
        this.handlerManager = new HandlerManager();
        this.outputPath = outputPath;
        this.modsList = modsList.ToArray();
        this.basePath = basePath;
        
        configPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Totk", "config.json");

        shopsPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tkmm",
                                     "shops.json");

        if (!File.Exists(configPath))
            throw new Exception($"{configPath} not found");

        this.config = ConfigJson.Load(configPath);

        if (String.IsNullOrWhiteSpace(this.config.GamePath))
            throw new Exception("Game path is not defined in config.json");

        var compressionPath = Path.Combine(this.config.GamePath, "Pack", "ZsDic.pack.zs");
        if (!File.Exists(compressionPath)) {
            throw new Exception("Compression package not found: {this.config.GamePath}");
        }

        if (!File.Exists(shopsPath))
            throw new Exception($"{shopsPath} not found");

        this.shops = ShopsJsonEntry.Load(shopsPath);
        compression = new ZsCompression(compressionPath);
        
    }

    public void Merge() {

        Trace.TraceInformation("Merging archives");
        InternalMergeArchives();

        Trace.TraceInformation("Merging flat files");
        InternalFlatMerge();

    }

    public bool HasGdlChanges(string fileOne, string fileTwo) {
        if (fileOne == null)
            throw new ArgumentNullException(nameof(fileOne));
        
        if (fileTwo == null)
            throw new ArgumentNullException(nameof(fileTwo));
        
        if (!fileOne.EndsWith(".zs") || !fileTwo.EndsWith(".zs")) {
            throw new Exception("Only compressed (.zs) GDL files are supported");
        }

        var fileOneBytes = GetFlatFileContents(fileOne, true);
        var fileTwoBytes = GetFlatFileContents(fileTwo, true);

        var merger = new GameDataListMerger();
        return merger.Compare(fileOneBytes, fileTwoBytes);
    }

    private void InternalFlatMerge() {
        foreach (var modFolderName in modsList) {
            Trace.TraceInformation("Processing {0}", modFolderName);
            
            MergeFilesInMod(modFolderName);
            
            Trace.TraceInformation("Processing GDL in {0}", modFolderName);
            MergeGameDataList(modFolderName);
        }
    }

    private void InternalMergeArchives() {

        CleanPackagesInTarget();

        foreach (var modFolderName in modsList) {
            Trace.TraceInformation("Processing {0}", modFolderName);
            MergeArchivesInMod(modFolderName);
        }

        Trace.TraceInformation("Merging shops");
        MergeShops();

    }

    private void MergeFilesInMod(string modFolderName) {

        // Get archive files in mod folder
        var modFolderPath = Path.Combine(basePath, modFolderName);

        if (!Directory.Exists(modFolderPath))
            throw new Exception($"Mod folder '{modFolderName}' does not exist under '{basePath}'");

        var filesInModFolder =
            Directory.GetFiles(modFolderPath, "*", SearchOption.AllDirectories);

        var supportedFlatExtensions = handlerManager.GetSupportedExtensions().ToHashSet();
        supportedFlatExtensions = supportedFlatExtensions.Concat(supportedFlatExtensions.Select(l => $"{l}.zs")).ToHashSet();

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
            
            var baseRomfs = Path.Combine(basePath, modFolderName, "romfs");
            var pathRelativeToBase = Path.GetRelativePath(baseRomfs, Path.GetDirectoryName(filePath)!);

            try {
                MergeFile(filePath, modFolderName, pathRelativeToBase);
            } catch {
                Trace.TraceError("Failed to merge {0}", filePath);
                throw;
            }

            Trace.TraceInformation("Merged {0} into {1}", filePath, pathRelativeToBase);
        }
    }

    private void MergeGameDataList(string modFolderName) {
        var modPath = Path.Combine(basePath, modFolderName);
        var gdlChangelog = Path.Combine(modPath, "romfs", "GameData", "GameDataList.gdlchangelog");

        if (!File.Exists(gdlChangelog))
            return;
        
        // Copy over vanilla files first
        var vanillaGdlPath = Path.Combine(config!.GamePath!, "GameData");

        if (!Directory.Exists(vanillaGdlPath))
            throw new Exception($"Failed to find vanilla GDL files at {vanillaGdlPath}");

        var vanillaGdlFiles = Directory.GetFiles(vanillaGdlPath)
                                       .Where(l => Path.GetFileName(l).StartsWith("GameDataList.Product") &&
                                                   Path.GetFileName(l).EndsWith(".byml.zs"));

        foreach (var vanillaFile in vanillaGdlFiles) {
            var outputGdl = Path.Combine(outputPath, "GameData", Path.GetFileName(vanillaFile));

            Directory.CreateDirectory(Path.GetDirectoryName(outputGdl)!);
            
            if (!File.Exists(outputGdl))
                File.Copy(vanillaFile, outputGdl, true);
        }

        var gdlFiles = Directory.GetFiles(Path.Combine(outputPath, "GameData"))
                                .Where(l => Path.GetFileName(l).StartsWith("GameDataList.Product"))
                                .ToList();

        var changelogBytes = File.ReadAllBytes(gdlChangelog);

        foreach (var gdlFile in gdlFiles) {
            var gdlFileBytes = GetFlatFileContents(gdlFile, true);
            var merger = new GameDataListMerger();

            var resultBytes = merger.Merge(gdlFileBytes, changelogBytes);

            WriteFlatFileContents(gdlFile, resultBytes, true);

            Trace.TraceInformation("Merged GDL changelog into {0}", gdlFile);
        }

        // Delete the changelog in the output folder in case it's there
        var gdlChangelogInOutput = Path.Combine(outputPath, "GameData", "GameDataList.gdlchangelog");
        if (File.Exists(gdlChangelogInOutput))
            File.Delete(gdlChangelogInOutput);

    }

    private void MergeShops() {
      
        
        var merger = new ShopsMerger(this, shops.Select(l => l.ActorName).ToHashSet());
        
        // This will be called if we ever need to request a shop file from the dump
        merger.GetEntryForShop = (actorName) => {
            var dumpPath = Path.Combine(config!.GamePath!, "Pack", "Actor", $"{actorName}.pack.zs");
            var target = Path.Combine(outputPath, "Pack", "Actor", $"{actorName}.pack.zs");

            File.Copy(dumpPath, target, true);

            return new ShopsMerger.ShopMergerEntry(actorName, target);
        };
        
        foreach (var shop in shops) {
            var archivePath = Path.Combine(outputPath, "Pack", "Actor", $"{shop.ActorName}.pack.zs");
            if (!File.Exists(archivePath)) {
                continue;
            }

            merger.Add(shop.ActorName, archivePath);
        }

        merger.MergeShops();
    }

    private void MergeFile(string filePath, string modFolderName, string pathRelativeToBase) {
        var targetFilePath = Path.Combine(outputPath, pathRelativeToBase, Path.GetFileName(filePath));

        // If the output doesn't even exist just copy it over and we're done
        if (!File.Exists(targetFilePath)) {
            if (!Directory.Exists(Path.GetDirectoryName(targetFilePath)))
                Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);

            var didCopy = CopyOriginal(filePath, pathRelativeToBase, targetFilePath);

            // Copy the mod's file to the output if we otherwise failed to copy the file from the dump
            if (!didCopy) {
                File.Copy(filePath, targetFilePath, true);
                return;
            }
        }

        // Otherwise try to reconcile and merge
        var sourceIsCompressed = filePath.EndsWith(".zs");
        var targetIsCompressed = filePath.EndsWith(".zs");

        var sourceFileContents = GetFlatFileContents(filePath, sourceIsCompressed);
        var targetFileContents = GetFlatFileContents(targetFilePath, targetIsCompressed);

        var fileExtension = Path.GetExtension(filePath).Substring(1).ToLower();
        var handler = handlerManager.GetHandlerInstance(fileExtension);

        if (handler == null) {
            Trace.TraceWarning("{0}: No handler for {1} - overwriting contents of {2} in {3}", modFolderName,
                               fileExtension, Path.GetFileName(filePath), pathRelativeToBase);
            
            File.Copy(filePath, targetFilePath, true);
        } else {
            var relativeFilename = Path.Combine(pathRelativeToBase, Path.GetFileName(filePath));

            if (Path.DirectorySeparatorChar != '/')
                relativeFilename = relativeFilename.Replace(Path.DirectorySeparatorChar, '/');
            
            var result = handler.Merge(relativeFilename, new List<MergeFile>() {
                new MergeFile(1, sourceFileContents),
                new MergeFile(0, targetFileContents)
            });

            WriteFlatFileContents(targetFilePath, result, targetIsCompressed);
        }
    }

    private void MergeArchivesInMod(string modFolderName) {
        
        // Get archive files in mod folder
        var modFolderPath = Path.Combine(basePath, modFolderName);

        if (!Directory.Exists(modFolderPath))
            throw new Exception($"Mod folder '{modFolderName}' does not exist under '{basePath}'");
        
        var filesInModFolder = Directory.GetFiles(modFolderPath, "*", SearchOption.AllDirectories);

        foreach (var filePath in filesInModFolder.Where(file => SarcPackager.SupportedExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))) {

            var baseRomfs = Path.Combine(basePath, modFolderName, "romfs");
            var pathRelativeToBase = Path.GetRelativePath(baseRomfs, Path.GetDirectoryName(filePath)!);
            Trace.TraceInformation("{0}: Merging {1}", modFolderName, filePath);

            try {
                MergeArchive(modFolderName, filePath, pathRelativeToBase);
            } catch (InvalidDataException) {
                Trace.TraceWarning("Invalid archive: {0} - can't merge so overwriting by priority", filePath);
                var targetArchivePath = Path.Combine(outputPath, pathRelativeToBase, Path.GetFileName(filePath));

                if (File.Exists(targetArchivePath))
                    File.Delete(targetArchivePath);

                File.Copy(filePath, targetArchivePath, true);
            } catch (Exception exc) {
                Trace.TraceError("Failed to merge {0}", filePath);
                throw;
            }
        }
        
    }

    private void CleanPackagesInTarget() {
        if (!Directory.Exists(outputPath))
            return;
        
        Trace.TraceWarning("Cleaning existing archives in output {0}", outputPath);

        var filesInOutputFolder = Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories);
        foreach (var filePath in filesInOutputFolder.Where(file => SarcPackager.SupportedExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))) {
            File.Delete(filePath);
        }
    }

    private void MergeArchive(string modFolderName, string archivePath, string pathRelativeToBase) {
        // If the output doesn't even exist just copy it over and we're done
        var targetArchivePath = Path.Combine(outputPath, pathRelativeToBase, Path.GetFileName(archivePath));

        if (!File.Exists(targetArchivePath)) {
            if (!Directory.Exists(Path.GetDirectoryName(targetArchivePath)))
                Directory.CreateDirectory(Path.GetDirectoryName(targetArchivePath)!);
            
            var didCopy = CopyOriginal(archivePath, pathRelativeToBase, targetArchivePath);
            
            // Copy the mod's package to the output if we otherwise failed to copy the file from the dump
            if (!didCopy) {
                File.Copy(archivePath, targetArchivePath, true);
                return;
            }
        }
        
        // Otherwise try to reconcile and merge
        var isCompressed = archivePath.EndsWith(".zs");
        var isPackFile = archivePath.Contains(".pack.");

        Span<byte> sourceFileContents = GetFileContents(archivePath, isCompressed, isPackFile);
        Span<byte> targetFileContents = GetFileContents(targetArchivePath, isCompressed, isPackFile);

        var sourceSarc = Sarc.FromBinary(sourceFileContents.ToArray());
        var targetSarc = Sarc.FromBinary(targetFileContents.ToArray());

        foreach (var entry in sourceSarc) {
            if (!targetSarc.ContainsKey(entry.Key)) {
                // If the archive doesn't have the file, add it
                targetSarc.Add(entry.Key, entry.Value);
            } else {
                // Otherwise, reconcile with the handler
                var fileExtension = Path.GetExtension(entry.Key).Substring(1);
                var handler = handlerManager.GetHandlerInstance(fileExtension);

                if (handler == null) {
                    Trace.TraceWarning("{0}: No handler for {1} - overwriting contents of {2} in {3}", modFolderName,
                                       fileExtension, entry.Key, targetArchivePath);
                    targetSarc[entry.Key] = entry.Value;
                    continue;
                }
                
                var result = handler.Merge(entry.Key, new List<MergeFile>() {
                    new MergeFile(1, entry.Value),
                    new MergeFile(0, targetSarc[entry.Key])
                });

                targetSarc[entry.Key] = result.ToArray();
            }
        }

        WriteFileContents(targetArchivePath, targetSarc, isCompressed, isPackFile);
    }

    internal void WriteFileContents(string archivePath, Sarc sarc, bool isCompressed, bool isPackFile) {
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

    internal Span<byte> GetFileContents(string archivePath, bool isCompressed, bool isPackFile) {
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

    private bool CopyOriginal(string archivePath, string pathRelativeToBase, string outputFile) {
        var sourcePath = config!.GamePath!;
        var originalFile = Path.Combine(sourcePath, pathRelativeToBase, Path.GetFileName(archivePath));

        if (File.Exists(originalFile)) {
            File.Copy(originalFile, outputFile, true);
            return true;
        }

        return false;
    }

    
}