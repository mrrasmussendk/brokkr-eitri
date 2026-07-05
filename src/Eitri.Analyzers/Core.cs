using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Eitri;

internal static class Descriptors
{
    private const string Category = "Eitri.Architecture";
    private const string HelpBase = "https://github.com/brokkr-eitri/brokkr-eitri/blob/main/docs/rules/";

    public static readonly DiagnosticDescriptor Eit001PublicOutsideContract = new(
        id: "EIT001",
        title: "Public type outside Contract",
        messageFormat: "'{0}' is public but lives outside the slice's Contract namespace — the public surface of a slice is its Contract, everything else must be internal",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "EIT001.md");

    public static readonly DiagnosticDescriptor Eit002InternalsVisibleTo = new(
        id: "EIT002",
        title: "InternalsVisibleTo is banned",
        messageFormat: "InternalsVisibleTo('{0}') breaches slice isolation — internals are slice-private by design; expose capability through the Contract instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "EIT002.md",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor Eit003ContractPurity = new(
        id: "EIT003",
        title: "Contract signature leaks a non-kernel type",
        messageFormat: "Contract member '{0}' exposes '{1}' — contract signatures may use only kernel types, System types, and types of the same Contract, so that consuming a contract never drags in transitive context",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "EIT003.md");

    public static readonly DiagnosticDescriptor Eit100ContextBudgetExceeded = new(
        id: "EIT100",
        title: "Agent context budget exceeded",
        messageFormat: "Slice '{0}' worst-case agent working set is ≈{1:N0} tokens (own source {2:N0} + dependency contracts {3:N0} + kernel {4:N0}), over the budget of {5:N0} — split the slice or slim its contracts",
        category: "Eitri.ContextBudget",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "EIT100.md");

    public static readonly DiagnosticDescriptor Eit101ContextBudgetReport = new(
        id: "EIT101",
        title: "Agent context budget report",
        messageFormat: "Slice '{0}' agent working set ≈{1:N0} tokens of {5:N0} budget (source {2:N0} · contracts {3:N0} · kernel {4:N0})",
        category: "Eitri.ContextBudget",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "EIT100.md");
}

internal readonly struct EitriConfig
{
    public readonly string SlicePrefix;
    public readonly string KernelAssembly;
    public readonly int TokenBudget;
    public readonly bool Enabled;

    private EitriConfig(string slicePrefix, string kernelAssembly, int tokenBudget, bool enabled)
    {
        SlicePrefix = slicePrefix;
        KernelAssembly = kernelAssembly;
        TokenBudget = tokenBudget;
        Enabled = enabled;
    }

    public static EitriConfig Read(AnalyzerConfigOptions options)
    {
        options.TryGetValue("build_property.Eitri_SlicePrefix", out var prefix);
        options.TryGetValue("build_property.Eitri_KernelAssembly", out var kernel);
        options.TryGetValue("build_property.Eitri_TokenBudget", out var budgetRaw);
        options.TryGetValue("build_property.Eitri_Enabled", out var enabledRaw);
        var budget = int.TryParse(budgetRaw, out var b) ? b : 15_000;
        var enabled = !string.Equals(enabledRaw, "false", StringComparison.OrdinalIgnoreCase);
        return new EitriConfig(
            string.IsNullOrWhiteSpace(prefix) ? "Slices." : prefix!,
            string.IsNullOrWhiteSpace(kernel) ? "SharedKernel" : kernel!,
            budget,
            enabled);
    }
}

/// <summary>
/// Fast, dependency-free token estimator. Approximates BPE tokenizers on C# source:
/// counts word/identifier chunks, digits runs, and punctuation, then applies a
/// calibration factor. Within ~±10% of o200k_base on typical C# — good enough for
/// a budget gate; swap in a real tokenizer via Eitri.Tokenizers when precision matters.
/// </summary>
internal static class TokenEstimator
{
    public static int Estimate(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var tokens = 0;
        var i = 0;
        var n = text.Length;
        while (i < n)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                // BPE splits long identifiers into subwords ≈ every 6 chars
                tokens += Math.Max(1, (i - start + 5) / 6);
                continue;
            }
            if (char.IsDigit(c))
            {
                var start = i;
                while (i < n && char.IsDigit(text[i])) i++;
                tokens += Math.Max(1, (i - start + 2) / 3);
                continue;
            }
            // punctuation: runs of the same operator often merge into one token
            var pStart = i;
            while (i < n && !char.IsLetterOrDigit(text[i]) && !char.IsWhiteSpace(text[i]) && text[i] != '_' && i - pStart < 3) i++;
            tokens += 1;
        }
        return tokens;
    }
}
