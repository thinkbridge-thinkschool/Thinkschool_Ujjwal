using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace QuoteHub.ArchitectureTests;

// Reads project-reference edges straight from the .csproj files under
// src/, not from compiled assembly metadata. Assembly-level reflection
// (Assembly.GetReferencedAssemblies) would also surface *transitive*
// references - e.g. Curation.Infrastructure ends up able to use
// Curation.Domain types because Application already references Domain,
// which shows up as a direct assembly reference in the compiled DLL even
// though Infrastructure's .csproj never declares it. "No module
// references another module's projects" is a declaration-level rule, so
// this checks it at that level: it parses the same <ProjectReference>
// elements `dotnet add reference` writes.
public static class ProjectGraph
{
    public static string SrcRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src"));

    // projectName (e.g. "QuoteHub.Curation.Domain") -> names of the
    // projects it directly references.
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Load()
    {
        var csprojFiles = Directory.GetFiles(SrcRoot(), "*.csproj", SearchOption.AllDirectories);
        var graph = new Dictionary<string, IReadOnlyList<string>>();

        foreach (var path in csprojFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(path);
            var doc = XDocument.Load(path);

            var references = doc.Descendants("ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrEmpty(include))
                .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
                .ToList();

            graph[projectName] = references;
        }

        return graph;
    }
}
