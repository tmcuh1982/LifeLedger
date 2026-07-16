using System.Reflection;
using System.Runtime.Loader;
using LifeLedger.Api.Services;

namespace LifeLedger.Api.Plugins;

/// <summary>Public contract for a compiled LifeLedger extension placed in the configured plugins directory.</summary>
public interface ILifeLedgerPlugin
{
    string Id { get; }
    string DisplayName { get; }
    Version Version { get; }
    void Configure(PluginContext context);
}

public sealed class PluginContext
{
    private readonly List<IProjectionModifier> _projectionModifiers;
    internal PluginContext(List<IProjectionModifier> projectionModifiers) => _projectionModifiers = projectionModifiers;
    public void AddProjectionModifier(IProjectionModifier modifier) => _projectionModifiers.Add(modifier);
}

public sealed class PluginRegistry
{
    private readonly List<IProjectionModifier> _projectionModifiers = [];
    private readonly List<PluginDescriptor> _plugins = [];
    public IReadOnlyList<IProjectionModifier> ProjectionModifiers => _projectionModifiers;
    public IReadOnlyList<PluginDescriptor> Plugins => _plugins;

    public void Load(string pluginDirectory, ILogger logger)
    {
        if (!Directory.Exists(pluginDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
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

public sealed record PluginDescriptor(string Id, string DisplayName, string Version);
