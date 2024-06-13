using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using Spectre.Console;
using TKMM.SarcTool.Core;

namespace TKMM.SarcTool;

public static class Program {

    public static int Main(string[] args) {

        Trace.Listeners.Add(new ConsoleTraceListener(false));
        PrintBanner();
        
        var rootCommand = GetCommandLine();
        return rootCommand.Invoke(args);
    }

    private static RootCommand GetCommandLine() {
        var mergeCommand = new Command("merge", "Perform a merge of multiple packaged mods");
        var packageCommand = new Command("package", "Package up a mod for distribution");
        var assembleCommand = new Command("assemble", "Collect loose files in a mod and return them to their proper archives");
        var compareCommand = new Command("comparegdl", "Compare two GameDataList files for differences");
        

        var verboseOption = new Option<bool>("--verbose", "Enable verbose output");

        MakeMergeCommand(mergeCommand, verboseOption);
        MakePackageCommand(packageCommand, verboseOption);
        MakeAssembleCommand(assembleCommand, verboseOption);
        MakeCompareCommand(compareCommand, verboseOption);
        
        var rootCommand = new RootCommand();
        rootCommand.Add(assembleCommand);
        rootCommand.Add(packageCommand);
        rootCommand.Add(mergeCommand);
        rootCommand.Add(compareCommand);
        
        return rootCommand;
    }

    private static void MakeCompareCommand(Command compareCommand, Option<bool> verboseOption) {
        var files = new Option<string[]>("--files", "Path to the two GDL files to compare") {
            IsRequired = true,
            AllowMultipleArgumentsPerToken = true
        }.LegalFilePathsOnly();
        
        var configPath = new Option<string?>(
                "--config",
                "Path to the TKMM configuration files (config.json). Default if not specified.")
            .LegalFileNamesOnly();

        compareCommand.AddOption(files);
        compareCommand.AddOption(configPath);

        compareCommand.SetHandler((files, configPath) => RunCompare(files, configPath), files, configPath);


    }
    
    private static void MakeAssembleCommand(Command assembleCommand, Option<bool> verboseOption) {
        var assembleCommandModOption = new Option<string>("--mod", "Path to the mod to perform the assembly on") {
                IsRequired = true
            }
            .LegalFilePathsOnly();

        var assembleCommandConfigOption = new Option<string?>(
                "--config",
                "Path to the TKMM configuration files (config.json). Default if not specified.")
            .LegalFileNamesOnly();

        assembleCommand.AddOption(assembleCommandModOption);
        assembleCommand.AddOption(assembleCommandConfigOption);

        assembleCommand.SetHandler((modPath, configPath) =>
                                       RunAssemble(modPath, configPath),
                                   assembleCommandModOption, assembleCommandConfigOption);
    }

    private static void MakePackageCommand(Command packageCommand, Option<bool> verboseOption) {
        var packageCommandModOption = new Option<string>("--mod", "Path to the mod to perform the packaging on") {
                IsRequired = true
            }
            .LegalFilePathsOnly();

        var packageCommandOutputOption = new Option<string>("--output", "Merged mods output directory") {
                IsRequired = true
            }
            .LegalFilePathsOnly();

        var packageCommandVersionsOption = new Option<int[]>("--versions", "Versions to try and package against");
        packageCommandVersionsOption.SetDefaultValue(new[] {100, 110, 111, 112, 120, 121});
        packageCommandVersionsOption.AddValidator(val => {
            if (!val.Tokens.All(l => Int32.TryParse(l.Value, out _)))
                val.ErrorMessage = "Specified versions must be a number.";
        });

        var packageCommandConfigOption = new Option<string?>(
                "--config",
                "Path to the TKMM configuration files (config.json). Default if not specified.")
            .LegalFileNamesOnly();

        var packageCommandChecksumOption = new Option<string?>(
                "--checksum",
                "Path to the TKMM checksum database. Default if not specified."
            )
            .LegalFileNamesOnly();

        packageCommand.AddOption(packageCommandModOption);
        packageCommand.AddOption(packageCommandOutputOption);
        packageCommand.AddOption(packageCommandConfigOption);
        packageCommand.AddOption(packageCommandChecksumOption);
        packageCommand.AddOption(packageCommandVersionsOption);

        packageCommand.SetHandler((outputPath, modPath, configPath, checksumPath, versions, verbose) =>
                                      RunPackage(outputPath, modPath, configPath, checksumPath, versions, verbose),
                                  packageCommandOutputOption,
                                  packageCommandModOption, packageCommandConfigOption, packageCommandChecksumOption,
                                  packageCommandVersionsOption, verboseOption);
    }

