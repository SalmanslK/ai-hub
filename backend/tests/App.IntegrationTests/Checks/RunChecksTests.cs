using System.Text.Json;
using System.Text.RegularExpressions;
using App.IntegrationTests.Image;
using Xunit;

namespace App.IntegrationTests.Checks;

// The checks script (devtools/checks/run-checks.py) run as a process. Each test gives it a list of
// stand-in steps through `--steps`, so every outcome is driven by a small command and fixture results
// in a folder made for that test alone — the real builds and suites never run inside a test.
public sealed class ChecksRun : IDisposable
{
    public const string Script = "devtools/checks/run-checks.py";
    private const string Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public string Folder { get; } = Directory.CreateTempSubdirectory("r013-").FullName;

    private readonly List<object> _steps = [];

    // A step that runs a short Python program and exits with its code.
    public ChecksRun Step(string name, string program) =>
        Add(new { name, command = new[] { Python.Path, "-c", program }, folder = Folder });

    // A suite step: its command copies the given results file into the step's results folder, then exits.
    public ChecksRun Suite(string name, string? resultsFile, string resultsName, int exitCode = 0)
    {
        var results = Path.Combine(Folder, "results-" + _steps.Count);
        var copy = resultsFile is null
            ? $"import sys; sys.exit({exitCode})"
            : $"import os, shutil, sys; os.makedirs(sys.argv[2], exist_ok=True); shutil.copy(sys.argv[1], os.path.join(sys.argv[2], '{resultsName}')); sys.exit({exitCode})";
        string[] command = resultsFile is null
            ? [Python.Path, "-c", copy]
            : [Python.Path, "-c", copy, resultsFile, results];
        return Add(new { name, command, folder = Folder, results });
    }

    // A .trx with the given outcomes (Passed, Failed, NotExecuted), counted in its summary as dotnet writes it.
    public string WriteTrx(params string[] outcomes)
    {
        var results = string.Concat(outcomes.Select((outcome, i) =>
            $"<UnitTestResult testId=\"{Guid.NewGuid()}\" testName=\"Test{i}\" outcome=\"{outcome}\" />"));
        var executed = outcomes.Count(outcome => outcome != "NotExecuted");
        var passed = outcomes.Count(outcome => outcome == "Passed");
        var failed = outcomes.Count(outcome => outcome == "Failed");
        var notExecuted = outcomes.Count(outcome => outcome == "NotExecuted");
        return WriteFile(".trx",
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun xmlns=\"{Trx}\"><Results>{results}</Results>" +
            $"<ResultSummary outcome=\"{(failed > 0 ? "Failed" : "Completed")}\"><Counters total=\"{outcomes.Length}\" " +
            $"executed=\"{executed}\" passed=\"{passed}\" failed=\"{failed}\" notExecuted=\"{notExecuted}\" /></ResultSummary></TestRun>");
    }

    // JUnit XML as Vitest's junit reporter writes it; "skipped" cases carry a <skipped/>.
    public string WriteJUnit(params string[] outcomes) =>
        WriteFile(".xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><testsuites><testsuite name=\"suite\">" +
            string.Concat(outcomes.Select((outcome, i) => outcome switch
            {
                "skipped" => $"<testcase name=\"test{i}\"><skipped/></testcase>",
                "failed" => $"<testcase name=\"test{i}\"><failure message=\"no\"/></testcase>",
                _ => $"<testcase name=\"test{i}\"></testcase>",
            })) +
            "</testsuite></testsuites>");

    public async Task<Output> RunAsync()
    {
        Assert.True(File.Exists(Path.Combine(Checkout.Root, Script)), $"There is no checks script at {Script}.");
        var steps = Path.Combine(Folder, "steps.json");
        await File.WriteAllTextAsync(steps, JsonSerializer.Serialize(_steps));
        var result = await CommandLine.RunAsync(Python.Path, [Script, "--steps", steps], Checkout.Root);
        return new Output(result);
    }

    public void Dispose() => Directory.Delete(Folder, recursive: true);

    private ChecksRun Add(object step)
    {
        _steps.Add(step);
        return this;
    }

    private string WriteFile(string extension, string content)
    {
        var path = Path.Combine(Folder, "fixture-" + Guid.NewGuid().ToString("N")[..8] + extension);
        File.WriteAllText(path, content);
        return path;
    }

    public sealed record Output(CommandLine.Result Result)
    {
        public string[] Lines { get; } =
            (Result.Stdout + "\n" + Result.Stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public string LastLine => Result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";

        public string Describe() => $"exit {Result.ExitCode}\nstdout:\n{Result.Stdout}\nstderr:\n{Result.Stderr}";

        // The one line the script printed for a step.
        public string LineFor(string step) =>
            Lines.FirstOrDefault(line => line.Contains(step, StringComparison.Ordinal))
            ?? throw new Xunit.Sdk.XunitException($"No line names the step '{step}'.\n{Describe()}");
    }
}

// The Python the factory's scripts run on: python3 where it exists, else python.
public static class Python
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        foreach (var candidate in new[] { "python3", "python" })
        {
            try
            {
                if (CommandLine.RunAsync(candidate, ["--version"], Checkout.Root).GetAwaiter().GetResult().ExitCode == 0)
                    return candidate;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not on the PATH; try the next.
            }
        }
        throw new InvalidOperationException("Neither python3 nor python is on the PATH.");
    }
}

