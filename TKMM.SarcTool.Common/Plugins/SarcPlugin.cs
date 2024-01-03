using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;

namespace TKMM.SarcTool.Common;

public abstract class SarcPlugin {

    public abstract string Name { get; }
    public abstract HashSet<string> Extensions { get; }
    public abstract HandlerDefinitions Definitions { get; }

    public virtual void Initialize(IServiceCollection services) {

    }

    public virtual void Configure(IServiceProvider serviceProvider) {
        
    }
}

public class HandlerDefinitions {
    private readonly ReadOnlyDictionary<string, Type> handlers;
    private readonly ImmutableHashSet<string> availableHandlers;
    
    public HandlerDefinitions(IDictionary<string, Type> fromDictionary) {
        handlers = new ReadOnlyDictionary<string, Type>(fromDictionary.ToDictionary(l => l.Key.ToLower(), l => l.Value));
        availableHandlers = ImmutableHashSet.Create(handlers.Keys.ToArray());
    }

    public ImmutableHashSet<string> AvailableHandlers => availableHandlers;

    public Type? GetHandlerForType(string extension) {
        if (!availableHandlers.Contains(extension.ToLower()))
            return null;

        return handlers[extension.ToLower()];
    }
}