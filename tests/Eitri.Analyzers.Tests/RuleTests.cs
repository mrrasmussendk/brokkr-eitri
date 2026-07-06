using Eitri;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Eitri.Tests;

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
            build_property.Eitri_SlicePrefix = Slices.
            build_property.Eitri_KernelAssembly = SharedKernel
            build_property.Eitri_TokenBudget = {budget}
            """));
        return test;
    }
}

public class Eit001Tests
{
    [Fact]
    public async Task Public_type_in_Internal_is_reported()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace Slices.Kvad.Internal;
            public sealed class {|EIT001:Leak|} { }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task Public_type_in_Contract_is_fine()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace Slices.Kvad.Contract;
            public interface IKvadService { }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task Module_is_exempt()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace Slices.Kvad.Internal;
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
            namespace Slices.Kvad.Internal;
            internal sealed class Engine { }
            """);
        await test.RunAsync();
    }
}

public class Eit002Tests
{
    [Fact]
    public async Task InternalsVisibleTo_is_reported()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            using System.Runtime.CompilerServices;
            [assembly: {|EIT002:InternalsVisibleTo("Kvad")|}]
            namespace Slices.Rune.Internal { internal sealed class Engine { } }
            """);
        await test.RunAsync();
    }
}

public class Eit003Tests
{
    [Fact]
    public async Task Contract_exposing_foreign_contract_type_is_reported()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace Slices.Rune.Contract
            {
                public sealed record RuneReading(double Score);
            }
            namespace Slices.Kvad.Contract
            {
                public interface ILeaky
                {
                    Slices.Rune.Contract.RuneReading {|EIT003:Get|}();
                }
            }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task Contract_using_System_and_same_contract_types_is_fine()
    {
        var test = TestHost.Create<WallAnalyzer>("""
            namespace Slices.Kvad.Contract;
            public sealed record Verse(double Score, System.Collections.Generic.IReadOnlyList<string> Notes);
            public interface IKvadService
            {
                Verse Compose(string utterance);
            }
            """);
        await test.RunAsync();
    }
}

public class Eit100Tests
{
    [Fact]
    public async Task Over_budget_slice_fails()
    {
        var test = TestHost.Create<ContextBudgetAnalyzer>("""
            namespace Slices.Kvad.Internal;
            internal sealed class Engine
            {
                public int ComputeWeightedReadingScore(int runeCount, int utteranceLength)
                    => runeCount * 31 + utteranceLength * 7;
            }
            """, budget: 10);
        test.ExpectedDiagnostics.Add(new DiagnosticResult("EIT100", DiagnosticSeverity.Error));
        await test.RunAsync();
    }

    [Fact]
    public async Task Under_budget_slice_reports_info()
    {
        var test = TestHost.Create<ContextBudgetAnalyzer>("""
            namespace Slices.Kvad.Internal;
            internal sealed class Engine { }
            """, budget: 100_000);
        test.ExpectedDiagnostics.Add(new DiagnosticResult("EIT101", DiagnosticSeverity.Info));
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
