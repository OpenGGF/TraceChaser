Trace recording and analysis tools used by OpenGGF.

# TraceChaser

TraceChaser is the standalone home for OpenGGF's emulator-facing trace
toolchain. It contains the native BizHawk/GPGX recorder, Lua and RetroArch
capture utilities, and trace-v5 validation, comparison, compression, inventory,
evidence, and publication tools.

The repository is source-only. It does not make OpenGGF a build dependency and
does not vendor the emulator or private capture inputs.

## Repository layout

- `bizhawk-headless/`: preferred native BizHawk 2.11/GPGX capture path and its
  source-side tests.
- `bizhawk/`: Lua recorders, shared modules, launchers, and diagnostic probes.
- `retro/`: alternative emulator capture adapters.
- `traces/`: trace-v5 validation, comparison, compression, inventory, evidence,
  and publication utilities.
- `testing/`: repository, history, and source-side policy tests.
- `contracts/`: small portable producer/consumer conformance inputs.

TraceChaser does not include or redistribute ROMs, BK2 movies, BizHawk binaries,
OpenGGF's canonical trace corpus, or generated capture output. Those inputs are
selected explicitly, and all capture output belongs in durable scratch storage
outside both the TraceChaser and OpenGGF repositories.

## Documentation

The history-preserving import and the portable path/input boundary are documented
in [History import](docs/history-import.md) and
[Migration from OpenGGF](docs/migration-from-openggf.md).

The supported BizHawk dependency workflow is documented in
[Install and verify BizHawk 2.11](docs/install-bizhawk-2.11.md).

- Installation: [scratch and security policy](docs/scratch-and-security.md).
- Capture (standard and complete-run modes where supported):
  [Sonic 1](docs/capture-s1.md), [Sonic 2](docs/capture-s2.md),
  [Sonic 3 & Knuckles](docs/capture-s3k.md),
  [native headless workflows](docs/native-headless.md), and
  [Lua probes](docs/lua-probes.md).
- Trace-v5 lifecycle: [contract](docs/trace-v5.md) and
  [validation, comparison, compression, inventory, and publication](docs/validate-compare-publish.md).
- Project work: [contributing](docs/contributing.md) and
  [releasing](docs/releasing.md).

## Repository policy

Run the current-tree and all-history policy gates before committing:

```bash
python3 -B -m unittest testing.test_repository_policy testing.test_history_audit -v
python3 -B testing/repository_policy.py --root .
python3 -B testing/history_audit.py --root .
```

The current-tree gate scans exactly the Git index. The history gate scans every
blob reachable from every ref. Both share one artifact policy, including exact
content hashes for the root GPL license and the curated Zstandard notice.

The sole raw-payload exception is the small conformance pack below
`contracts/v5/`. Every member must be listed by the pack's exact manifest path,
stored size, and stored SHA-256; deterministic gzip members also require logical
size and SHA-256. Both scanners enforce the relationship in the current index
or in each historical commit independently. This admission policy does not
define trace-v5 semantics; the executable conformance pack does that separately.

## License

TraceChaser is licensed under GPLv3. See [LICENSE](LICENSE). The exact upstream
Zstandard notice retained for the native observer is stored beside that source.
