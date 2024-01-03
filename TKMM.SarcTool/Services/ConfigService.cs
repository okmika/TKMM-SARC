using Newtonsoft.Json;
using Spectre.Console;

namespace TKMM.SarcTool.Services;

public class ConfigService {

    public ConfigJson GetConfig(string path) {
        try {
            var configContents = File.ReadAllText(path);
            var deserialized = JsonConvert.DeserializeObject<ConfigJson>(configContents);

            return deserialized ?? new ConfigJson();
        } catch (Exception exc) {
            AnsiConsole.WriteException(exc, ExceptionFormats.ShortenEverything);
            AnsiConsole.Markup("[orange]Failed to read configuration.[/]");
            return new ConfigJson();
        }
    }

}

public class ConfigJson {
    public string? GamePath { get; set; }
}