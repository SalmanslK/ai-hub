using System.Reflection;
using Xunit;

namespace App.Tests.Architecture;

// The worker in Capture calls Processing, so Capture may reference Processing's contract — never its
// implementation, and no module another's implementation or the host.
public sealed class EventPipelineDependencyTests
{
    [Fact]
    [Trait("AC", "R-006.AC-10")]
    public void Capture_reaches_processing_only_through_its_contract()
    {
        var capture = References(DependencyRule.Implementation("Capture"))
            .Concat(References(DependencyRule.Contract("Capture")))
            .ToList();

        Assert.DoesNotContain(DependencyRule.Implementation("Processing"), capture);
        Assert.DoesNotContain(DependencyRule.Host, capture);
    }

    [Fact]
    [Trait("AC", "R-006.AC-10")]
    public void No_module_references_another_modules_implementation_or_the_host()
    {
        var map = DependencyRule.AllProjects().ToDictionary(
            name => name,
            name => (IReadOnlyCollection<string>)References(name));

        var violations = DependencyRule.Violations(map);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static List<string> References(string project) =>
        Assembly.Load(new AssemblyName(project))
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(reference => reference.StartsWith("App.", StringComparison.Ordinal))
            .ToList();
}
