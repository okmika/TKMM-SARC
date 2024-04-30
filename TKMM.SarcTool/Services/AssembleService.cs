using System.Runtime.Serialization.Formatters.Binary;
using SarcLibrary;
using Spectre.Console;
using TKMM.SarcTool.Common;
using TKMM.SarcTool.Compression;

namespace TKMM.SarcTool.Services;

public class AssembleService {
    private readonly ConfigService configService;
    private readonly IHandlerManager handlerManager;

    private ConfigJson? config;
    private ZsCompression? compression;
    private Dictionary<string, string> archiveMappings = new Dictionary<string, string>();

    public AssembleService(ConfigService configService, IHandlerManager handlerManager) {
        this.configService = configService;
        this.handlerManager = handlerManager;
    }

    public int Assemble(string modPath, string? configPath) {

        if (String.IsNullOrWhiteSpace(configPath))
            configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "Totk");

        var configFile = Path.Combine(configPath, "config.json");

        if (!File.Exists(configFile)) {
            AnsiConsole.MarkupInterpolated($"[red]Failed to find config file: {configFile} - abort[/]");
            return -1;
        }

        if (!Initialize(configFile))
            return -1;

        AnsiConsole.Status()
                   .Spinner(Spinner.Known.Dots2)
                   .Start("Preparing", context => {
                       LoadArchiveCache(configPath, context);
                       InternalAssemble(modPath, context);
                   });

