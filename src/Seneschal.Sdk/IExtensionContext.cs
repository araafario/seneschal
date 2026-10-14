namespace Seneschal.Sdk;

/// <summary>What an extension is allowed to know about itself.</summary>
public interface IExtensionContext
{
    /// <summary>The id from the extension's manifest.</summary>
    string Id { get; }

    string Name { get; }

    Version Version { get; }

    /// <summary>A folder only this extension writes to. It survives restarts and updates, and is not removed with the app's own files.</summary>
    string DataDirectory { get; }

    /// <summary>Where the extension was loaded from.</summary>
    string Directory { get; }
}
