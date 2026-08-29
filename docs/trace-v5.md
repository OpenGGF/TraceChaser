# Trace schema v5

TraceChaser supports one live contract: `trace_schema: 5`. Older traces are not
an input compatibility promise. Recorder provenance is opaque; it never selects
parser or replay behavior.

## Fleet members

A level/special-stage member contains `metadata.json`, a physics CSV (plain or
deterministic gzip), and optional auxiliary/timing streams permitted by its
profile. Named runs add `run_manifest.json` and ordered segment directories.
Plain and gzipped files may not both represent the same logical member.

Current native provenance is:

```json
{
  "recorder": "native-bizhawk-headless",
  "recorder_version": "3.0",
  "trace_schema": 5
}
```

`lua_script_version`, `csv_version`, `ss_csv_version`,
`hardware_timing_schema`, and `run_schema` were removed. They were not renamed
and must not appear in a v5 envelope.

## Rows and sidecars

Ordinary level physics has exactly 42 columns, covering frame/input/camera,
clocks, and symmetric player/sidekick state including animation and mapping
frames. S1, S2, and S3K special stages use their game-owned exact 14-, 48-, and
20-column shapes respectively. Numeric interpretation is field-specific; do
not normalize by guessing from appearance.

Auxiliary JSONL is comparison data. When a consumer requires frame-keyed
events, validate with `--require-frame-keyed-auxiliary`.

Hardware timing records scheduling outcomes only. The supported work kinds are
Kosinski module queue, direct Kosinski decompression queue, and the S1 Nemesis
PLC queue at their allowed service boundaries. Frame bounds, monotonic order,
contiguous ordinals, and `sha256:` submission fingerprints are mandatory.
Timing never supplies gameplay values or creates work.

Run manifests own ordered `segments`, `transitions`, and
`dynamic_art_gap_transitions` arrays. Member order is semantic, segment
directories must agree with their metadata, and every run includes the gap
array even when empty.

## Executable contract

`contracts/v5/manifest.json` inventories accept/reject fixtures, exact stored
identity, covered rules, expected diagnostics, consumer entry points, and
normalized semantics. It includes real deterministic gzip and collection/order
faults. Validate it with:

```bash
python3 traces/validate_v5_conformance.py contracts/v5
```

To prove reproducibility, generate into a new external directory and compare
the resulting tree byte-for-byte with `contracts/v5`; never regenerate over the
committed authority.
