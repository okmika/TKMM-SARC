using System.Collections.Immutable;

namespace TKMM.SarcTool.Common;

public interface IPluginManager {
    public ImmutableList<SarcPlugin> GetPlugins();
    public ImmutableDictionary<string, Type> GetHandlers();
}