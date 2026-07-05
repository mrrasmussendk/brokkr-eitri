using Garmr;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Garmr.Tests;

public static class TestHost
{
    public static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> Create<TAnalyzer>(
        string code, int budget = 15_000)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $"""
            is_global = true
            build_property.Garmr_SlicePrefix = Slices.
            build_property.Garmr_KernelAssembly = SharedKernel
            build_property.Garmr_TokenBudget = {budget}
            """));
        return test;
    }
}

public class Garm001Tests
{
    [Fact]
    public async Task Public_type_in_Internal_is_reported()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace Slices.Domme.Internal;
            public sealed class {|GARM001:Leak|} { }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task Public_type_in_Contract_is_fine()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace Slices.Domme.Contract;
            public interface IDommeService { }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task Module_is_exempt()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace Slices.Domme.Internal;
            public static class Module { }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task Non_slice_namespaces_are_not_policed()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace SomethingElse.Internal;
            public sealed class Fine { }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task Internal_type_in_Internal_is_fine()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace Slices.Domme.Internal;
            internal sealed class Engine { }
            """);
        await test.RunAsync();
    }
}

public class Garm002Tests
{
    [Fact]
    public async Task InternalsVisibleTo_is_reported()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            using System.Runtime.CompilerServices;
            [assembly: {|GARM002:InternalsVisibleTo("Domme")|}]
            namespace Slices.Retskilder.Internal { internal sealed class Engine { } }
            """);
        await test.RunAsync();
    }
}

public class Garm003Tests
{
    [Fact]
    public async Task Contract_exposing_foreign_contract_type_is_reported()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace Slices.Retskilder.Contract
            {
                public sealed record RetskilderAssessment(double Score);
            }
            namespace Slices.Domme.Contract
            {
                public interface ILeaky
                {
                    Slices.Retskilder.Contract.RetskilderAssessment {|GARM003:Get|}();
                }
            }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task Contract_using_System_and_same_contract_types_is_fine()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace Slices.Domme.Contract;
            public sealed record DommeAssessment(double Score, System.Collections.Generic.IReadOnlyList<string> Notes);
            public interface IDommeService
            {
                DommeAssessment Assess(string factum);
            }
            """);
        await test.RunAsync();
    }
}

public class Garm100Tests
{
    [Fact]
    public async Task Over_budget_slice_fails()
    {
        var test = TestHost.Create<ContextBudgetAnalyzer>("""
            namespace Slices.Domme.Internal;
            internal sealed class Engine
            {
                public int ComputeWeightedAssessmentScore(int provisionCount, int factumLength)
                    => provisionCount * 31 + factumLength * 7;
            }
            """, budget: 10);
        test.ExpectedDiagnostics.Add(new DiagnosticResult("GARM100", DiagnosticSeverity.Error));
        await test.RunAsync();
    }

    [Fact]
    public async Task Under_budget_slice_reports_info()
    {
        var test = TestHost.Create<ContextBudgetAnalyzer>("""
            namespace Slices.Domme.Internal;
            internal sealed class Engine { }
            """, budget: 100_000);
        test.ExpectedDiagnostics.Add(new DiagnosticResult("GARM101", DiagnosticSeverity.Info));
        await test.RunAsync();
    }

    [Fact]
    public async Task Non_slice_assembly_is_not_metered()
    {
        var test = TestHost.Create<ContextBudgetAnalyzer>("""
            namespace JustALibrary;
            public sealed class Thing { }
            """, budget: 10);
        await test.RunAsync();
    }
}
