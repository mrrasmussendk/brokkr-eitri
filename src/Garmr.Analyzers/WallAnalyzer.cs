using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Garmr;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WallAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            Descriptors.Garm001PublicOutsideContract,
            Descriptors.Garm002InternalsVisibleTo,
            Descriptors.Garm003ContractPurity);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var cfg = GarmrConfig.Read(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
            if (!cfg.Enabled) return;

            // GARM001 + GARM003: per named type
            start.RegisterSymbolAction(ctx => AnalyzeType(ctx, cfg), SymbolKind.NamedType);

            // GARM002: assembly attributes
            start.RegisterCompilationEndAction(ctx =>
            {
                foreach (var attr in ctx.Compilation.Assembly.GetAttributes())
                {
                    if (attr.AttributeClass?.ToDisplayString() ==
                        "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
                    {
                        var target = attr.ConstructorArguments.Length > 0
                            ? attr.ConstructorArguments[0].Value?.ToString() ?? "?"
                            : "?";
                        var loc = attr.ApplicationSyntaxReference?.GetSyntax(ctx.CancellationToken).GetLocation()
                                  ?? Location.None;
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.Garm002InternalsVisibleTo, loc, target));
                    }
                }
            });
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext ctx, GarmrConfig cfg)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        // only police slice namespaces
        if (!ns.StartsWith(cfg.SlicePrefix, StringComparison.Ordinal)) return;

        var inContract = ns.EndsWith(".Contract", StringComparison.Ordinal)
                         || ns.Contains(".Contract.");

        // ---- GARM001: public types belong in Contract (Module is the wiring exemption) ----
        if (!inContract
            && type.DeclaredAccessibility == Accessibility.Public
            && type.ContainingType is null
            && type.Name != "Module")
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptors.Garm001PublicOutsideContract,
                type.Locations.FirstOrDefault() ?? Location.None,
                type.Name));
        }

        // ---- GARM003: contract signatures may expose only kernel/System/same-contract types ----
        if (inContract && type.DeclaredAccessibility == Accessibility.Public)
        {
            foreach (var member in type.GetMembers())
            {
                if (member.DeclaredAccessibility != Accessibility.Public) continue;

                foreach (var exposed in ExposedTypes(member))
                {
                    if (IsAllowedInContract(exposed, ns, cfg)) continue;
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.Garm003ContractPurity,
                        member.Locations.FirstOrDefault() ?? Location.None,
                        member.Name,
                        exposed.ToDisplayString()));
                }
            }
        }
    }

    private static IEnumerable<ITypeSymbol> ExposedTypes(ISymbol member)
    {
        switch (member)
        {
            case IMethodSymbol m when m.MethodKind is MethodKind.Ordinary or MethodKind.Constructor:
                foreach (var t in Flatten(m.ReturnType)) yield return t;
                foreach (var p in m.Parameters)
                    foreach (var t in Flatten(p.Type)) yield return t;
                break;
            case IPropertySymbol p:
                foreach (var t in Flatten(p.Type)) yield return t;
                break;
            case IFieldSymbol f:
                foreach (var t in Flatten(f.Type)) yield return t;
                break;
        }
    }

    private static IEnumerable<ITypeSymbol> Flatten(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arr) { foreach (var t in Flatten(arr.ElementType)) yield return t; yield break; }
        yield return type;
        if (type is INamedTypeSymbol named)
            foreach (var arg in named.TypeArguments)
                foreach (var t in Flatten(arg))
                    yield return t;
    }

    private static bool IsAllowedInContract(ITypeSymbol t, string contractNs, GarmrConfig cfg)
    {
        if (t.TypeKind is TypeKind.TypeParameter or TypeKind.Error) return true;
        if (t.SpecialType != SpecialType.None) return true;
        var ns = t.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)) return true;
        if (t.ContainingAssembly?.Name == cfg.KernelAssembly) return true;
        if (ns == contractNs) return true;                       // same contract
        return false;
    }
}