public sealed partial class RunChecksTests
{
    // The built-in steps, in the order the Design names them.
    private static readonly string[] BuiltInSteps =
    [
        "backend build", "frontend build", "backend fast suite", "backend integration suite",
        "frontend suite", "lint", "types", "generated docs", "factory self-tests",
    ];

    [Fact]
    [Trait("AC", "R-013.AC-1")]
    public async Task Every_step_passing_prints_a_line_per_step_with_suite_counts_and_exits_zero()
    {
        using var run = new ChecksRun();
        run.Step("stand-in build", "print('built')")
            .Suite("stand-in dotnet suite", run.WriteTrx("Passed", "Passed", "Passed"), "results.trx")
            .Suite("stand-in vitest suite", run.WriteJUnit("passed", "passed"), "results.xml")
            .Step("stand-in lint", "pass");

        var output = await run.RunAsync();

        Assert.True(output.Result.ExitCode == 0, output.Describe());
        foreach (var step in new[] { "stand-in build", "stand-in dotnet suite", "stand-in vitest suite", "stand-in lint" })
            Assert.Contains("PASS", output.LineFor(step));
        Assert.Matches(Counts(3, 3, 0, 0), output.LineFor("stand-in dotnet suite"));
        Assert.Matches(Counts(2, 2, 0, 0), output.LineFor("stand-in vitest suite"));
        Assert.DoesNotContain("FAIL", output.Result.Stdout);
    }

    [Fact]
    [Trait("AC", "R-013.AC-1")]
    public async Task The_built_in_steps_run_in_the_designed_order()
    {
        var path = Path.Combine(Checkout.Root, ChecksRun.Script);
        Assert.True(File.Exists(path), $"There is no checks script at {ChecksRun.Script}.");
        var script = await File.ReadAllTextAsync(path);

        var position = 0;
        foreach (var step in BuiltInSteps)
        {
            var found = script.IndexOf($"\"{step}\"", position, StringComparison.Ordinal);
            Assert.True(found >= 0, $"The step \"{step}\" is missing from the built-in list, or is out of order.");
            position = found;
        }
        Assert.Contains("App.slnx", script);
        Assert.Contains("tests/App.Tests", script);
        Assert.Contains("tests/App.IntegrationTests", script);
        Assert.Contains("check-test-results.py", script);
    }

    [Fact]
    [Trait("AC", "R-013.AC-2")]
    public async Task A_failing_step_stops_the_run_and_the_last_line_names_it()
    {
        using var run = new ChecksRun();
        var marker = Path.Combine(run.Folder, "after-ran");
        run.Step("stand-in build", "pass")
            .Step("stand-in lint", "import sys; print('error: unused variable'); sys.exit(3)")
            .Step("stand-in types", $"open(r'{marker}', 'w').close()");

        var output = await run.RunAsync();

        Assert.True(output.Result.ExitCode != 0, output.Describe());
        Assert.Contains("stand-in lint", output.LastLine);
        Assert.Contains("FAIL", output.LineFor("stand-in lint"));
        Assert.False(File.Exists(marker), "A step after the failed one still ran.");
    }

    [Fact]
    [Trait("AC", "R-013.AC-2")]
    public async Task A_suite_with_a_failing_test_fails_and_the_last_line_names_it()
    {
        using var run = new ChecksRun();
        run.Suite("stand-in dotnet suite", run.WriteTrx("Passed", "Failed"), "results.trx", exitCode: 1);

        var output = await run.RunAsync();

        Assert.True(output.Result.ExitCode != 0, output.Describe());
        Assert.Contains("stand-in dotnet suite", output.LastLine);
        Assert.Matches(Counts(2, 1, 1, 0), output.LineFor("stand-in dotnet suite"));
    }

    [Fact]
    [Trait("AC", "R-013.AC-3")]
    public async Task A_dotnet_suite_that_skipped_a_test_fails_as_skipped()
    {
        using var run = new ChecksRun();
        run.Suite("stand-in dotnet suite", run.WriteTrx("Passed", "NotExecuted"), "results.trx");

        var output = await run.RunAsync();

        AssertFailedAs(output, "stand-in dotnet suite", "skipped");
    }

    [Fact]
    [Trait("AC", "R-013.AC-3")]
    public async Task A_vitest_suite_that_skipped_a_test_fails_as_skipped()
    {
        using var run = new ChecksRun();
        run.Suite("stand-in vitest suite", run.WriteJUnit("passed", "skipped"), "results.xml");

        var output = await run.RunAsync();

        AssertFailedAs(output, "stand-in vitest suite", "skipped");
    }

