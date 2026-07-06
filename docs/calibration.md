# Calibrating the token estimator

`src/Shared/TokenEstimator.cs` is the single source of truth for token counting —
source-linked into both Eitri.Analyzers (the EIT100/EIT101 compile-time gate) and the
`heimdall` CLI (`heimdall estimate`), so the harness and the compiler cannot drift.

To re-check its calibration against a real tokenizer, compare `heimdall estimate`
with tiktoken's `o200k_base` on any C# tree (tiktoken is a Python library — this doc
is the one intentional Python mention left in the repo):

    heimdall estimate samples        # estimator total for all .cs under samples/

    python3 -c "import sys,os,tiktoken; enc=tiktoken.get_encoding('o200k_base'); \
      print(sum(len(enc.encode(open(os.path.join(r,f),encoding='utf-8').read())) \
      for r,_,fs in os.walk(sys.argv[1]) for f in fs if f.endswith('.cs')))" samples

Target: within ~±10% of o200k_base, erring conservative (+) on typical C# — a stricter
budget is the safe failure mode. If you retune the estimator, update the README claim.
