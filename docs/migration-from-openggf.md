# Migration from OpenGGF

TraceChaser was extracted from OpenGGF without changing the live trace contract:
published traces remain `trace_schema: 5`, with native recorder provenance and
the existing comparison, timing, inventory, and no-replacement rules.

## Portable paths and inputs

TraceChaser commands resolve their own checkout from the entry point's location.
They do not search a current directory, Git superproject, sibling checkout, or
machine-specific OpenGGF path. The former OpenGGF paths map as follows:

| Former OpenGGF path | TraceChaser path |
|---|---|
| `tools/bizhawk-headless/` | `bizhawk-headless/` |
| `tools/bizhawk/` | `bizhawk/` |
| `tools/traces/` | `traces/` |

Python imports use the `traces` package. The canonical capture-matrix module is
`traces/trace_v5_capture_matrix.py`; `bizhawk-headless/trace_v5_capture_matrix.py`
is a thin command-line entry point retained for users of the old component
location.

Capture-matrix operations require the matrix file and explicit consumer inputs:
the OpenGGF checkout, fixture root and inventory, movie root, all three verified
ROM paths, and external batch/candidate roots as applicable. No ROM filename or
environment-variable discovery is performed. Scratch and candidate roots are
rejected when they are within either source tree. The default native BizHawk
installation is checkout-local
`.dependencies/BizHawk-2.11-linux-x64`; it contains dependencies, never capture
output.

All paths may contain spaces. Matrix expansion returns literal argument vectors
and performs shell quoting only when writing a human-readable command ledger.

## Producer and consumer test ownership

The extracted producer tests remain behavioral and use synthetic explicit
inputs. Assertions that require OpenGGF-owned files were not weakened or made
optional; they return to the OpenGGF consumer suite in the integration task.

| Removed TraceChaser assumption | OpenGGF consumer disposition |
|---|---|
| `.agents`/`.claude` trace-skill mirrors and their capability text | Keep as OpenGGF agent-document policy tests. |
| Java replay Javadoc and `TestTraceFixtureRootOverride` implementation text | Keep beside the corresponding OpenGGF Java tests. |
| OpenGGF BizHawk README and recorder-behavior prose assertions | Replace prose-change detectors with consumer conformance tests where behavior is executable; retain documentation checks in OpenGGF only where policy requires them. |
| Read-only inventory generation against `src/test/resources/traces` | Run against the explicit OpenGGF fixture root from the consumer suite. |
| Git-index inventory discovery from OpenGGF root and fixture children | Keep as an OpenGGF worktree integration test. |
| Exact six-movie extraction identities, frozen OpenGGF commits/diffs, extraction argv ledger, and committed fixture publication mappings | Keep in the OpenGGF extraction/integration evidence suite using explicit paths into both checkouts. |
| Count and placement of OpenGGF BK2 fixtures after assembly | Assert in OpenGGF's consumer publication test; TraceChaser asserts only that the explicitly supplied static inputs are copied byte-for-byte. |

TraceChaser continues to test exact matrix format and row ordering, complete
explicit ROM/movie identity checks, typed argv expansion, no-replacement
assembly, source-tree write rejection, trace-v5 validation, comparison, and raw
host-evidence behavior.

## Historical citations

`docs/history-import.md`, `history-paths.tsv`, and provenance passages in the
imported component documentation intentionally retain former OpenGGF paths.
They describe where bytes and evidence originated, not live TraceChaser command
locations. Live commands and internal citations use the root-relative paths in
the table above.
