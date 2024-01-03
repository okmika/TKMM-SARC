using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using TKMM.SarcTool.Common;

namespace TKMM.SarcTool.Plugins;

public class HandlerManager : IHandlerManager {

    private readonly IPluginManager pluginManager;
    private readonly IServiceProvider serviceProvider;

    public HandlerManager(IPluginManager pluginManager, IServiceProvider serviceProvider) {
        this.pluginManager = pluginManager;
        this.serviceProvider = serviceProvider;
    }
    
    public ISarcFileHandler? GetHandlerInstance(string extension) {
        var handlers = pluginManager.GetHandlers();

        if (!handlers.TryGetValue(extension.ToLower(), out var handler))
            return null;

        return serviceProvider.GetRequiredService(handler) as ISarcFileHandler;
    }

    public ImmutableHashSet<string> GetSupportedExtensions() {
        return pluginManager.GetHandlers().Keys.ToImmutableHashSet();
    }
}