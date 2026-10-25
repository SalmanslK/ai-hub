using System.Reflection;
using System.Text.RegularExpressions;
using App.Connections;
using App.Connections.Contracts;
using Xunit;

namespace App.Tests.Architecture;

// The connector seam (plan boilerplate-and-architecture §3 Connector): a connector hands its events to
// Capture through Capture's contract, never Capture's implementation, and the guide says so and why.
public sealed class ConnectorSeamTests
{
    [Fact]
    [Trait("AC", "R-007.AC-5")]
    public void Connections_reaches_capture_only_through_its_contract()
    {
        var connections = References(AssemblyOf<SimulatedConnector>())
            .Concat(References(typeof(IConnector).Assembly))
            .ToList();

        Assert.Contains(DependencyRule.Contract("Capture"), connections);
        Assert.DoesNotContain(DependencyRule.Implementation("Capture"), connections);
        Assert.DoesNotContain(DependencyRule.Host, connections);
    }

    [Fact]
    [Trait("AC", "R-007.AC-5")]
    public void Connections_referencing_capture_implementation_fails_the_rule()
    {
        var connections = DependencyRule.Implementation("Connections");
        var map = new Dictionary<string, IReadOnlyCollection<string>>
        {
            [connections] = References(AssemblyOf<SimulatedConnector>())
                .Append(DependencyRule.Implementation("Capture"))
                .ToList(),
        };

        var violation = Assert.Single(DependencyRule.Violations(map));
        Assert.Contains(connections, violation);
        Assert.Contains("App.Capture ", violation + " ");
    }

    [Fact]
    [Trait("AC", "R-007.AC-7")]
    public void Architecture_guide_states_the_connector_seam_rule_and_why()
    {
        var guide = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "architecture.md"));
        var section = Section(guide, "connector");

        Assert.Contains(nameof(IConnector), section);
        Assert.Contains("IUsageEventLog", section);
        Assert.Matches(@"(?i)simulated", section);
        Assert.Matches(@"(?i)\breal\b", section);
        Assert.Matches(@"(?i)(one|single|same) interface", section);
        Assert.Matches(@"(?i)(never|not|only)[^.]*(another|other) module", section);
        Assert.Contains("**Why:**", section);
    }

    // The assembly a connector is built in.
    private static Assembly AssemblyOf<TConnector>() where TConnector : IConnector => typeof(TConnector).Assembly;

    private static List<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(reference => reference.StartsWith("App.", StringComparison.Ordinal))
            .ToList();

    // From the heading that names `title` to the next heading of the same or a higher level.
    private static string Section(string text, string title)
    {
        var heading = new Regex(@"^(#{1,6})\s+.*" + Regex.Escape(title) + ".*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Match(text);
        Assert.True(heading.Success, $"The architecture guide has no heading naming \"{title}\".");
        var level = heading.Groups[1].Value.Length;
        var next = new Regex(@"^#{1," + level + @"}\s", RegexOptions.Multiline)
            .Match(text, heading.Index + heading.Length);
        return next.Success ? text[heading.Index..next.Index] : text[heading.Index..];
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "App.slnx")))
            directory = directory.Parent;
        return directory?.Parent?.FullName ?? throw new InvalidOperationException("App.slnx not found above the test output.");
    }
}
