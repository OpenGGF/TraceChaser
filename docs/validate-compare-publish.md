# Validate, compare, and publish

Capture creates a scratch candidate; it never authorizes or installs canonical
fixtures. Validation, comparison, inventory review, approval, and consumer-side
publication are separate gates.

## 1. Validate v5 semantics

Validate the root that contains the complete fleet or run members:

```bash
python3 traces/validate_trace_v5.py \
  --require-frame-keyed-auxiliary \
  /absolute/scratch/candidate
```

The validator is read-only. It rejects legacy envelopes, malformed or
nondeterministic gzip, duplicate JSON keys, wrong row widths, invalid timing,
and run-manifest membership/order faults. An empty or arbitrary directory is
not a valid fleet.

The repository's portable synthetic contract is checked separately:

```bash
python3 traces/validate_v5_conformance.py contracts/v5
```

## 2. Freeze an inventory

Write the inventory outside the candidate root:

```bash
python3 traces/trace_fixture_inventory.py generate \
  /absolute/scratch/candidate \
  --output /absolute/evidence/candidate-inventory.json
python3 traces/trace_fixture_inventory.py verify \
  /absolute/scratch/candidate \
  /absolute/evidence/candidate-inventory.json
```

The inventory sorts every path and records stored hashes plus logical hashes
for gzip members. Keep the movie and ROM identities, command argv, tool commit,
BizHawk lock, counts, and ordering in the same review evidence.

## 3. Compare independently

Write the report outside both compared roots. Use `v5-literal` for two v5
fleets and the special `credits-20-to-42` mode only for the reviewed S1 credits
shape change.

```bash
python3 traces/compare_trace_v5_candidates.py \
  /absolute/reference/fleet \
  /absolute/scratch/candidate \
  --mode v5-literal \
  --output /absolute/evidence/comparison.json \
  --fail-on-difference
```

Without `--fail-on-difference`, a semantic difference is reported rather than
treated as a tool failure. Read the JSON report; do not judge parity from an
exit code alone. Never derive expected hashes from the candidate invocation
that is supposed to prove them.

## 4. Compression

Native publication compresses `physics.csv` and `aux_state.jsonl` by default
once they reach the configured threshold. Metadata, hardware timing, and run
manifests remain plain. Gzip must be deterministic (timestamp zero), and the
inventory must preserve both stored and logical identity. Prefer native
all-or-nothing compression. `traces/compress-traces.ps1` exists for reviewed
legacy fixture maintenance; run it only against a disposable external copy and
revalidate afterward.

## 5. Approve and publish

Publication requires human approval of the exact candidate bytes and every
explained delta. Copy only the approved, manifested members into the consumer
repository using its own atomic cutover process. Then validate the staged
consumer index with `trace_fixture_inventory.py verify --git-index` and run the
consumer's full tests. TraceChaser never discovers, overwrites, or silently
updates an OpenGGF fixture tree.

If any identity, count, order, normalized field, or unexplained byte differs,
quarantine the candidate in external evidence storage and record again.
