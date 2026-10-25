using System.Reflection;
using Xunit;

namespace App.Tests.Architecture;

// Guard for the connector seam: adding it must leave the module dependency rule holding for every project.
public sealed class ConnectorModuleRuleTests
{
    [Fact]
    [Trait("AC", "R-007.AC-8")]
    public void No_module_references_another_modules_implementation_or_the_host()
    {
        var map = DependencyRule.AllProjects().ToDictionary(
            name => name,
            name => (IReadOnlyCollection<string>)Assembly.Load(new AssemblyName(name))
                .GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(reference => reference.StartsWith("App.", StringComparison.Ordinal))
                .ToList());

        var violations = DependencyRule.Violations(map);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
