using Newtonsoft.Json;

namespace TKMM.SarcTool.Core.Model;

internal class ConfigJson {
    public string? GamePath { get; set; }

    public static ConfigJson Load(string path) {
    
        if (!File.Exists(path))
            return new ConfigJson();

        var configContents = File.ReadAllText(path);
        var deserialized = JsonConvert.DeserializeObject<ConfigJson>(configContents);

        return deserialized ?? new ConfigJson();
    
    }

    
}

internal class ShopsJsonEntry {
    [JsonProperty("NPC ActorName")]
    public string ActorName { get; set; }

    public static List<ShopsJsonEntry> Load(string path) {
        
        if (!File.Exists(path))
            return new List<ShopsJsonEntry>();

        var contents = File.ReadAllText(path);
        var deserialized = JsonConvert.DeserializeObject<List<ShopsJsonEntry>>(contents);

        return deserialized ?? new List<ShopsJsonEntry>();
       
    }
}