using System.Text.Json;
using TotkCommon;

namespace TKMM.SarcTool.Core;

/// <summary>
/// Generates and merges changelogs for GameDataList files.
/// </summary>
public class GdlPackager {

    private readonly Totk config;
    private readonly ZsCompression compression;
    private readonly ArchiveHelper archiveHelper;
    
    /// <summary>
    /// Creates a new instance of <see cref="GdlPackager"/> class.
    /// </summary>
    /// <param name="configPath">
    ///     The path to the location of the "config.json" file in standard NX Toolbox format, or
    ///     null to use the default location in local app data.
    /// </param>
    /// <exception cref="Exception">
    ///     Thrown if any of the configuration files are not found, or if the compression
    ///     dictionary is missing.
    /// </exception>
    public GdlPackager(string? configPath = null) {
        configPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "totk", "Config.json");

        if (!File.Exists(configPath))
            throw new Exception($"{configPath} not found");

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
        this.archiveHelper = new ArchiveHelper(compression);
    }

    /// <summary>
    /// Generate a changelog for the specified GameDataList file. 
    /// </summary>
    /// <param name="gdlPath">The full path to the GameDataList file to generate a changelog for.</param>
    /// <param name="outputChangelogPath">The full path of the changelog to write.</param>
    /// <returns>True if the changelog was created or False if no changes were found.</returns>
    /// <exception cref="ArgumentNullException">The path arguments are null.</exception>
    /// <exception cref="FileNotFoundException">The GameDataList file is not found.</exception>
    /// <exception cref="Exception">The vanilla GDL file was not found in the game dump path.</exception>
    public bool Package(string gdlPath, string outputChangelogPath) {
        if (gdlPath == null)
            throw new ArgumentNullException(nameof(gdlPath));
        if (outputChangelogPath == null)
            throw new ArgumentNullException(nameof(outputChangelogPath));

        if (!File.Exists(gdlPath))
            throw new FileNotFoundException("GDL file not found", gdlPath);

        var gdlMerger = new GameDataListMerger();

        var isCompressed = gdlPath.EndsWith(".zs");

        var vanillaFilePath = Path.Combine(config.GamePath, "GameData", Path.GetFileName(gdlPath));

        if (!File.Exists(vanillaFilePath)) {
            throw new Exception("Failed to find vanilla GameDataList file");
        }

        var isVanillaCompressed = vanillaFilePath.EndsWith(".zs");

        var vanillaFile = archiveHelper.GetFlatFileContents(vanillaFilePath, isVanillaCompressed, out _);
        var modFile = archiveHelper.GetFlatFileContents(gdlPath, isCompressed, out _);

        var changelog = gdlMerger.Package(vanillaFile, modFile);

        if (changelog.Length == 0)
            return false;

        File.WriteAllBytes(outputChangelogPath, changelog.ToArray());
        return true;
    }

    /// <summary>
    /// Merges the GameDataList changelog into the output path's GameDataList files.
    /// </summary>
    /// <param name="changelogPath">The full path to the GameDataList changelog to merge.</param>
    /// <param name="outputPath">The path to write the GameDataList files to.</param>
    /// <exception cref="ArgumentNullException">The required arguments are null.</exception>
    /// <exception cref="Exception">The vanilla GDL files are not found in the game dump.</exception>
    public void MergeInto(string changelogPath, string outputPath) {
        if (changelogPath == null)
            throw new ArgumentNullException(nameof(changelogPath));
        if (outputPath == null)
            throw new ArgumentNullException(nameof(outputPath));
        
        // Copy over vanilla files first
        var vanillaGdlPath = Path.Combine(config.GamePath, "GameData");

        if (!Directory.Exists(vanillaGdlPath))
            throw new Exception($"Failed to find vanilla GDL files at {vanillaGdlPath}");

        var vanillaGdlFiles = Directory.GetFiles(vanillaGdlPath)
                                       .Where(l => Path.GetFileName(l).StartsWith("GameDataList.Product") &&
                                                   Path.GetFileName(l).EndsWith(".byml.zs"));

        foreach (var vanillaFile in vanillaGdlFiles) {
            var outputGdl = Path.Combine(outputPath, Path.GetFileName(vanillaFile));

            Directory.CreateDirectory(Path.GetDirectoryName(outputGdl)!);

            if (!File.Exists(outputGdl))
                CopyHelper.CopyFile(vanillaFile, outputGdl);
            
        }

        var gdlFiles = Directory.GetFiles(outputPath)
                                .Where(l => Path.GetFileName(l).StartsWith("GameDataList.Product"))
                                .ToList();

        var changelogBytes = File.ReadAllBytes(changelogPath);

        foreach (var gdlFile in gdlFiles) {
            var gdlFileBytes = archiveHelper.GetFlatFileContents(gdlFile, true, out var dictionaryId);
            var merger = new GameDataListMerger();

            var resultBytes = merger.Merge(gdlFileBytes, changelogBytes);

            archiveHelper.WriteFlatFileContents(gdlFile, resultBytes, true, dictionaryId);
        }
    }
    
}