using System.Reflection;
using App.Connections.Contracts;
using Xunit;

namespace App.Tests.Connections;

// The connector seam (plan boilerplate-and-architecture §3 Connector): one interface every AI tool's
// connector implements, real or simulated, carrying only Connections' and the shared kernel's types.
public sealed class ConnectorContractTests
{
    private static readonly Assembly Contract = typeof(IConnectionsModule).Assembly;

    [Fact]
    [Trait("AC", "R-007.AC-1")]
    public void The_contract_exposes_one_connector_interface_with_a_tool_and_a_run()
    {
        var connectors = Contract.GetExportedTypes()
            .Where(type => type.IsInterface && type != typeof(IConnectionsModule))
            .ToList();
        Assert.Equal(typeof(IConnector), Assert.Single(connectors));

        var tool = typeof(IConnector).GetProperty(nameof(IConnector.Tool));
        Assert.NotNull(tool);
        Assert.Equal(typeof(string), tool.PropertyType);
        var run = typeof(IConnector).GetMethod(nameof(IConnector.RunAsync));
        Assert.NotNull(run);
        Assert.Equal(typeof(Task<ConnectorRunResult>), run.ReturnType);
        Assert.Equal([typeof(CancellationToken)], run.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    [Trait("AC", "R-007.AC-1")]
    public void A_run_result_reports_events_appended_and_records_rejected()
    {
        var result = RunResult(3, new RejectedRecord(2, "Payload"));

        Assert.Equal(3, result.Appended);
        Assert.Equal(2, Assert.Single(result.Rejected).Index);
        Assert.Equal("Payload", result.Rejected[0].Field);
    }

    [Fact]
    [Trait("AC", "R-007.AC-1")]
    public void No_contract_member_takes_or_returns_another_modules_implementation_type()
    {
        var offenders = Contract.GetExportedTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(member => SignatureTypes(member).Select(used => (type, member, used))))
            .Where(entry => IsImplementation(entry.used.Assembly))
            .Select(entry => $"{entry.type.Name}.{entry.member.Name} uses {entry.used.FullName}")
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static ConnectorRunResult RunResult(int appended, params RejectedRecord[] rejected) => new(appended, rejected);

    private static IEnumerable<Type> SignatureTypes(MemberInfo member)
    {
        var types = member switch
        {
            MethodInfo method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType),
            ConstructorInfo constructor => constructor.GetParameters().Select(p => p.ParameterType),
            PropertyInfo property => [property.PropertyType],
            FieldInfo field => [field.FieldType],
            _ => [],
        };
        return types.SelectMany(Unwrap);
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (type.HasElementType)
            return Unwrap(type.GetElementType()!);
        return type.IsGenericType
            ? type.GetGenericArguments().SelectMany(Unwrap).Prepend(type)
            : [type];
    }

    // Ours, and neither the shared kernel nor a module's contract.
    private static bool IsImplementation(Assembly assembly)
    {
        var name = assembly.GetName().Name!;
        return name.StartsWith("App.", StringComparison.Ordinal)
               && name != "App.Shared"
               && !name.EndsWith(".Contracts", StringComparison.Ordinal);
    }
}
