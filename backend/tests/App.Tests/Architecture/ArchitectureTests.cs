using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace App.Tests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly string BackendRoot = FindBackendRoot();
    private static readonly string RepoRoot = Path.GetDirectoryName(BackendRoot)!;

    [Fact]
    [Trait("AC", "R-003.AC-1")]
    public void Solution_lists_the_host_the_shared_kernel_and_a_contract_and_implementation_per_module()
    {
        var solution = File.ReadAllText(Path.Combine(BackendRoot, "App.slnx"));
        var expected = new List<string> { "src/App/App.Api.csproj", "src/Shared/App.Shared.csproj" };
        foreach (var module in DependencyRule.Modules)
        {
            var contract = DependencyRule.Contract(module);
            var implementation = DependencyRule.Implementation(module);
            expected.Add($"src/Modules/{module}/{contract}/{contract}.csproj");
            expected.Add($"src/Modules/{module}/{implementation}/{implementation}.csproj");
        }

        var missing = expected
            .Where(path => !solution.Contains($"\"{path}\"") || !File.Exists(Path.Combine(BackendRoot, path)))
            .ToList();

        Assert.True(missing.Count == 0, "Missing from App.slnx or disk: " + string.Join(", ", missing));
    }

    [Fact]
    [Trait("AC", "R-005.AC-9")]
    [Trait("AC", "R-003.AC-2")]
    public void Every_project_depends_only_on_what_the_rule_allows()
    {
        var violations = DependencyRule.Violations(RealDependencyMap());

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("AC", "R-003.AC-3")]
    public void A_module_implementation_on_another_module_implementation_fails()
    {
        var map = WithEdge(RealDependencyMap(), "App.Recommendations", "App.Capture");

        var violation = Assert.Single(DependencyRule.Violations(map));
        Assert.Contains("App.Recommendations", violation);
        Assert.Contains("App.Capture", violation);
    }

    [Fact]
    [Trait("AC", "R-003.AC-3")]
    public void A_module_implementation_on_another_module_contract_is_allowed()
    {
        var map = WithEdge(RealDependencyMap(), "App.Recommendations", "App.Capture.Contracts");

        Assert.Empty(DependencyRule.Violations(map));
    }

    [Fact]
    [Trait("AC", "R-003.AC-4")]
    public void A_module_contract_on_a_module_implementation_fails()
    {
        var map = WithEdge(RealDependencyMap(), "App.Capture.Contracts", "App.Processing");

        var violation = Assert.Single(DependencyRule.Violations(map));
        Assert.Contains("App.Capture.Contracts", violation);
        Assert.Contains("App.Processing", violation);
    }

    [Theory]
    [Trait("AC", "R-003.AC-4")]
    [InlineData("App.Discovery")]
    [InlineData("App.Discovery.Contracts")]
    [InlineData("App.Api")]
    public void The_shared_kernel_on_a_module_or_the_host_fails(string dependency)
    {
        var map = WithEdge(RealDependencyMap(), "App.Shared", dependency);

        var violation = Assert.Single(DependencyRule.Violations(map));
        Assert.Contains("App.Shared", violation);
        Assert.Contains(dependency, violation);
    }

    [Theory]
    [Trait("AC", "R-003.AC-5")]
    [InlineData("App.Governance")]
    [InlineData("App.Governance.Contracts")]
    public void A_module_on_the_host_fails(string module)
    {
        var map = WithEdge(RealDependencyMap(), module, "App.Api");

        var violation = Assert.Single(DependencyRule.Violations(map));
        Assert.Contains(module, violation);
        Assert.Contains("App.Api", violation);
    }

    [Fact]
    [Trait("AC", "R-003.AC-6")]
    public void The_placeholder_greeting_is_gone()
    {
        var declaration = new Regex(@"\b(class|record|struct)\s+Greeting(Tests)?\b");
        var offenders = Directory
            .EnumerateFiles(BackendRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => Path.GetFileName(path) is "Greeting.cs" or "GreetingTests.cs"
                           || declaration.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(BackendRoot, path))
            .ToList();

        Assert.True(offenders.Count == 0, "Still present: " + string.Join(", ", offenders));
    }

    [Fact]
    [Trait("AC", "R-003.AC-7")]
    public void Architecture_guide_states_the_layout_and_the_enforced_rule()
    {
        var guide = File.ReadAllText(Path.Combine(RepoRoot, "docs", "architecture.md"));
        var layout = Section(guide, "## 1. Layout and layering", "## 2. Where code lives");
        var placement = Section(guide, "## 2. Where code lives", "## 3. Conventions");

        Assert.DoesNotContain("FILL", layout);
        Assert.DoesNotContain("FILL", placement);
        Assert.Contains("modular monolith", layout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("**Why:**", layout);
        foreach (var module in DependencyRule.Modules)
            Assert.Contains(module, layout);
        Assert.Matches(@"\[enforced:[^\]]*ArchitectureTests[^\]]*\]", layout);
        foreach (var place in new[] { "backend/src/App", "backend/src/Modules", "backend/src/Shared", "Contracts" })
            Assert.Contains(place, placement);
    }

    private static Dictionary<string, IReadOnlyCollection<string>> RealDependencyMap() =>
        DependencyRule.AllProjects().ToDictionary(
            name => name,
            name => (IReadOnlyCollection<string>)Assembly.Load(new AssemblyName(name))
                .GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(reference => reference.StartsWith("App.", StringComparison.Ordinal))
                .ToList());

    private static Dictionary<string, IReadOnlyCollection<string>> WithEdge(
        Dictionary<string, IReadOnlyCollection<string>> map, string from, string to)
    {
        map[from] = map[from].Append(to).ToList();
        return map;
    }

    private static string Section(string text, string heading, string nextHeading)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing heading: {heading}");
        var end = text.IndexOf(nextHeading, start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is var parts
        && (parts.Contains("bin") || parts.Contains("obj"));

    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "App.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("App.slnx not found above the test output.");
    }
}
