using System.Diagnostics;
using System.Text.Json;
using SarcLibrary;
using TotkCommon;

namespace TKMM.SarcTool.Core;

internal class ArchiveCache {

    private readonly string configPath;
    private Dictionary<string, string> archiveMappings;
    private readonly ArchiveHelper archiveHelper;
    private readonly Totk config;

    private static Dictionary<string, string>? globalArchiveMappings;

    private static object syncRoot = new object();

    private bool isInitialized;

    public ArchiveCache(string configPath, ZsCompression compression) {
        this.configPath = configPath;
        this.archiveMappings = new Dictionary<string, string>();
        this.archiveHelper = new ArchiveHelper(compression);
        
        using FileStream fs = File.OpenRead(configPath);
        this.config = JsonSerializer.Deserialize<Totk>(fs)
                      ?? new();
    }
    
    public void Initialize() {
        lock (syncRoot) {
            if (isInitialized)
                return;

            if (globalArchiveMappings != null) {
                Trace.TraceInformation("Using cached archive mappings");
                this.archiveMappings = globalArchiveMappings;
            } else {
                Trace.TraceInformation("Loading archived mappings");
                var archiveCachePath =
                    Path.Combine(Path.GetDirectoryName(configPath) ?? string.Empty, "archivemappings.bin");

                if (!File.Exists(archiveCachePath)) {
                    CreateArchiveCache(archiveCachePath);
                } else {
                    LoadArchiveCacheFromDisk(archiveCachePath);
                }

                // Cache it for use by future instances
                globalArchiveMappings = this.archiveMappings;
            }

            isInitialized = true;
        }
    }

    private void CreateArchiveCache(string archiveCachePath) {

        var supportedExtensions = new[] {
            ".bfarc", ".bkres", ".blarc", ".genvb", ".pack", ".ta",
            ".bfarc.zs", ".bkres.zs", ".blarc.zs", ".genvb.zs", ".pack.zs", ".ta.zs"
        };

        Trace.TraceInformation("Creating archive cache (this may take a bit)");

        var dumpArchives = Directory.GetFiles(config.GamePath, "*", SearchOption.AllDirectories)
                                    .Where(l => supportedExtensions.Any(ext => l.EndsWith(ext)))
                                    .ToList();
        
        foreach (var file in dumpArchives) {
            var isCompressed = file.EndsWith(".zs");
            var relativeArchivePath = archiveHelper.GetRelativePath(file, config.GamePath);

            try {
                var archiveContents = archiveHelper.GetFileContents(file, isCompressed, out _);
                var sarc = Sarc.FromBinary(archiveContents.ToArray());

                foreach (var key in sarc.Keys)
                    archiveMappings.TryAdd(key, relativeArchivePath);
                
            } catch (Exception exc) {
                Trace.TraceError("Couldn't load {0} - Error: {1} - Skipping", file, exc.Message);
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

        archiveMappings.Clear();

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

    public bool TryGetValue(string relativeFilePath, out string archiveRelativePath) {
        if (!isInitialized)
            throw new InvalidOperationException("Cache is not initialized");
        
        var result = archiveMappings.TryGetValue(relativeFilePath, out var output);

        // This is so we always return a non-null string in the out parameter
        if (!result || String.IsNullOrWhiteSpace(output)) {
            archiveRelativePath = "";
            return false;
        }

        archiveRelativePath = output;
        return true;
    }
}