    [Fact]
    [Trait("AC", "R-013.AC-4")]
    public async Task A_suite_that_executed_no_tests_fails_as_empty()
    {
        using var run = new ChecksRun();
        run.Suite("stand-in dotnet suite", run.WriteTrx(), "results.trx");

        var output = await run.RunAsync();

        AssertFailedAs(output, "stand-in dotnet suite", "empty");
    }

    [Fact]
    [Trait("AC", "R-013.AC-4")]
    public async Task A_suite_that_wrote_no_results_fails_as_empty()
    {
        using var run = new ChecksRun();
        run.Suite("stand-in vitest suite", resultsFile: null, "results.xml");

        var output = await run.RunAsync();

        AssertFailedAs(output, "stand-in vitest suite", "empty");
    }

    [Fact]
    [Trait("AC", "R-013.AC-5")]
    public async Task An_integration_suite_without_its_database_fails_and_is_not_reported_as_skipped()
    {
        using var run = new ChecksRun();
        run.Suite("backend integration suite", run.WriteTrx("Failed", "Failed"), "results.trx", exitCode: 1);

        var output = await run.RunAsync();

        Assert.True(output.Result.ExitCode != 0, output.Describe());
        Assert.Contains("backend integration suite", output.LastLine);
        var line = output.LineFor("backend integration suite");
        Assert.Contains("FAIL", line);
        Assert.Matches(Counts(2, 0, 2, 0), line);
        Assert.DoesNotContain("PASS", line);
    }

    [Fact]
    [Trait("AC", "R-013.AC-5")]
    public async Task The_built_in_integration_suite_runs_whole_through_the_skip_gate()
    {
        var path = Path.Combine(Checkout.Root, ChecksRun.Script);
        Assert.True(File.Exists(path), $"There is no checks script at {ChecksRun.Script}.");
        var script = await File.ReadAllTextAsync(path);

        Assert.Contains("\"backend integration suite\"", script);
        Assert.Contains("tests/App.IntegrationTests", script);
        Assert.Contains("check-test-results.py", script);
    }

    [Fact]
    [Trait("AC", "R-013.AC-6")]
    public async Task A_stale_generated_doc_fails_the_generated_docs_step()
    {
        using var run = new ChecksRun();
        run.Step("stand-in build", "pass")
            .Step("generated docs", "import sys; print('docs/project.md: the task roll-up is stale'); sys.exit(1)");

        var output = await run.RunAsync();

        Assert.True(output.Result.ExitCode != 0, output.Describe());
        Assert.Contains("generated docs", output.LastLine);
        Assert.Contains("FAIL", output.LineFor("generated docs"));
    }

    [Fact]
    [Trait("AC", "R-013.AC-6")]
    public async Task The_built_in_generated_docs_step_runs_every_generator_check()
    {
        var path = Path.Combine(Checkout.Root, ChecksRun.Script);
        Assert.True(File.Exists(path), $"There is no checks script at {ChecksRun.Script}.");
        var script = await File.ReadAllTextAsync(path);

        Assert.Contains("gen-task-rollup.py", script);
        Assert.Contains("\"index\"", script);
        Assert.Contains("\"--check\"", script);
    }

    [Fact]
    [Trait("AC", "R-013.AC-7")]
    public async Task Architecture_ci_describes_the_checks_script_with_no_placeholders_left()
    {
        var architecture = await File.ReadAllTextAsync(Path.Combine(Checkout.Root, "docs", "architecture.md"));
        var start = architecture.IndexOf("\n## 8. CI", StringComparison.Ordinal);
        Assert.True(start >= 0, "architecture.md has no §8 CI.");
        var end = architecture.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = architecture[start..(end < 0 ? architecture.Length : end)];

        Assert.Contains("run-checks.py", section);
        foreach (var row in new[] { "**Build**", "**Tests**", "**Generated docs match the source**", "**Factory self-tests**" })
        {
            var line = section.Split('\n').FirstOrDefault(l => l.StartsWith("| " + row, StringComparison.Ordinal));
            Assert.True(line is not null, $"§8 CI has no {row} row.");
            Assert.DoesNotContain("FILL", line);
        }
        var tests = section.Split('\n').First(l => l.StartsWith("| **Tests**", StringComparison.Ordinal));
        Assert.Contains("skipped", tests, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("empty", tests, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("AC", "R-013.AC-7")]
    public async Task Readme_says_how_to_run_the_checks_before_a_merge()
    {
        var readme = await File.ReadAllTextAsync(Path.Combine(Checkout.Root, "README.md"));

        Assert.Contains("devtools/checks/run-checks.py", readme);
        Assert.Contains("merge", readme, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertFailedAs(ChecksRun.Output output, string step, string reason)
    {
        Assert.True(output.Result.ExitCode != 0, output.Describe());
        Assert.Contains(step, output.LastLine);
        Assert.Contains(reason, output.LastLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PASS", output.LineFor(step));
    }

    // "<e> executed … <p> passed … <f> failed … <s> skipped", in that order.
    private static Regex Counts(int executed, int passed, int failed, int skipped) =>
        new($@"\b{executed} executed\b.*\b{passed} passed\b.*\b{failed} failed\b.*\b{skipped} skipped\b");
}
