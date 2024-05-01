using System.Collections.Immutable;

namespace TKMM.SarcTool.Core;

internal class HandlerManager {

    private readonly List<ISarcFileHandler> handlers;

    public HandlerManager() {
        handlers = new List<ISarcFileHandler>() {
            new BymlHandler()
        };
    }
    
    
    public ISarcFileHandler? GetHandlerInstance(string extension) {
        var result = handlers.FirstOrDefault(l => l.Extensions.Contains(extension));
        return result;
    }

    public ImmutableHashSet<string> GetSupportedExtensions() {
        return handlers.SelectMany(l => l.Extensions).ToImmutableHashSet();
    }
}