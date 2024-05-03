# SARC/BYML Packager and Merger

This project is part of [TKMM](https://github.com/tkmm-team) and is used as part of the mod manager's functionality. 
It can also be used standalone if need be, using the instructions below.

## Library

There is a [library](https://github.com/okmika/TKMM-SARC/blob/main/docs/README.md) that you can use in your own executables
to assemble, package, and merge SARC archives, BYML files, and GameDataList files.

Alternatively, you can use the executables to perform all of those operations from the command line.

## What does the executable tool do?

`SarcTool` has three primary modes of operation: `assemble`, `package` and `merge`:

- The `assemble` mode places all flat files that are originally included inside of archives in the vanilla version
of the game back into their respective original archives. This is useful if you develop a mod without
placing the files into their associated archive.
  > **It's recommended you run this step before packaging the mod.** 
  > If you don't, you will find that a proper change log will not be generated for your flat files, 
  > causing mod conflicts. 
  
- The `package` mode examines all SARC archives and removes any duplicated assets within
that have not been modified from their vanilla versions. This reduces the size of a mod and
prevents conflicts where the author did not intend to modify an asset. Packaging also
compares loose `byml` or `bgyml` files with their vanilla versions and creates a special
copy of the file that contains only the changes. Packaging will also create a changelog
for any GameDataList files inside of the mods.


- The `merge` mode accepts multiple mod folders and will combine all of the assets inside of
all included SARC archives, based on priority. For example, if two mods provide the
the same package file, `SarcTool` will evaluate its contents and smartly combine its contents. 
It will, for example, merge `.byml` files together, attempting to combine modified values
from each mod. It will also process any change logs to GameDataList files and merge them in.
In cases where this kind of merging is not possible, `SarcTool` will replace the
file with the one provided by a mod that has higher priority.

## Assembling flat files

`SarcTool.exe assemble --mod [mod]`

- `mod`: The path to the mod folder you would like to assemble flat files in.

> **IMPORTANT**
>
> The `assemble` command will overwrite files in your mod folder. Please make sure you are working on a copy
> of everything in case you need to roll back the changes made.

Example:
```
SarcTool.exe assemble --mod "C:\My Mods\My Great Mod Folder"
```

```
Usage:
  TKMM.SarcTool assemble [options]

Options:
  --mod <mod> (REQUIRED)        Path to the mod to perform the assembly on
  --config <config>             Path to the TKMM configuration files (config.json). Default if not specified.
  -?, -h, --help                Show help and usage information
```

> **Note**: The first time you run `assemble`, SarcTool will build a database of files inside the archives. If you
> update your game's version, you should delete the `archivemappings.bin` file in the TKMM configuration folder
> (typically in `%localappdata%\totk` on Windows) to allow SarcTool to regenerate the database, otherwise the assembly
> function will not work as intended.

## Packaging archives
`SarcTool.exe package --mod [mod] --output [output]`

- `mod`: The path to the mod folder you would like to package up
- `output`: The path to the destination folder you would like the packaged mod to be written to.

Example:
```
SarcTool.exe package --mod "C:\My Mods\My Great Mod Folder" --output "C:\My Mods\combined\My Great Mod"
```

Some optional parameters exist. Here is the full help, which can be accessed any time by running the tool
without the required parameters:

```
Usage:
  TKMM.SarcTool package [options]

Options:
  --mod <mod> (REQUIRED)        Path to the mod to perform the packaging on
  --output <output> (REQUIRED)  Merged mods output directory
  --config <config>             Path to the TKMM config json. Default if not specified.
  --checksum <checksum>         Path to the TKMM checksum database. Default if not specified.
  --versions <versions>         Versions to try and package against [default: 100|110|111|112|120|121]
  -?, -h, --help                Show help and usage information

```

## Merging archives
`SarcTool.exe merge --mods [modlist] --base [base] --output [output]`

- `base`: The path to the folder that holds all of your mods
- `modlist`: A list of mod folders inside of `base` to merge together, listed in priority (in order of lowest to highest priority).
Higher priority mods will overwrite conflicting options of lower priority mods.
- `output`: The path to the destination folder you would like the merged mod files to be written to.

Example: 

```
SarcTool.exe merge --base "C:\My Mods" --mods "My Great Mod" "Another Great Mod" --output "C:\My Mods\combined"
```

Some optional parameters exist. Here is the full help, which can be accessed any time by running the tool
without the required parameters:

```
Usage:
  TKMM.SarcTool merge [options]

Options:
  --base <base> (REQUIRED)      The base folder path containing the mod subfolders
  --mods <mods> (REQUIRED)      A list of mod folder names, within the base mod folder, to merge, in order of priority, from lowest to highest
  --output <output> (REQUIRED)  Merged mods output directory
  --config <config>             Path to the TKMM config json. Default if not specified.
  -?, -h, --help                Show help and usage information
```

## Miscellaneous Commands

### Comparing GameDataList Files

You can use the `compare` command to compare two GameDataList files and see if there are any differences. The tool 
won't tell you what differences there are, just that they are different. This is not a byte-for-byte compare; the
tool compares all of the entries logically to determine if there are any changes.

```
Description:
  Compare two GameDataList files for differences

Usage:
  TKMM.SarcTool comparegdl [options]

Options:
  --files <files> (REQUIRED)  Path to the two GDL files to compare
  --config <config>           Path to the TKMM configuration files (config.json). Default if not specified.
  --verbose                   Enable verbose output
  -?, -h, --help              Show help and usage information

```