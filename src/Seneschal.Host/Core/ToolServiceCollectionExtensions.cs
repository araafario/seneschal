using System.Reflection;
using Seneschal.Sdk;

namespace Seneschal.Core;

public static class ToolServiceCollectionExtensions
{
    /// <summary>What a tool that is compiled into the app is told about itself.</summary>
    private static readonly ExtensionContext HostContext = new("host", "Seneschal", SdkInfo.Version,
        Path.Combine(AppPaths.ExtensionDataDir, "host"), AppContext.BaseDirectory);

    /// <summary>
    /// Registers every non-abstract <see cref="IToolModule"/> in the assembly as a singleton, plus the registry, and lets a
    /// tool that implements <see cref="IToolLifecycle"/> register its own services.
    /// Adding a tool requires no registration line.
    /// </summary>
    public static IServiceCollection AddTools(this IServiceCollection services)
    {
        var moduleTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IToolModule).IsAssignableFrom(t));

        foreach (var type in moduleTypes)
        {
            services.AddSingleton(typeof(IToolModule), type);

            // The container does not exist yet, so a short-lived instance gives the tool its chance to register services
            if (typeof(IToolLifecycle).IsAssignableFrom(type))
                ((IToolLifecycle)Activator.CreateInstance(type)!).ConfigureServices(services, HostContext);
        }

        services.AddSingleton<IToolRegistry, ToolRegistry>();
        return services;
    }

    /// <summary>Tells the tools that implement <see cref="IToolLifecycle"/> that the app is built. One failing tool does not stop the others.</summary>
    public static void StartTools(this IServiceProvider services)
    {
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("Tools");
        foreach (var tool in services.GetServices<IToolModule>().OfType<IToolLifecycle>())
        {
            try
            {
                tool.Started(services, HostContext);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Tool {Tool} failed to start", tool.GetType().Name);
            }
        }
    }
}
