# Releasing

A TraceChaser release is source-only. It must not contain BizHawk, ROMs, BK2
movies, captures, native build output, or OpenGGF fixtures.

## Release gates

1. Freeze the intended commit and run the complete source-only workflow from a
   fresh local clone with full reachable history.
2. Run current-tree and all-history repository audits. Inspect every reachable
   object, license/provenance notice, and conformance-pack exception.
3. Validate the v5 conformance pack and reproduce it byte-for-byte into a new
   external directory.
4. Preflight exact BizHawk 2.11 and run native source integration on the
   labelled cache host. Run ROM-backed gates only with verified user-supplied
   inputs, and record skips separately from passes.
5. Reproduce the reviewed capture matrix. Freeze ROM/movie identities, argv,
   counts, ordering, stored/logical hashes, normalized semantics, host/toolchain,
   and all explained differences in a versioned validation record.
6. Obtain independent review of history, policy, CI, dependency locks,
   captures, documentation, and the exact release tree.
7. Verify repository ownership/nonexistence before the first remote mutation.
   Create/push/tag only after every prior gate passes.

Release notes state the compatible OpenGGF range, schema v5, exact BizHawk
2.11 support, tested host/toolchain matrix, capture evidence, and retained
limitations. Tag the exact reviewed commit with an annotated semantic version.
After publishing, fresh-fetch the remote and prove the peeled tag commit equals
the intended object.

Do not move a tag, publish a branch archive as evidence, or attach binary
artifacts. OpenGGF pins the immutable release commit as a gitlink; a release is
not complete until that consumer pin and its copied conformance pack have been
verified independently.
