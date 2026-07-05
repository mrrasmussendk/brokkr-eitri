# GARM003 — Contract signature leaks a non-kernel type
Contracts must be leaves: signatures may use kernel types, System types, and same-Contract types only. Anything else makes consuming your contract drag in transitive context — the exact failure mode the architecture exists to prevent.
**Fix:** map the foreign type to a kernel primitive or a DTO owned by this Contract.
