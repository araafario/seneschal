using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Seneschal.Core;

/// <summary>
/// One load context per extension. Libraries that ship in the package are loaded from memory; everything else
/// (the framework, MudBlazor, Seneschal.Sdk, anything the host already has) comes from the host.
/// </summary>
internal sealed class ExtensionLoadContext(string name, string folder, IReadOnlyDictionary<string, PackageLibrary> libraries)
    : AssemblyLoadContext($"extension:{name}", isCollectible: true)
{
    // The extension's UI and metadata types are exchanged with the host, so these must never be a second copy.
    private static readonly string[] AlwaysHost = ["MudBlazor", "Seneschal.Sdk"];

    private readonly Dictionary<string, Assembly> _loaded = new(StringComparer.OrdinalIgnoreCase);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var simple = assemblyName.Name;
        if (simple is null || !libraries.TryGetValue(simple, out var library) || HostProvides(library))
            return null;

        lock (_loaded)
        {
            if (!_loaded.TryGetValue(simple, out var assembly))
                _loaded[simple] = assembly = LoadFromStream(new MemoryStream(library.Bytes));
            return assembly;
        }
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var candidate in NativeCandidates(unmanagedDllName))
        {
            if (File.Exists(candidate)) return LoadUnmanagedDllFromPath(candidate);
        }
        return nint.Zero;
    }

    /// <summary>True when the host already provides this library in a version that is the same major and at least as new.</summary>
    public static bool HostProvides(PackageLibrary library) => Provides(library, frameworkOnly: false);

    /// <summary>
    /// Like <see cref="HostProvides"/> for a clean Seneschal: only the .NET framework, MudBlazor and the SDK count.
    /// The running dev app also contains the packages of its own source tools, which another app won't have.
    /// </summary>
    public static bool CleanHostProvides(PackageLibrary library) => Provides(library, frameworkOnly: true);

    /// <summary>True when the host can supply an assembly with this name.</summary>
    public static bool HostHas(string assemblyName) => HostVersion(assemblyName, frameworkOnly: false) is not null;

    private static bool Provides(PackageLibrary library, bool frameworkOnly)
    {
        if (AlwaysHost.Contains(library.Name, StringComparer.OrdinalIgnoreCase)) return true;
        var host = HostVersion(library.Name, frameworkOnly);
        return host is not null && host.Major == library.Version.Major && host >= library.Version;
    }

    private static Version? HostVersion(string assemblyName, bool frameworkOnly)
    {
        try
        {
            var assembly = Default.LoadFromAssemblyName(new AssemblyName(assemblyName));
            return frameworkOnly && !InFramework(assembly) ? null : assembly.GetName().Version;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static readonly string[] FrameworkDirs =
    [
        Path.GetDirectoryName(typeof(object).Assembly.Location)!,
        Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.Location)!,
    ];

    private static bool InFramework(Assembly assembly) =>
        FrameworkDirs.Any(d => assembly.Location.StartsWith(d, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<string> NativeCandidates(string unmanagedName)
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var rid = $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
        var extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        var names = Path.HasExtension(unmanagedName) ? [unmanagedName] : new[] { unmanagedName + extension, unmanagedName };

        foreach (var dir in new[] { Path.Combine(folder, "runtimes", rid, "native"), folder })
            foreach (var file in names)
                yield return Path.Combine(dir, file);
    }
}
