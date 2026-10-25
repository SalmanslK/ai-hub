using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using App.Capture.Contracts;
using Xunit;

namespace App.Tests.Architecture;

// The event pipeline rule (plan boilerplate-and-architecture §3 Usage event, §4 flows 2–3), in
// Capture's contract and in the architecture guide.
public sealed class EventPipelineRuleTests
{
    private static readonly Regex Mutation = new(@"^(Update|Delete|Remove|Change|Edit|Set|Replace|Clear|Purge)",
        RegexOptions.IgnoreCase);

    [Fact]
    [Trait("AC", "R-006.AC-3")]
    public void Capture_contract_offers_append_and_no_update_or_delete()
    {
        var methods = typeof(IUsageEventLog).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToList();
        Assert.Contains("AppendAsync", methods);
        Assert.All(methods, name => Assert.StartsWith("Append", name, StringComparison.Ordinal));

        var mutators = typeof(IUsageEventLog).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && Mutation.IsMatch(m.Name))
                .Select(m => $"{type.Name}.{m.Name}"))
            .ToList();
        Assert.True(mutators.Count == 0, "Capture's contract offers a change to stored events: " + string.Join(", ", mutators));
    }

    [Fact]
    [Trait("AC", "R-006.AC-3")]
    public void A_usage_event_cannot_be_changed_once_made()
    {
        var settable = typeof(UsageEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } setter
                        && !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(settable.Count == 0, "UsageEvent has settable properties: " + string.Join(", ", settable));
        foreach (var field in new[] { "Tool", "Employee", "OccurredAt", "Kind", "Payload" })
            Assert.NotNull(typeof(UsageEvent).GetProperty(field));
    }

    [Fact]
    [Trait("AC", "R-006.AC-9")]
    public void Architecture_guide_states_the_event_pipeline_rule_its_why_and_the_queue_trade_off()
    {
        var guide = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "architecture.md"));
        var section = Section(guide, "event pipeline");

        Assert.Matches(@"(?i)append[- ]only", section);
        Assert.Matches(@"(?i)stored? (first|before)", section);
        Assert.Matches(@"(?i)at least once", section);
        Assert.Matches(@"(?i)idempotent", section);
        Assert.Contains("**Why:**", section);
        Assert.Matches(@"(?i)queue", section);
        Assert.Matches(@"(?i)table", section);
        Assert.Matches(@"(?i)broker", section);
        Assert.Contains("IUsageEventLog", section);
        Assert.Contains("IUsageEventProcessor", section);
    }

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
