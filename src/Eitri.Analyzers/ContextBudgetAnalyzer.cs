using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Eitri;

/// <summary>
/// EIT100/EIT101 — the context-budget fitness function, inside the compiler.
///
/// For each slice assembly, computes the worst-case *agent working set*:
///
///     own source tokens
///   + Σ public-Contract API surface tokens of every referenced slice assembly
///   + public API surface tokens of the kernel
///
/// The dependency surfaces are reconstructed from assembly METADATA via
/// SymbolDisplay — i.e. what an agent would actually need in context to consume
/// that dependency — so the number is architecture-truthful: internals of
/// dependencies cost zero, exactly as the architecture promises.
///
/// Always reports EIT101 (info) with the number; fails the build with EIT100
/// when the budget (build_property.Eitri_TokenBudget, default 15000) is exceeded.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContextBudgetAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            Descriptors.Eit100ContextBudgetExceeded,
            Descriptors.Eit101ContextBudgetReport);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationAction(ctx =>
        {
            var cfg = EitriConfig.Read(ctx.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
            if (!cfg.Enabled) return;

            var compilation = ctx.Compilation;
            var assemblyName = compilation.AssemblyName ?? "?";

            // only meter slice assemblies (they contain slice-prefixed namespaces)
            if (!ContainsSliceNamespace(compilation.Assembly.GlobalNamespace, cfg.SlicePrefix))
                return;

            // 1) own source
            var sourceTokens = 0;
            foreach (var tree in compilation.SyntaxTrees)
                sourceTokens += TokenEstimator.Estimate(tree.GetText(ctx.CancellationToken).ToString());

            // 2) dependency contract surfaces + 3) kernel surface (from metadata)
            var contractTokens = 0;
            var kernelTokens = 0;
            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol asm) continue;
                var name = asm.Name;
                if (IsFrameworkAssembly(name)) continue;

                if (name == cfg.KernelAssembly)
                    kernelTokens += SurfaceTokens(asm.GlobalNamespace, contractOnly: false, cfg);
                else if (ContainsSliceNamespace(asm.GlobalNamespace, cfg.SlicePrefix))
                    contractTokens += SurfaceTokens(asm.GlobalNamespace, contractOnly: true, cfg);
            }

            var total = sourceTokens + contractTokens + kernelTokens;
            var over = total > cfg.TokenBudget;
            ctx.ReportDiagnostic(Diagnostic.Create(
                over ? Descriptors.Eit100ContextBudgetExceeded : Descriptors.Eit101ContextBudgetReport,
                Location.None,
                assemblyName, total, sourceTokens, contractTokens, kernelTokens, cfg.TokenBudget));
        });
    }

    private static bool ContainsSliceNamespace(INamespaceSymbol root, string prefix)
    {
        var head = prefix.TrimEnd('.').Split('.')[0];
        foreach (var member in root.GetNamespaceMembers())
            if (member.Name == head)
                return true;
        return false;
    }

    private static bool IsFrameworkAssembly(string name) =>
        name is "mscorlib" or "netstandard" or "System" or "System.Runtime"
        || name.StartsWith("System.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal);

    /// <summary>Reconstructs the public API surface an agent would load, and prices it.</summary>
    private static int SurfaceTokens(INamespaceSymbol root, bool contractOnly, EitriConfig cfg)
    {
        var sb = new StringBuilder();
        var stack = new Stack<INamespaceSymbol>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var ns = stack.Pop();
            foreach (var child in ns.GetNamespaceMembers()) stack.Push(child);

            var nsName = ns.ToDisplayString();
            if (contractOnly &&
                !(nsName.EndsWith(".Contract", StringComparison.Ordinal) ||
                  nsName.Contains(".Contract.")))
                continue;

            foreach (var type in ns.GetTypeMembers())
            {
                if (type.DeclaredAccessibility != Accessibility.Public) continue;
                AppendTypeSurface(sb, type);
            }
        }
        return TokenEstimator.Estimate(sb.ToString());
    }

    private static void AppendTypeSurface(StringBuilder sb, INamedTypeSymbol type)
    {
        sb.AppendLine(type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        foreach (var member in type.GetMembers())
        {
            if (member.DeclaredAccessibility != Accessibility.Public) continue;
            if (member is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet }) continue;
            sb.AppendLine(member.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        }
        foreach (var nested in type.GetTypeMembers())
            if (nested.DeclaredAccessibility == Accessibility.Public)
                AppendTypeSurface(sb, nested);
    }
}
