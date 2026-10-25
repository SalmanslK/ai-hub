using System.Reflection;
using Xunit;

namespace App.Tests.Architecture;

// R-004 adds host plumbing only; the module layout from R-003 must hold through it.
public sealed class ModuleLayoutGuardTests
{
    [Fact]
    [Trait("AC", "R-004.AC-10")]
    public void No_module_depends_on_another_module_implementation_or_on_the_host()
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
