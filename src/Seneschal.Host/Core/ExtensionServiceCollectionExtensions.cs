using Seneschal.Dev;

namespace Seneschal.Core;

public static class ExtensionServiceCollectionExtensions
{
    /// <summary>Extension on/off, import and remove, the extensions' static files, and the dev-only source tools.</summary>
    public static IServiceCollection AddExtensionManagement(this IServiceCollection services)
    {
        services.AddSingleton<ExtensionSettings>();
        services.AddSingleton<ShellLauncher>();
        services.AddSingleton<ExtensionAssets>();
        services.AddSingleton<DllExtensions>();
        services.AddSingleton<DevMode>();
        services.AddSingleton<Scaffolder>();
        services.AddSingleton<Workspace>();
        services.AddSingleton<DotnetSdk>();
        services.AddSingleton<Authoring>();
        services.AddSingleton<DevProjects>();
        services.AddSingleton<Restarter>();
        services.AddSingleton<DevWatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<DevWatcher>());
        return services;
    }

    /// <summary>
    /// Loads the extensions that are installed or ship with the app and lets them register their services. This has to run
    /// before the container is built, which is why it is a separate call from <see cref="AddExtensionManagement"/>.
    /// </summary>
    public static IServiceCollection AddExtensions(this IServiceCollection services, IHostEnvironment environment)
    {
        var startup = new ExtensionStartup();
        startup.LoadAll(services, ExtensionStartup.DevFolders(environment));
        services.AddSingleton(startup);
        return services;
    }
}
