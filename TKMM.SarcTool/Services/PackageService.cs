using SarcLibrary;
using Spectre.Console;
using TKMM.SarcTool.Common;
using TKMM.SarcTool.Compression;

namespace TKMM.SarcTool.Services;

internal class PackageService {

    private readonly ConfigService configService;

    private ConfigJson? config;
    private ZsCompression? compression;
    private ChecksumLookup? checksumLookup;
    private string[] versions = new string[0];
    
    private readonly string[] supportedExtensions = new[] {
        ".bars", ".bfarc", ".bkres", ".blarc", ".genvb", ".pack", ".ta",
        ".bars.zs", ".bfarc.zs", ".bkres.zs", ".blarc.zs", ".genvb.zs", ".pack.zs", ".ta.zs"
    };

    public PackageService(ConfigService configService) {
        this.configService = configService;
    }
    
    public int Execute(string outputPath, string modPath, string? configPath, string? checksumPath, string[] checkVersions) {
        if (String.IsNullOrWhiteSpace(configPath))
            configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "Totk", "config.json");
        
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
                
                context.Status($"Processing {filePath}");
                var result = HandleArchive(filePath, pathRelativeToBase);

                if (result.Length == 0) {
                    AnsiConsole.MarkupLineInterpolated($"! [yellow]Omitting {modPath} because file is same as vanilla[/]");
                    continue;
                }
                
                // Copy to destination
                
                var destinationPath = Path.Combine(outputPath, pathRelativeToBase);
                if (!Directory.Exists(destinationPath))
                    Directory.CreateDirectory(destinationPath);

                var outputFilePath = Path.Combine(destinationPath, Path.GetFileName(filePath));

                if (File.Exists(outputFilePath)) {
                    AnsiConsole.MarkupLineInterpolated($"! [yellow]Overwriting existing file in output: {outputFilePath}[/]");
                    File.Delete(outputFilePath);
                }

                File.WriteAllBytes(outputFilePath, result.ToArray());

                AnsiConsole.MarkupLineInterpolated($"» [green]Wrote {outputFilePath}[/]");
            }
        });
        

    }

    private Span<byte> HandleArchive(string archivePath, string pathRelativeToBase) {

        if (compression == null)
            throw new Exception("Compression not loaded");

        if (checksumLookup == null)
            throw new Exception("Checksums not loaded");

        var isCompressed = archivePath.EndsWith(".zs");
        var isPackFile = archivePath.Contains(".pack.");
        
        Span<byte> fileContents;
        if (isCompressed) {
            // Need to decompress the file first
            var compressedContents = File.ReadAllBytes(archivePath).AsSpan();
            fileContents = compression.Decompress(compressedContents, isPackFile ? CompressionType.Pack : CompressionType.Common);
        } else {
            fileContents = File.ReadAllBytes(archivePath).AsSpan();
        }

        var archiveHash = Checksum.ComputeXxHash(fileContents);
        
        // Identical archives don't need to be processed or copied
        if (IsArchiveIdentical(archivePath, pathRelativeToBase, archiveHash))
            return Span<byte>.Empty;

        var sarc = Sarc.FromBinary(fileContents);
        var toRemove = new List<string>();

        foreach (var entry in sarc) {
            var fileHash = Checksum.ComputeXxHash(entry.Value);
            
            // Remove identical items from the SARC
            if (IsFileIdentical(entry.Key, fileHash)) {
                toRemove.Add(entry.Key);
            }
        }
        
        // Nothing to remove? Send back the original file
        if (toRemove.Count == 0)
            return fileContents;
        
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

    private bool IsArchiveIdentical(string archivePath, string pathRelativeToBase, ulong archiveHash) {
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