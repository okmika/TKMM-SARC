using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Spectre.Console;

namespace TKMM.SarcTool.Services;

public class ConfigService {

    public ConfigJson GetConfig(string path) {
        try {
            if (!File.Exists(path))
                return new ConfigJson();
            
            var configContents = File.ReadAllText(path);
            var deserialized = JsonConvert.DeserializeObject<ConfigJson>(configContents);

            return deserialized ?? new ConfigJson();
        } catch (Exception exc) {
            AnsiConsole.WriteException(exc, ExceptionFormats.ShortenEverything);
            AnsiConsole.MarkupLine("[yellow]Failed to read configuration.[/]");
            return new ConfigJson();
        }
    }

    public List<ShopsJsonEntry> GetShops(string path) {
        try {
            if (!File.Exists(path))
                return new List<ShopsJsonEntry>();
            
            var contents = File.ReadAllText(path);
            var deserialized = JsonConvert.DeserializeObject<List<ShopsJsonEntry>>(contents);

            return deserialized ?? new List<ShopsJsonEntry>();
        } catch (Exception exc) {
            AnsiConsole.WriteException(exc, ExceptionFormats.ShortenEverything);
            AnsiConsole.MarkupLine("[yellow]Failed to read shops JSON.[/]");
            return new List<ShopsJsonEntry>();
        }
    }

}

public class ConfigJson {
    public string? GamePath { get; set; }
}

public class ShopsJsonEntry {
    [JsonProperty("NPC ActorName")]
    public string ActorName { get; set; }
}