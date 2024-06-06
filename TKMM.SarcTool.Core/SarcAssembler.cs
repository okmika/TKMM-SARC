using System.Diagnostics;
using System.Text.Json;
using SarcLibrary;
using TotkCommon;

namespace TKMM.SarcTool.Core;

/// <summary>
/// Assembles all loose BYML files that were ordinarily
/// a part of a SARC archive and returns them to their
/// original archive. If the loose BYML file is modified,
/// the modified version will be written to the original
/// SARC archive.
/// </summary>
public class SarcAssembler {
    
    private readonly Totk config;
    private readonly ArchiveHelper archiveHelper;
    private readonly ArchiveCache archiveCache;
    private readonly string modPath;

    /// <summary>
    /// Create an instance of the <see cref="SarcAssembler"/> class.
    /// </summary>
    /// <param name="modPath">The full path to the mod to perform assembly on. This folder should contain the "romfs" folder.</param>
    /// <param name="configPath">
    ///     The path to the location of the "config.json" file in standard NX Toolbox format, or
    ///     null to use the default location in local app data.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if any of the required parameters are null.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown if any of the configuration files are not found, or if the compression
    ///     dictionary is missing.
    /// </exception>
    public SarcAssembler(string modPath, string? configPath = null) {
        if (!modPath.Contains($"{Path.DirectorySeparatorChar}romfs"))
            throw new ArgumentException("Path must be to the \"romfs\" folder of the mod", nameof(modPath));
        
        ArgumentNullException.ThrowIfNull(modPath);
        
        configPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Totk", "config.json");

        if (!File.Exists(configPath))
            throw new Exception($"{configPath} not found");

        using FileStream fs = File.OpenRead(configPath);
        this.config = JsonSerializer.Deserialize<Totk>(fs)
            ?? new();

        if (String.IsNullOrWhiteSpace(this.config.GamePath))
            throw new Exception("Game path is not defined in config.json");

        var compressionPath = Path.Combine(this.config.GamePath, "Pack", "ZsDic.pack.zs");
        if (!File.Exists(compressionPath)) {
            throw new Exception("Compression package not found: {this.config.GamePath}");
        }

        var compression = new ZsCompression(compressionPath);
        archiveHelper = new ArchiveHelper(compression);
        archiveCache = new ArchiveCache(configPath, compression);

        this.modPath = modPath;
    }

    /// <summary>
    /// <para>Perform assembly on the selected mod.</para>
    ///
    /// <para>
    /// WARNING: This operation destructively overwrites
    /// files in the mod folder and thus should be performed on a copy of the mod in case
    /// the changes need to be reversed.</para>
    /// </summary>
    public void Assemble() {
        archiveCache.Initialize();
        InternalAssemble();
    }

    /// <summary>
    /// <para>Perform assembly on the selected mod asynchronously.</para>
    ///
    /// <para>
    /// WARNING: This operation destructively overwrites
    /// files in the mod folder and thus should be performed on a copy of the mod in case
    /// the changes need to be reversed.</para>
    /// </summary>
    /// <returns>A task that represents the assembly work queued on the task pool.</returns>
    public async Task AssembleAsync() {
        await Task.Run(Assemble);
    }

    private void InternalAssemble() {

        var supportedExtensions = new[] {"byml", "byaml"};

        var flatFiles = Directory.GetFiles(modPath, "*", SearchOption.AllDirectories)
                                 .Where(l => supportedExtensions.Any(
                                            ext => l.EndsWith(ext) || l.EndsWith(ext + ".zs")))
                                 .ToList();

        foreach (var file in flatFiles) {
            var relativeFilePath = archiveHelper.GetRelativePath(file, modPath);
            
            if (!archiveCache.TryGetValue(relativeFilePath, out var archiveRelativePath)) {
                continue;
            }

            if (!MergeIntoArchive(archiveRelativePath, file, relativeFilePath)) {
                Trace.TraceWarning("Skipping {0} - could not assemble", file);
                continue;
            }
            
            // Success means we delete the flat file
            File.Delete(file);

        }
        
    }

    private bool MergeIntoArchive(string archiveRelativePath, string filePath, string fileRelativePath) {

        var archivePath = archiveHelper.GetAbsolutePath(archiveRelativePath, modPath);

        // First test the existing archive
        if (!File.Exists(archivePath))
            archivePath += ".zs";
        if (!File.Exists(archivePath) && !CopyVanillaArchive(archiveRelativePath, archivePath))
            return false;

        var isCompressed = archivePath.EndsWith(".zs");
        var archiveContents = archiveHelper.GetFileContents(archivePath, isCompressed, true);
        var sarc = Sarc.FromBinary(archiveContents.ToArray());

        var isFileCompressed = filePath.EndsWith(".zs");
        var fileContents = archiveHelper.GetFileContents(filePath, isFileCompressed, false);

        // Skip if the SARC doesn't contain the file already
        if (!sarc.ContainsKey(fileRelativePath)) {
            sarc.Add(fileRelativePath, fileContents.ToArray());
        } else {
            sarc[fileRelativePath] = fileContents.ToArray();
        }

        archiveHelper.WriteFileContents(archivePath, sarc, isCompressed, true);
        return true;

    }

    private bool CopyVanillaArchive(string archiveRelativePath, string destination) {
        var vanillaPath = archiveHelper.GetAbsolutePath(archiveRelativePath, config.GamePath);

        if (!File.Exists(vanillaPath))
            vanillaPath += ".zs";
        if (!File.Exists(vanillaPath))
            return false;

        CopyHelper.CopyFile(vanillaPath, destination);
        return true;
    }
    
    
}