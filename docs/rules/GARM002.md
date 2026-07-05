# GARM002 — InternalsVisibleTo is banned
`InternalsVisibleTo` is a visibility escape hatch through the slice wall. One attribute silently reconnects what the architecture separated. Found in the wild during Garmr's own development — the compiler accepts it without complaint.
**Fix:** expose the needed capability through the Contract. If tests need internals, colocate tests inside the slice assembly.
