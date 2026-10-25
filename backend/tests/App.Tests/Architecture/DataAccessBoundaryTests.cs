using System.Reflection;
using Xunit;

namespace App.Tests.Architecture;

// Data contexts belong to a module's implementation; its Contracts stay free of the data layer.
public sealed class DataAccessBoundaryTests
{
    private static readonly string[] DataLayer = ["Microsoft.EntityFrameworkCore", "Npgsql"];

    [Fact]
    [Trait("AC", "R-005.AC-9")]
    public void No_module_contract_references_the_data_layer_or_declares_a_data_context()
    {
        var offenders = new List<string>();
        foreach (var contract in DependencyRule.Modules.Select(DependencyRule.Contract))
        {
            var assembly = Assembly.Load(new AssemblyName(contract));
            offenders.AddRange(assembly.GetReferencedAssemblies()
                .Where(reference => DataLayer.Any(prefix => reference.Name!.StartsWith(prefix, StringComparison.Ordinal)))
                .Select(reference => $"{contract} references {reference.Name}"));
            offenders.AddRange(assembly.GetTypes()
                .Where(IsDataContext)
                .Select(type => $"{contract} declares the data context {type.FullName}"));
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static bool IsDataContext(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "DbContext")
                return true;
        }
        return false;
    }
}
