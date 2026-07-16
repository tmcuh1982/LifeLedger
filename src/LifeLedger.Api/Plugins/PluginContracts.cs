using System.Reflection;
using System.Runtime.Loader;
using LifeLedger.Api.Services;

namespace LifeLedger.Api.Plugins;

/// <summary>Public contract for a compiled LifeLedger extension placed in the configured plugins directory.</summary>
public interface ILifeLedgerPlugin
{
    /// <summary>Stable, machine-readable identifier of the plugin.</summary>
    string Id { get; }
    /// <summary>Name shown in the application and diagnostics.</summary>
    string DisplayName { get; }
    /// <summary>Version supplied by the plugin author.</summary>
    Version Version { get; }
    /// <summary>Registers the plugin's capabilities with the application.</summary>
    void Configure(PluginContext context);
}

/// <summary>Exposes the safe registration points available to a plugin during startup.</summary>
public sealed class PluginContext
{
    /// <summary>Projection modifiers collected for the shared projection engine.</summary>
    private readonly List<IProjectionModifier> _projectionModifiers;

    /// <summary>Creates a context backed by the registry's modifier collection.</summary>
    internal PluginContext(List<IProjectionModifier> projectionModifiers) => _projectionModifiers = projectionModifiers;

    /// <summary>Adds a modifier that can adjust each annual projection row.</summary>
    public void AddProjectionModifier(IProjectionModifier modifier) => _projectionModifiers.Add(modifier);
}

/// <summary>Discovers compatible compiled plugins from the configured local directory.</summary>
public sealed class PluginRegistry
{
    /// <summary>Registered calculation modifiers exposed to the projection engine.</summary>
    private readonly List<IProjectionModifier> _projectionModifiers = [];
    /// <summary>Descriptors of successfully loaded plugins.</summary>
    private readonly List<PluginDescriptor> _plugins = [];
    /// <summary>Read-only view of the active calculation modifiers.</summary>
    public IReadOnlyList<IProjectionModifier> ProjectionModifiers => _projectionModifiers;
    /// <summary>Read-only view of plugins that were successfully loaded.</summary>
    public IReadOnlyList<PluginDescriptor> Plugins => _plugins;

    /// <summary>Loads each compatible DLL independently so one faulty plugin cannot stop the application.</summary>
    public void Load(string pluginDirectory, ILogger logger)
    {
        if (!Directory.Exists(pluginDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
                // Only concrete implementations of the public plugin contract are instantiated.
                var types = assembly.GetTypes().Where(x => !x.IsAbstract && typeof(ILifeLedgerPlugin).IsAssignableFrom(x));
                foreach (var type in types)
                {
                    if (Activator.CreateInstance(type) is not ILifeLedgerPlugin plugin) continue;
                    plugin.Configure(new PluginContext(_projectionModifiers));
                    _plugins.Add(new PluginDescriptor(plugin.Id, plugin.DisplayName, plugin.Version.ToString()));
                    logger.LogInformation("Loaded LifeLedger plugin {PluginId} v{Version}", plugin.Id, plugin.Version);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not load plugin assembly {PluginPath}", path);
            }
        }
    }
}

/// <summary>Public metadata describing a successfully loaded plugin.</summary>
public sealed record PluginDescriptor(string Id, string DisplayName, string Version);
