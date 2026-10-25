using System.Diagnostics;
using System.Net;

namespace App.IntegrationTests.Image;

// The repository root: the folder holding backend/App.slnx, where the Dockerfile and compose.yaml live.
public static class Checkout
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "App.slnx")))
                return directory.FullName;
        }
        throw new InvalidOperationException("The repository root (the folder holding backend/App.slnx) was not found.");
    }
}

// Runs a command line tool and keeps what it wrote.
public static class CommandLine
{
    public sealed record Result(int ExitCode, string Stdout, string Stderr);

    public static async Task<Result> RunAsync(string tool, IEnumerable<string> arguments, string workingDirectory)
    {
        var start = new ProcessStartInfo(tool)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{tool} did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new Result(process.ExitCode, await stdout, await stderr);
    }
}

public static class Http
{
    // Asks until the host answers 200 or the timeout passes, then returns the last answer.
    public static async Task<HttpResponseMessage> UntilAnsweredAsync(HttpClient client, string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                var response = await client.GetAsync(path);
                if (response.StatusCode == HttpStatusCode.OK || DateTime.UtcNow > deadline)
                    return response;
            }
            catch (HttpRequestException) when (DateTime.UtcNow <= deadline)
            {
                // The host is still starting.
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}
