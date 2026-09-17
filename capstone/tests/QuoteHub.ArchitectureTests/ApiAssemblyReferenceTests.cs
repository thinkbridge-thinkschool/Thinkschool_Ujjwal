using System.Reflection;

namespace QuoteHub.ArchitectureTests;

// DependencyRuleTests.Api_references_module_infrastructure_only checks
// declared <ProjectReference> elements - a build-time rule read straight
// from .csproj XML (see ProjectGraph.cs). On its own, it would not catch
// Api code that spells a Domain or Application type directly: each
// module's Infrastructure already references its own Domain and
// Application, and MSBuild resolves project references transitively, so
// Api can compile against those types with zero new declared references
// - that test would stay green even if the boundary were violated in
// source, not just in the build graph.
//
// This test closes that gap by inspecting Api's own compiled assembly
// instead of its project file: the C# compiler only emits an AssemblyRef
// entry for an assembly whose member the referencing code actually
// names, so GetReferencedAssemblies() reflects what Api's code really
// uses, not just what MSBuild made reachable. Demonstrated by temporarily
// using a Curation.Domain type from Program.cs while writing this test:
// this test went red, DependencyRuleTests did not.
public class ApiAssemblyReferenceTests
{
    private static readonly string[] ForbiddenAssemblies =
    [
        "QuoteHub.Curation.Domain",
        "QuoteHub.Curation.Application",
        "QuoteHub.Moderation.Domain",
        "QuoteHub.Moderation.Application",
    ];

    [Fact]
    public void Api_assembly_never_references_a_module_Domain_or_Application_assembly()
    {
        var apiAssembly = Assembly.Load("QuoteHub.Api");

        var referencedNames = apiAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        foreach (var forbidden in ForbiddenAssemblies)
            Assert.DoesNotContain(forbidden, referencedNames);
    }
}
