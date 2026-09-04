namespace QuoteHub.ArchitectureTests;

// Each test enforces one row of DESIGN.md's dependency table by reading
// the actual .csproj files under src/ - not documentation, not
// convention. Temporarily add a forbidden <ProjectReference> to any
// project (e.g. point Curation.Domain at Moderation.Domain) and the
// matching test here goes red; the verification report for this scaffold
// records that demonstration actually being run and reverted.
public class DependencyRuleTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Graph = ProjectGraph.Load();

    private const string SharedKernel = "QuoteHub.SharedKernel";
    private const string Contracts = "QuoteHub.Contracts";

    public static IEnumerable<object[]> DomainProjects =>
    [
        ["QuoteHub.Curation.Domain"],
        ["QuoteHub.Moderation.Domain"],
    ];

    public static IEnumerable<object[]> ApplicationProjects =>
    [
        ["QuoteHub.Curation.Application", "QuoteHub.Curation.Domain"],
        ["QuoteHub.Moderation.Application", "QuoteHub.Moderation.Domain"],
    ];

    public static IEnumerable<object[]> InfrastructureProjects =>
    [
        ["QuoteHub.Curation.Infrastructure", "QuoteHub.Curation.Domain", "QuoteHub.Curation.Application"],
        ["QuoteHub.Moderation.Infrastructure", "QuoteHub.Moderation.Domain", "QuoteHub.Moderation.Application"],
    ];

    [Theory]
    [MemberData(nameof(DomainProjects))]
    public void Domain_references_nothing_but_SharedKernel(string domainProject)
    {
        // No EF, no ASP.NET, no other module - the only thing a Domain
        // project is allowed to declare a reference to is the
        // domain-agnostic kernel (Entity, AggregateRoot, Result).
        var allowed = new[] { SharedKernel };

        Assert.All(Graph[domainProject], reference => Assert.Contains(reference, allowed));
    }

    [Theory]
    [MemberData(nameof(ApplicationProjects))]
    public void Application_references_only_own_domain_and_contracts(string applicationProject, string ownDomain)
    {
        var allowed = new[] { SharedKernel, Contracts, ownDomain };

        Assert.All(Graph[applicationProject], reference => Assert.Contains(reference, allowed));
    }

    [Theory]
    [MemberData(nameof(InfrastructureProjects))]
    public void Infrastructure_references_only_own_application(string infrastructureProject, string ownDomain, string ownApplication)
    {
        var allowed = new[] { SharedKernel, Contracts, ownDomain, ownApplication };

        Assert.All(Graph[infrastructureProject], reference => Assert.Contains(reference, allowed));
    }

    [Fact]
    public void Api_references_module_infrastructure_only()
    {
        // The composition root wires DI through each module's
        // Infrastructure - it never reaches past that into a module's
        // Domain or Application directly.
        var allowed = new[] { "QuoteHub.Curation.Infrastructure", "QuoteHub.Moderation.Infrastructure" };

        Assert.All(Graph["QuoteHub.Api"], reference => Assert.Contains(reference, allowed));
    }

    [Theory]
    [InlineData("Curation", "Moderation")]
    [InlineData("Moderation", "Curation")]
    public void No_project_in_one_module_references_a_project_in_the_other(string ownModule, string otherModule)
    {
        var ownProjects = Graph.Keys.Where(name => name.Contains($".{ownModule}."));

        foreach (var project in ownProjects)
        {
            var forbidden = Graph[project].Where(reference => reference.Contains($".{otherModule}."));
            Assert.Empty(forbidden);
        }
    }
}
