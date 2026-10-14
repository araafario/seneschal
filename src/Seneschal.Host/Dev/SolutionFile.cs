using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Seneschal.Dev;

/// <summary>Adds and removes extension projects in Seneschal.slnx, so Visual Studio shows a project the Developer page created.</summary>
internal static class SolutionFile
{
    private const string ExtensionsFolder = "/extensions/";

    public static void AddExtension(string solutionPath, string name)
    {
        var doc = XDocument.Load(solutionPath);
        var root = doc.Root ?? throw new InvalidDataException("The solution file is empty.");
        var folder = root.Elements("Folder").FirstOrDefault(f => (string?)f.Attribute("Name") == ExtensionsFolder);
        if (folder is null)
        {
            folder = new XElement("Folder", new XAttribute("Name", ExtensionsFolder));
            root.Add(folder);
        }

        var path = ProjectPath(name);
        if (folder.Elements("Project").Any(p => string.Equals((string?)p.Attribute("Path"), path, StringComparison.OrdinalIgnoreCase)))
            return;
        folder.Add(new XElement("Project", new XAttribute("Path", path)));
        Save(doc, solutionPath);
    }

    /// <summary>Returns false when the project was not in the solution.</summary>
    public static bool RemoveExtension(string solutionPath, string name)
    {
        var doc = XDocument.Load(solutionPath);
        var path = ProjectPath(name);
        var entries = doc.Descendants("Project")
            .Where(p => string.Equals((string?)p.Attribute("Path"), path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entries.Count == 0) return false;

        foreach (var entry in entries) entry.Remove();
        Save(doc, solutionPath);
        return true;
    }

    private static string ProjectPath(string name) => $"extensions/{name}/{name}.csproj";

    private static void Save(XDocument doc, string path)
    {
        using var writer = XmlWriter.Create(path, new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = true,
            Encoding = new UTF8Encoding(false),
        });
        doc.Save(writer);
    }
}
