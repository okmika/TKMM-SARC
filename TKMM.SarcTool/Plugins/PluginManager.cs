using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using TKMM.SarcTool.Common;

namespace TKMM.SarcTool.Plugins;

public class PluginManager : IPluginManager {

    private ImmutableList<SarcPlugin> loadedPlugins = new List<SarcPlugin>().ToImmutableList();
    private ImmutableDictionary<string, Type> handlerByExtension = new Dictionary<string, Type>().ToImmutableDictionary();

    private readonly string applicationDirectory;
    private bool loaded = false;

    public PluginManager() {
        applicationDirectory = Path.GetDirectoryName((Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).Location)
                               ?? throw new Exception("Plugin path not found");
    }

    internal void LoadPlugins(IServiceCollection services) {
        if (loaded)
            throw new InvalidOperationException("Plugins already loaded");
        
        var pluginAssemblies = Directory.GetFiles(applicationDirectory, searchPattern: "*.dll");

        var loadContext = AssemblyLoadContext.Default;
        loadContext.Resolving += LoadContextOnResolving;

        var plugins = new List<SarcPlugin>();
        var handlers = new Dictionary<string, Type>();

        foreach (var file in pluginAssemblies) {
            // Only init assemblies that have the filename in the format TKMM.SarcTool.Plugin.xxxx.dll
            if (!Path.GetFileName(file).ToLower().StartsWith("tkmm.sarctool.plugin."))
                continue;

            try {
                var assembly = loadContext.LoadFromAssemblyPath(file);
                var exportedTypes = assembly.GetExportedTypes();

                foreach (var type in exportedTypes.Where(l => l.GetTypeInfo().IsSubclassOf(typeof(SarcPlugin)))) {
                    var instance = (SarcPlugin?)Activator.CreateInstance(type);

                    if (instance == null)
                        continue;

                    instance.Initialize(services);
                    plugins.Add(instance);

                    foreach (var item in instance.Definitions.AvailableHandlers) {
                        if (handlers.ContainsKey(item.ToLower()))
                            throw new Exception($"Another plugin already handles file type {item}");

                        var handlerForType = instance.Definitions.GetHandlerForType(item);

                        if (handlerForType == null)
                            throw new Exception($"Handler for extension {item} not defined");

                        // Add handler and register with service provider
                        handlers.Add(item.ToLower(), handlerForType);
                        services.AddTransient(handlerForType);
                    }
                }

                loadedPlugins = plugins.ToImmutableList();
                handlerByExtension = handlers.ToImmutableDictionary();
            } catch (Exception exc) {
                throw new PluginLoadException($"Failed to load plugin {file}", exc);
            }
        }
    }

    public ImmutableList<SarcPlugin> GetPlugins() {
        return loadedPlugins;
    }

    public ImmutableDictionary<string, Type> GetHandlers() {
        return handlerByExtension;
    }

    private Assembly? LoadContextOnResolving(AssemblyLoadContext context, AssemblyName assembly) {
        if (assembly.Name == null)
            return null;
        
        var potentialPath = Path.Combine(applicationDirectory, $"{assembly.Name}.dll");

        if (!File.Exists(potentialPath))
            return null;

        return context.LoadFromAssemblyPath(potentialPath);
    }

    
}