    private static void MakeMergeCommand(Command mergeCommand, Option<bool> verboseOption) {
        var mergeCommandModsOption = new Option<IEnumerable<string>>(
            "--mods", "A list of mod folder names, within the base mod folder, to merge, in order of priority (lowest to highest)") {
            IsRequired = true,
            AllowMultipleArgumentsPerToken = true,
        };

        var mergeCommandBaseOption = new Option<string>("--base", "The base folder path containing the mod subfolders") {
                IsRequired = true
            }
            .LegalFilePathsOnly();

        var mergeConfigOption =
            new Option<string?>(
                "--config", "Path to the TKMM configuration file (config.json). Default if not specified.");

        var mergeCommandOutputOption = new Option<string>("--output", "Merged mods output directory") {
                IsRequired = true
            }
            .LegalFilePathsOnly();

        mergeCommand.AddOption(mergeCommandBaseOption);
        mergeCommand.AddOption(mergeCommandModsOption);
        mergeCommand.AddOption(mergeCommandOutputOption);
        mergeCommand.AddOption(mergeConfigOption);

        mergeCommand.SetHandler((modsList, basePath, outputPath, configPath, verbose) =>
                                    RunMerge(modsList, basePath, outputPath, configPath, verbose),
                                mergeCommandModsOption, mergeCommandBaseOption, mergeCommandOutputOption,
                                mergeConfigOption, verboseOption);
    }

   
    private static void RunAssemble(string modPath, string? configPath) {
        try {
            var assembler = new SarcAssembler(modPath, configPath);
            assembler.Assemble();
        } catch (Exception exc) {
            AnsiConsole.WriteException(exc, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes);
        }
    }

    private static void RunMerge(IEnumerable<string> modsList, string basePath, string outputPath, string? configPath, bool verbose) {
        try {
            var timer = new Stopwatch();
            timer.Start();

            var merger = new SarcMerger(modsList.Select(x => Path.Combine(basePath, x, "romfs")), outputPath, configPath);
            merger.Verbose = verbose;
            merger.Merge();

            timer.Stop();
            AnsiConsole.WriteLine($"Command completed in {timer.Elapsed}");
        } catch (Exception exc) {
            AnsiConsole.WriteException(exc, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes);
        }
    }

    private static void RunCompare(IEnumerable<string> files, string? configPath) {
        try {
            var filesArray = files.ToArray();

            if (filesArray.Length != 2) {
                AnsiConsole.MarkupLineInterpolated($"[red]Need to specify the path to two GDL files - abort[/]");
                return;
            }

            var merger = new SarcMerger(new string[0], Environment.ProcessPath!, configPath, null);
            var result = merger.HasGdlChanges(filesArray[0], filesArray[1]);

            if (result)
                AnsiConsole.MarkupLine("[red]Changes detected[/]");
            else
                AnsiConsole.MarkupLine("[green]No changes detected[/]");
        } catch (Exception exc) {
            AnsiConsole.WriteException(exc, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes);
        }
    }

    private static void RunPackage(string outputPath, string modPath, string? configPath, string? checksumPath, int[] versions, bool verbose) {
        
        try {
            var timer = new Stopwatch();
            timer.Start();
            
            var packager = new SarcPackager(outputPath, modPath, configPath, checksumPath, versions);
            packager.Verbose = verbose;
            packager.Package();

            timer.Stop();
            AnsiConsole.WriteLine($"Command completed in {timer.Elapsed}");
        } catch (Exception exc) {
            AnsiConsole.WriteException(exc, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes);
        }
    }

    
    private static void PrintBanner() {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = $"{assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown Version"}";
        
        AnsiConsole.Write(new FigletText("SARC Tool"));
        AnsiConsole.MarkupInterpolated(
            $"[bold][yellow]TKMM SARC Tool v. {version}\nhttps://github.com/okmika/TKMM-SARC[/][/]\n\n");
        
    }

    
    
}

public enum OperationMode {
    Package,
    Merge
}

public enum ProcessMode {
    All,
    Archive,
    Flat
}