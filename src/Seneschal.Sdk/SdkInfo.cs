namespace Seneschal.Sdk;

/// <summary>Which version of the SDK the app was built with.</summary>
public static class SdkInfo
{
    /// <summary>An extension declares the SDK version it was built against; the app accepts the same major version and anything not newer than this.</summary>
    public static Version Version { get; } = typeof(SdkInfo).Assembly.GetName().Version ?? new Version(1, 0);
}