        AnsiConsole.MarkupLine("[green]Assembly completed successfully.[/]");
        return 0;
    }

    private void InternalAssemble(string modPath, StatusContext context) {

        var supportedExtensions = handlerManager.GetSupportedExtensions();

        context.Status($"Preparing to assemble...");
        AnsiConsole.MarkupLineInterpolated($"[bold]Assembling in {modPath}  ({string.Join(" ", supportedExtensions)})[/]");
        
        var flatFiles = Directory.GetFiles(modPath, "*", SearchOption.AllDirectories)
                                 .Where(l => supportedExtensions.Any(
                                            ext => l.EndsWith(ext) || l.EndsWith(ext + ".zs")))
                                 .ToList();

        foreach (var file in flatFiles) {
            context.Status($"Assembling {file}");

            var relativeFilePath = GetRelativePath(file, modPath);
            
            if (!archiveMappings.TryGetValue(relativeFilePath, out var archiveRelativePath)) {
                continue;
            }

            if (!MergeIntoArchive(modPath, archiveRelativePath, file, relativeFilePath)) {
                AnsiConsole.MarkupLineInterpolated($"! [yellow]Skipping {file} - could not merge[/]");
                continue;
            }
            
            // Success means we delete the flat file
            AnsiConsole.MarkupLineInterpolated($"» [green]{file} merged into {archiveRelativePath}");
            File.Delete(file);

        }
        
    }

    private bool MergeIntoArchive(string modPath, string archiveRelativePath, string filePath, string fileRelativePath) {

        var archivePath = GetAbsolutePath(archiveRelativePath, modPath);

        // First test the existing archive
        if (!File.Exists(archivePath))
            archivePath += ".zs";
        if (!File.Exists(archivePath) && !CopyVanillaArchive(archiveRelativePath, archivePath))
            return false;

        var isCompressed = archivePath.EndsWith(".zs");
        var archiveContents = GetFileContents(archivePath, isCompressed, true);
        var sarc = Sarc.FromBinary(archiveContents.ToArray());

        var isFileCompressed = filePath.EndsWith(".zs");
        var fileContents = GetFileContents(filePath, isFileCompressed, false);

        // Skip if the SARC doesn't contain the file already
        if (!sarc.ContainsKey(fileRelativePath)) {
            sarc.Add(fileRelativePath, fileContents.ToArray());
        } else {
            sarc[fileRelativePath] = fileContents.ToArray();
        }

        WriteFileContents(archivePath, sarc, isCompressed, true);
        return true;

    }

    private bool CopyVanillaArchive(string archiveRelativePath, string destination) {
        var vanillaPath = GetAbsolutePath(archiveRelativePath, config!.GamePath!);

        if (!File.Exists(vanillaPath))
            vanillaPath += ".zs";
        if (!File.Exists(vanillaPath))
            return false;

        File.Copy(vanillaPath, destination, true);
        return true;
    }

    private void LoadArchiveCache(string configPath, StatusContext context) {
        var archiveCachePath = Path.Combine(configPath, "archivemappings.bin");

        if (!File.Exists(archiveCachePath)) {
            CreateArchiveCache(archiveCachePath, context);
        } else {
            LoadArchiveCacheFromDisk(archiveCachePath);
        }
    }

    private void CreateArchiveCache(string archiveCachePath, StatusContext context) {

        var supportedExtensions = new[] {
            ".pack.zs", ".pack"
        };

        context.Status($"Preparing to create archive cache");

        var dumpArchives = Directory.GetFiles(config!.GamePath!, "*", SearchOption.AllDirectories)
                                    .Where(l => supportedExtensions.Any(ext => l.EndsWith(ext)))
                                    .ToList();

        context.Status($"Creating archive cache (this may take a bit)");
        
        foreach (var file in dumpArchives) {
            var isCompressed = file.EndsWith(".zs");
            var relativeArchivePath = GetRelativePath(file, config!.GamePath!);

            try {
                var archiveContents = GetFileContents(file, isCompressed, true);
                var sarc = Sarc.FromBinary(archiveContents.ToArray());

                foreach (var key in sarc.Keys)
                    archiveMappings.TryAdd(key, relativeArchivePath);

                AnsiConsole.MarkupLineInterpolated($"» [green]{file}[/]");
            } catch (Exception exc) {
                AnsiConsole.WriteException(exc, ExceptionFormats.ShortenEverything);
                AnsiConsole.MarkupLineInterpolated($"X [red]Couldn't load {file} - skipping[/]");
            }
        }

        SerializeCacheToDisk(archiveCachePath);

    }

    private void LoadArchiveCacheFromDisk(string inputFile) {
        using var inputStream = new FileStream(inputFile, FileMode.Open);
        using var reader = new BinaryReader(inputStream);

        var magic = reader.ReadChars(4);

        if (new string(magic) != "STMC")
            throw new InvalidDataException("Cache has invalid header");

        var version = reader.ReadInt16();

        if (version != 1)
            throw new InvalidDataException($"Cache does not support version {version}");

        var itemCount = reader.ReadInt32();

        archiveMappings = new Dictionary<string, string>();

        for (int i = 0; i < itemCount; i++) {
            var key = reader.ReadString();
            var value = reader.ReadString();

            archiveMappings.TryAdd(key, value);
        }

        reader.Close();
        inputStream.Close();
    }

    private void SerializeCacheToDisk(string outputFile) {

        using var outputStream = new FileStream(outputFile, FileMode.Create);
        using var writer = new BinaryWriter(outputStream);

        // Header
        writer.Write("STMC".ToCharArray());       // Magic
        writer.Write((short)1);                   // Version

        writer.Write(archiveMappings.Count);      // Item count
        
        foreach (var item in archiveMappings) {
            writer.Write(item.Key);
            writer.Write(item.Value);
        }

        writer.Flush();
        outputStream.Flush();
        outputStream.Close();

    }

    private string GetRelativePath(string archivePath, string basePath) {
        var pathRelativeToBase = Path.GetRelativePath(basePath, archivePath);

        if (Path.DirectorySeparatorChar != '/')
            pathRelativeToBase = pathRelativeToBase.Replace(Path.DirectorySeparatorChar, '/');

        pathRelativeToBase = pathRelativeToBase.Replace($"romfs/", "")
                                               .Replace($"/romfs/", "");

        if (pathRelativeToBase.EndsWith(".zs"))
            pathRelativeToBase = pathRelativeToBase.Substring(0, pathRelativeToBase.Length - 3);

        return pathRelativeToBase;

    }

    private string GetAbsolutePath(string relativePath, string basePath) {
        if (Path.DirectorySeparatorChar != '/')
            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

        if (!basePath.Contains("romfs"))
            return Path.Combine(basePath, "romfs", relativePath);
        else
            return Path.Combine(basePath, relativePath);
    }

    private bool Initialize(string configPath) {
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
        return true;
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
            
            File.WriteAllBytes(archivePath, compression.Compress(memoryStream.ToArray(), type).ToArray());
        } else {
            File.WriteAllBytes(archivePath, memoryStream.ToArray());
        }
    }
    
}