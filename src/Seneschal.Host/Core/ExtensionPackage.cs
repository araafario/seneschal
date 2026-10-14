using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Seneschal.Core;

/// <summary>An import problem with a message that is safe to show to the user.</summary>
internal sealed class ExtensionException(string message) : Exception(message);

internal sealed record PackageFile(string Path, byte[] Bytes);

/// <summary>A managed library that ships inside an extension package.</summary>
internal sealed record PackageLibrary(string Name, Version Version, byte[] Bytes);

/// <summary>
/// The files of one extension: a single DLL, a .snx or .zip with the extension DLL, its manifest and the libraries it uses,
/// or the folder such a package was installed into. Paths are relative with '/' separators.
/// </summary>
internal sealed class ExtensionPackage
{
    public const string ManifestFile = "extension.json";

    private const int MaxEntries = 1000;
    private const long MaxUnpackedBytes = 150L * 1024 * 1024;
    private const string MainPrefix = "Seneschal.Extension.";

    private ExtensionPackage(List<PackageFile> files, PackageFile main, byte[]? mainSymbols, ExtensionManifest? manifest, string[] mainReferences, Dictionary<string, PackageLibrary> libraries)
    {
        Files = files;
        Main = main;
        MainSymbols = mainSymbols;
        Manifest = manifest;
        MainReferences = mainReferences;
        Libraries = libraries;
    }

    public IReadOnlyList<PackageFile> Files { get; }

    /// <summary>The DLL that contains the tool modules.</summary>
    public PackageFile Main { get; }

    /// <summary>The .pdb of <see cref="Main"/> when the package has one (used so a debugger can step into a dev build).</summary>
    public byte[]? MainSymbols { get; }

    /// <summary>extension.json, or null for a plain DLL or an older package that has none.</summary>
    public ExtensionManifest? Manifest { get; }

    /// <summary>The names of the assemblies the extension DLL references directly.</summary>
    public IReadOnlyList<string> MainReferences { get; }

    /// <summary>The other managed DLLs in the package root, by assembly name.</summary>
    public IReadOnlyDictionary<string, PackageLibrary> Libraries { get; }

    public static ExtensionPackage FromDll(string fileName, byte[] bytes) =>
        Build([new PackageFile(Path.GetFileName(fileName), bytes)], "The file");

    public static ExtensionPackage FromZip(byte[] bytes)
    {
        var files = new List<PackageFile>();
        long total = 0;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries) throw new ExtensionException($"The package has more than {MaxEntries} files.");

            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue; // folder entry
                var path = SafePath(entry.FullName);

                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    total += read;
                    if (total > MaxUnpackedBytes) throw new ExtensionException("The package is too large when unpacked (limit 150 MB).");
                    buffer.Write(chunk, 0, read);
                }
                files.Add(new PackageFile(path, buffer.ToArray()));
            }
        }
        catch (InvalidDataException)
        {
            throw new ExtensionException("This is not a valid .zip file.");
        }
        return Build(files, "The package");
    }

    /// <summary>
    /// Reads an extension from the folder it is installed in. Only what loading needs is read (the DLLs, their symbols and the
    /// manifest): static files stay on disk, and native libraries are loaded from their path.
    /// </summary>
    public static ExtensionPackage FromFolder(string folder)
    {
        var files = Directory.GetFiles(folder)
            .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(f).Equals(ManifestFile, StringComparison.OrdinalIgnoreCase))
            .Select(f => new PackageFile(Path.GetFileName(f), File.ReadAllBytes(f)))
            .ToList();
        if (!files.Any(f => f.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            throw new ExtensionException("The folder contains no .dll file.");
        return Build(files, "The folder");
    }

    /// <summary>Replaces the contents of <paramref name="folder"/> with this package's files.</summary>
    public void WriteTo(string folder)
    {
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            foreach (var file in Files)
            {
                var target = Path.GetFullPath(Path.Combine(folder, file.Path));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ExtensionException($"Unsafe file path '{file.Path}'.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, file.Bytes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExtensionException($"Could not write the extension's files (a library may still be in use; restart the app and try again): {ex.Message}");
        }
    }

    private static ExtensionPackage Build(List<PackageFile> files, string subject)
    {
        var managed = new List<(PackageFile File, string Name, Version Version, string[] References)>();
        foreach (var file in files.Where(f => !f.Path.Contains('/') && f.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            if (Identify(file.Bytes) is { } id) managed.Add((file, id.Name, id.Version, id.References));
        }
        if (managed.Count == 0)
            throw new ExtensionException("This is not a .NET assembly (or it targets another platform).");

        var duplicate = managed.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ExtensionException($"{subject} contains the library '{duplicate.Key}' more than once.");

        ExtensionManifest? manifest = null;
        if (files.FirstOrDefault(f => f.Path.Equals(ManifestFile, StringComparison.OrdinalIgnoreCase)) is { } manifestFile)
            manifest = ExtensionManifest.Parse(manifestFile.Bytes);

        List<(PackageFile File, string Name, Version Version, string[] References)> mains;
        if (manifest?.Entry is { Length: > 0 } entry)
        {
            // The manifest names the extension DLL
            mains = managed.Where(m => m.File.Path.Equals(entry, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mains.Count == 0)
                throw new ExtensionException($"extension.json names '{entry}' as the extension DLL, but {subject.ToLowerInvariant()} has no such .NET assembly.");
        }
        else
        {
            // No manifest (a plain DLL or an older package): the Seneschal.Extension.<Name>.dll, or the only DLL
            mains = managed.Where(m => m.File.Path.StartsWith(MainPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mains.Count == 0 && managed.Count == 1) mains = managed;
            if (mains.Count != 1)
                throw new ExtensionException(mains.Count == 0
                    ? $"{subject} has no extension DLL. Name it {MainPrefix}<Name>.dll, or name it as \"entry\" in {ManifestFile}."
                    : $"{subject} has more than one extension DLL ({string.Join(", ", mains.Select(m => m.File.Path))}).");
        }

        var main = mains[0];
        var symbols = files.FirstOrDefault(f => f.Path.Equals(Path.ChangeExtension(main.File.Path, ".pdb"), StringComparison.OrdinalIgnoreCase))?.Bytes;
        var libraries = managed.Where(m => m.File != main.File)
            .ToDictionary(m => m.Name, m => new PackageLibrary(m.Name, m.Version, m.File.Bytes), StringComparer.OrdinalIgnoreCase);
        return new ExtensionPackage(files, main.File, symbols, manifest, main.References, libraries);
    }

    /// <summary>The assembly name, version and references read from the file's metadata, or null when it is not a managed assembly.</summary>
    internal static (string Name, Version Version, string[] References)? Identify(byte[] bytes)
    {
        try
        {
            using var pe = new PEReader(new MemoryStream(bytes));
            if (!pe.HasMetadata) return null;
            var reader = pe.GetMetadataReader();
            var definition = reader.GetAssemblyDefinition();
            var references = reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)).ToArray();
            return (reader.GetString(definition.Name), definition.Version, references);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static string SafePath(string name)
    {
        var path = name.Replace('\\', '/').TrimStart('/');
        if (path.Length == 0 || name.StartsWith('/') || name.StartsWith('\\') || path.Contains(':') || path.Split('/').Any(s => s is ".." or "."))
            throw new ExtensionException($"Unsafe file path '{name}' in the package.");
        return path;
    }
}
