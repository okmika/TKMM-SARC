# Library Documentation
You can get the latest version of the SarcTool library on [NuGet](https://www.nuget.org/packages/TKMM.SarcTool.Core).
For the command line tool's parameters, see the [main README](https://github.com/okmika/TKMM-SARC/blob/main/README.md).

For documentation on the changelogs created by the tool, please see [changelogs.md](https://github.com/okmika/TKMM-SARC/blob/main/docs/changelogs.md).

## Assembling
```csharp
var modPath = @"C:\UnpackagedMods\ModOne";

var assembler = new SarcAssembler(modPath);
assembler.Assemble();
```

> ### Important!
> **Assembling is a destructive operation** - it will delete flat files
> from your mod folder as it places them into their respective
> archives. Please ensure you are operating on a copy of your
> mod source.

## Packaging
```csharp
var outputPath = @"C:\MyMods\ModOne";
var modPath = @"C:\UnpackagedMods\ModOne";

var packager = new SarcPackager(outputPath, modPath);
packager.Package();
```

## Merging
```csharp
var modsToMerge = new[] { "C:\MyMods\ModOne", "C:\MyMods\ModTwo" };
var outputPath = @"C:\MergedMods";

var merger = new SarcMerger(modsToMerge, outputPath);
merger.Merge();
```

You may also use the asynchronous alternatives: `AssembleAsync`, `PackageAsync` or `MergeAsync`.

