using System.Text.Json;
using System.Text.Json.Serialization;

namespace TKMM.SarcTool.Core.Model;

#nullable disable
internal class ShopsJsonEntry {
    [JsonPropertyName("NPC ActorName")]
    public string ActorName { get; set; }

    public static List<ShopsJsonEntry> Load(string path) {
        
        if (!File.Exists(path))
            return new List<ShopsJsonEntry>();

        var contents = File.ReadAllText(path);
        var deserialized = JsonSerializer.Deserialize<List<ShopsJsonEntry>>(contents);

        return deserialized ?? new List<ShopsJsonEntry>();
       
    }
}
#nullable restore