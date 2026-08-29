Trace recording and analysis tools used by OpenGGF.

# TraceChaser

TraceChaser is the standalone home for OpenGGF's emulator-facing trace
toolchain. It contains the native BizHawk/GPGX recorder, Lua and RetroArch
capture utilities, and trace-v5 validation, comparison, compression, inventory,
evidence, and publication tools.

This repository is being prepared for its first release. OpenGGF integration,
the verified BizHawk installer, and the workflow guides marked below are not yet
available; their links reserve the reviewed documentation structure for the
remaining extraction work.

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

The history-preserving import is documented now in
[History import](docs/history-import.md). The following reviewed workflow
documents are forthcoming; none of these links represents a currently shipped
command or support promise:

- Installation: [BizHawk 2.11](docs/install-bizhawk-2.11.md) and
  [scratch and security policy](docs/scratch-and-security.md).
- Capture (standard and complete-run modes where supported):
  [Sonic 1](docs/capture-s1.md), [Sonic 2](docs/capture-s2.md),
  [Sonic 3 & Knuckles](docs/capture-s3k.md),
  [native headless workflows](docs/native-headless.md), and
  [Lua probes](docs/lua-probes.md).
- Trace-v5 lifecycle: [producer contract](docs/trace-v5-contract.md),
  [validation](docs/validation.md), [comparison](docs/comparison.md),
  [compression](docs/compression.md), [inventory](docs/inventory.md), and
  [publication](docs/publication.md).
- Project work: [migration from OpenGGF](docs/migration-from-openggf.md),
  [contributing](docs/contributing.md), [testing](docs/testing.md), and
  [releasing](docs/releasing.md).

Until those guides land, treat the imported component READMEs as historical
context and do not infer portable commands from their former OpenGGF paths.

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

## License

TraceChaser is licensed under GPLv3. See [LICENSE](LICENSE). The exact upstream
Zstandard notice retained for the native observer is stored beside that source.
