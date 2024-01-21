using Microsoft.Extensions.DependencyInjection;
using TKMM.SarcTool.Common;

namespace TKMM.SarcTool.Plugin.BymlPlugin;

public class BymlPlugin : SarcPlugin {
    public override string Name => "Byml Plugin for TKMM SARC Tool";
    public override HashSet<string> Extensions => extensions;
    public override HandlerDefinitions Definitions => definitions;

    private readonly HashSet<string> extensions;
    private HandlerDefinitions definitions;

    public BymlPlugin() {
        extensions = new HashSet<string>(new[] {
            "byml", "bgyml"
        });

        definitions = new HandlerDefinitions(new Dictionary<string, Type>() {
            ["byml"] = typeof(BymlHandler),
            ["bgyml"] = typeof(BymlHandler)
        });
    }

}