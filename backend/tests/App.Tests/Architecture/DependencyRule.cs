namespace App.Tests.Architecture;

// The module dependency rule (plan boilerplate-and-architecture §3): a module depends only
// on the shared kernel and other modules' contracts. Projects are named App.Api (host),
// App.Shared (shared kernel), App.<Module>.Contracts and App.<Module>.
public static class DependencyRule
{
    public const string Host = "App.Api";
    public const string Shared = "App.Shared";
    public const string ContractsSuffix = ".Contracts";

    public static readonly string[] Modules =
    [
        "Discovery", "Connections", "Capture", "Processing", "Intelligence",
        "Recommendations", "Knowledge", "Analytics", "Governance",
    ];

    public static string Contract(string module) => $"App.{module}{ContractsSuffix}";

    public static string Implementation(string module) => $"App.{module}";

    public static IEnumerable<string> AllProjects() =>
        new[] { Host, Shared }
            .Concat(Modules.Select(Contract))
            .Concat(Modules.Select(Implementation));

    // Returns one message per forbidden edge, naming the project and its dependency.
    public static IReadOnlyList<string> Violations(IReadOnlyDictionary<string, IReadOnlyCollection<string>> map)
    {
        var violations = new List<string>();
        foreach (var (project, dependencies) in map)
        {
            foreach (var dependency in dependencies)
            {
                if (!IsAllowed(project, dependency))
                    violations.Add($"{project} must not depend on {dependency}");
            }
        }
        return violations;
    }

    private static bool IsAllowed(string project, string dependency)
    {
        if (project == Host || !IsOurs(dependency))
            return true;
        if (project == Shared)
            return false;
        if (IsModuleContract(project) || IsModuleImplementation(project))
        {
            if (dependency == Shared || IsModuleContract(dependency))
                return true;
            return false;
        }
        return true;
    }

    private static bool IsOurs(string name) => name == Host || name == Shared || IsModuleContract(name) || IsModuleImplementation(name);

    private static bool IsModuleContract(string name) => Modules.Any(m => name == Contract(m));

    private static bool IsModuleImplementation(string name) => Modules.Any(m => name == Implementation(m));
}
