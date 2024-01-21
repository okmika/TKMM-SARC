# SARC/BYML Packager and Merger

This project is part of [TKMM](https://github.com/tcml-team) and is used as part of the mod manager's functionality. 
It can also be used standalone if need be, using the instructions below.

## What does the tool do?

`SarcTool` has two modes of operation: `package` and `merge`:

- The `package` mode examines all SARC archives inside and removes any duplicated assets
that have not been modified from their vanilla versions. This reduces the size of a mod and
prevents conflicts where the author did not intend to modify an asset.
- The `merge` mode accepts multiple mod folders and will combine all of the assets inside of
all included SARC archives, based on priority. For example, if two mods provide the
the same package file, `SarcTool` will evaluate its contents and smartly combine its contents. 
It will, for example, merge `.byml` files together, attempting to combine modified values
from each mod. In cases where this kind of merging is not possible, `SarcTool` will replace the
file with the one provided by a mod that has higher priority.

## How to use the tool

### For packaging archives
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
  --versions <versions>         Versions to try and package against [default: 100|110|111|120|121]
  --verbose                     Enable verbose output
  -?, -h, --help                Show help and usage information

```

### For merging archives
`SarcTool.exe merge --mods [modlist] --base [base] --output [output]`

- `base`: The path to the folder that holds all of your mods
- `modlist`: A list of mod folders inside of `base` to merge together, listed in priority (mods listed first have higher priority).
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
  --mods <mods> (REQUIRED)      A list of mod folder names, within the base mod folder, to merge, in order of priority
  --output <output> (REQUIRED)  Merged mods output directory
  --config <config>             Path to the TKMM config json. Default if not specified.
  --process <All|Archive|Flat>  Specify what type of merge to perform
  --verbose                     Enable verbose output
  -?, -h, --help                Show help and usage information
```

### For merging flat files

You can run the `merge` command with the option `--flat` to perform the merge function on flat
files that are not in archives, provided they are in a format that is supported by included plugins.

When using the `--flat` option, **only** flat files will be merged and archives will remain untouched.

## Plugins

Smart merging is supported by handlers defined in plugins. 

Currently, we officially support the merging
of `.byml` and `.bgyml` files with the included BYML plugin. You can write your own plugins to support additional
file formats, and SarcTool will load your plugin as long as its DLL is in the same folder as the executable, and is named
appropriately. See below for more information about writing your own plugins.

### Listing Available Plugins

Running SarcTool with the `showplugins` command will show a list of valid, loadable plugins and their supported
formats. If your custom plugin is not shown when running this command, it will not be used by SarcTool to process
your file type.

### Creating a Custom Plugin

Create a new C# class library and add a reference to `TKMM.SarcTool.Common`. 

A plugin consists of two parts: the plugin definition and the handler. First, create a new class that derives from
`SarcPlugin` and implement the required properties. Next, create a new handler class that implements `ISarcFileHandler`.
This file handler performs all of the operations and returns the merged version of the file to SarcTool.

Take a look at the included BYML Plugin for an example implementation.

In order to ensure your plugin is loaded, make sure your DLL is in the same directory as the SarcTool executable. Its
name must also start with `TKMM.SarcTool.Plugin` (for example, `TKMM.SarcTool.Plugin.Byml.dll`). If you run the
`showplugins` command on the SarcTool executable and your plugin is listed, it will be used when processing files 
that are in the format(s) you support.
