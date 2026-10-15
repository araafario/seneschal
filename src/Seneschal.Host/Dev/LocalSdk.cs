using System.Reflection;
using System.Text;
using Seneschal.Sdk;

namespace Seneschal.Dev;

/// <summary>
/// The shared build setup of the folder that holds the extension projects (by default Documents/Seneschal/Projects): a copy of the Seneschal.Sdk the
/// running app uses, the build rules that stage and pack an extension, and a Directory.Build.props and .targets that every project below the folder
/// picks up. A project then needs no package, no network for the SDK and no checkout of Seneschal, and it always matches the app that builds it.
/// It is written again whenever the app is newer, so an update reaches every project at its next build.
/// </summary>
public static class LocalSdk
{
    public const string FolderName = ".seneschal";
    private const string TargetsResource = "Seneschal.Extension.targets";

    /// <summary>Makes sure <paramref name="projectsRoot"/> has the setup. Safe to call before every build.</summary>
    public static void Ensure(string projectsRoot)
    {
        var root = Path.GetFullPath(projectsRoot);
        var sdkDir = Path.Combine(root, FolderName);
        Directory.CreateDirectory(sdkDir);

        var sdkDll = typeof(IToolModule).Assembly.Location;
        CopyIfDifferent(sdkDll, Path.Combine(sdkDir, "Seneschal.Sdk.dll"));
        var xml = Path.ChangeExtension(sdkDll, ".xml");
        if (File.Exists(xml)) CopyIfDifferent(xml, Path.Combine(sdkDir, "Seneschal.Sdk.xml"));

        WriteIfDifferent(Path.Combine(sdkDir, TargetsResource), ReadTargets());

        var mudBlazor = typeof(MudBlazor.MudButton).Assembly.GetName().Version?.ToString(3) ?? "9.11.0";
        WriteIfDifferent(Path.Combine(root, "Directory.Build.props"), $$"""
            <Project>

              <!-- Written by Seneschal. Every extension project below this folder is built against the Seneschal.Sdk that the running app uses
                   (the copy in {{FolderName}}), so there is nothing to install. It is replaced when Seneschal is updated, so edit the projects, not this file. -->
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>

              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <!-- Compiled against, never shipped: the app provides MudBlazor and the SDK -->
                <PackageReference Include="MudBlazor" Version="{{mudBlazor}}" ExcludeAssets="runtime" />
                <Reference Include="Seneschal.Sdk">
                  <HintPath>$(MSBuildThisFileDirectory){{FolderName}}\Seneschal.Sdk.dll</HintPath>
                  <Private>false</Private>
                </Reference>
              </ItemGroup>

            </Project>
            """);

        WriteIfDifferent(Path.Combine(root, "Directory.Build.targets"), $$"""
            <Project>

              <!-- Written by Seneschal: the build rules that stage an extension in artifacts/extensions and pack it as a .snx -->
              <Import Project="$(MSBuildThisFileDirectory){{FolderName}}\{{TargetsResource}}" />

            </Project>
            """);
    }

    private static string ReadTargets()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(TargetsResource)
            ?? throw new InvalidOperationException("The build rules for extensions are missing from this copy of Seneschal");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void CopyIfDifferent(string from, string to)
    {
        var source = new FileInfo(from);
        if (File.Exists(to) && new FileInfo(to) is { } existing && existing.Length == source.Length && existing.LastWriteTimeUtc == source.LastWriteTimeUtc) return;
        File.Copy(from, to, overwrite: true);
        File.SetLastWriteTimeUtc(to, source.LastWriteTimeUtc);
    }

    private static void WriteIfDifferent(string path, string content)
    {
        content = content.TrimEnd() + Environment.NewLine;
        if (File.Exists(path) && File.ReadAllText(path) == content) return;
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
