# Contributing

TraceChaser changes must preserve standalone operation, the source-only
repository boundary, no-replace output, exact BizHawk 2.11 selection, and the
single live trace-v5 contract.

## Local source-only gate

The pinned Linux CI toolchain is Python 3.12, Lua 5.4, PowerShell 7.4.7,
Mono 6.12.0, Bash, Git, and ripgrep 14. PowerShell is installed as the exact
NuGet tool version before checkout; it is not inherited from the hosted-runner
image. From a clean clone with that toolchain, run:

```bash
LUA_BIN=lua5.4 python3 -m unittest discover -s testing -p 'test_*.py' -v
python3 testing/repository_policy.py --root .
python3 testing/history_audit.py --root .
python3 traces/validate_v5_conformance.py contracts/v5
python3 testing/documentation_policy.py --root .
git diff --check
```

Parse each tracked shell source using the NUL-delimited Git index:

```bash
count=0
while IFS= read -r -d '' script; do
  bash -n "$script"
  count=$((count + 1))
done < <(git ls-files -z -- '*.sh')
test "$count" -gt 0
```

The source-only job permits no test skips. It provisions and version-checks
every selected external interpreter before checkout; a missing or wrong tool
fails preflight before tests. The post-test auditor independently rejects a
nonzero unittest status, any verbose skip line, a missing run summary, or any
summary other than exact `OK`.

When checking an optional search result, handle all ripgrep outcomes. Status 0
means matches, 1 means no matches, and 2 or greater is an error; do not write
`rg ... || true`, which hides real failures.

## Change rules

- Add a failing behavioral test before changing producer behavior.
- Keep commands independent of the caller's current directory and support
  paths containing spaces.
- Require explicit consumer/input/scratch roots; never search for OpenGGF.
- Keep generated output outside both source trees and refuse replacement.
- Do not add ROMs, movies, emulator archives, build output, captures, logs, or
  corpus fixtures. Only the bounded synthetic conformance pack is admitted.
- Update the relevant guide and executable contract in the same change.
- Preserve recursive probe enumeration and all-history policy scanning.

Native integration is a separate, manually selected job on a labelled
self-hosted runner with a pre-provisioned cache. Source-only success never
claims that native integration or ROM-backed differential gates ran.
