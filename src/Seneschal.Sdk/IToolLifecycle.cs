using Microsoft.Extensions.DependencyInjection;

namespace Seneschal.Sdk;

/// <summary>
/// Optional, implemented by the tool class next to <see cref="IToolModule"/>, for a tool that needs its own services or
/// background work. Both methods are optional: implement only the one the tool needs.
/// The services an extension registers live in a container of its own, made when the extension is loaded and disposed when it is
/// replaced or removed (a singleton that implements IDisposable is disposed then: stop timers and background work there), so
/// an extension with services loads, updates and unloads live like any other.
/// </summary>
public interface IToolLifecycle
{
    /// <summary>Called when the extension is loaded, before its container is made: register this tool's own services here.</summary>
    void ConfigureServices(IServiceCollection services, IExtensionContext context) { }

    /// <summary>Called once the extension is loaded, with its services available: start background work or pick up earlier state. Called again for a new build.</summary>
    void Started(IServiceProvider services, IExtensionContext context) { }
}
