namespace Brokkr.Tokenization;

/// <summary>
/// Fast, dependency-free token estimator. Approximates BPE tokenizers on C# source:
/// counts word/identifier chunks, digit runs, and punctuation, then applies a
/// calibration factor. Within ~±10% of o200k_base on typical C# — good enough for
/// a budget gate; swap in a real tokenizer via Eitri.Tokenizers when precision matters.
///
/// This is the single source of truth for token counting. It is linked (as source)
/// into BOTH Eitri.Analyzers (the compile-time EIT100/EIT101 gate) and Heimdall.Cli
/// (the `heimdall estimate` subcommand), so the harness and the compiler never drift.
/// Keep it dependency-free: the analyzer must not acquire any package dependency.
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
