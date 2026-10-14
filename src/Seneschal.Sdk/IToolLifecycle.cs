using Microsoft.Extensions.DependencyInjection;

namespace Seneschal.Sdk;

/// <summary>
/// Optional, implemented by the tool class next to <see cref="IToolModule"/>, for a tool that needs its own services or
/// background work. Both methods are optional: implement only the one the tool needs.
/// Services are part of the app's container, which is built once at startup. So an extension that registers any
/// services is activated, updated and removed by a restart; an extension that registers none loads and unloads live.
/// </summary>
public interface IToolLifecycle
{
    /// <summary>Called while the app is being set up, before the container is built: register this tool's own services here.</summary>
    void ConfigureServices(IServiceCollection services, IExtensionContext context) { }

    /// <summary>Called once after the app is built and before it serves requests: start background work or pick up earlier state.</summary>
    void Started(IServiceProvider services, IExtensionContext context) { }
}
