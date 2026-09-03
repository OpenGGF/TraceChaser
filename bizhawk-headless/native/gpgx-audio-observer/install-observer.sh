#!/usr/bin/env bash
# Install a built observer core into a BizHawk 2.11 home.
#
# It copies the stock tree, replaces dll/gpgx.wbx.zst with the built core, and
# records the build's identity.json under gpgx-audio-observer-source/ so the
# managed harness can tell which core it is talking to.
#
# The only check is that the build's recorded hashes still describe the files
# being installed. That is provenance verification, not a host-image gate.
set -euo pipefail

fail() { printf 'install-observer: %s\n' "$*" >&2; exit 1; }

script_dir=$(cd -P -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
build= stock= output=
while (($#)); do
  case "$1" in
    --build) build=${2-}; shift 2 ;;
    --stock) stock=${2-}; shift 2 ;;
    --output) output=${2-}; shift 2 ;;
    *) fail "unknown argument: $1" ;;
  esac
done

for pair in "build:$build" "stock:$stock"; do
  name=${pair%%:*}; value=${pair#*:}
  [[ "$value" = /* && -d "$value" ]] || fail "--$name must be an absolute directory"
done
[[ "$output" = /* ]] || fail "--output must be an absolute path"
[[ ! -e "$output" ]] || fail "--output already exists: $output"

identity=$build/identity.json
[[ -f "$identity" ]] || fail "build has no identity.json"
[[ -f "$build/gpgx.wbx" && -f "$build/gpgx.wbx.zst" ]] || fail "build has no core"
[[ -f "$stock/EmuHawk.exe" && -f "$stock/dll/gpgx.wbx.zst" ]] \
  || fail "stock is not a BizHawk distribution"

sha() { local v; v=$(sha256sum "$1"); printf '%s' "${v%% *}"; }

recorded_raw=$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["decompressed_sha256"])' "$identity")
recorded_zst=$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["compressed_sha256"])' "$identity")
[[ "$(sha "$build/gpgx.wbx")" = "$recorded_raw" ]] || fail "built core does not match its recorded hash"
[[ "$(sha "$build/gpgx.wbx.zst")" = "$recorded_zst" ]] || fail "compressed core does not match its recorded hash"

lock_abi=$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["abi"]["version"])' "$script_dir/artifact-lock.json")
build_abi=$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["abi_version"])' "$identity")
[[ "$lock_abi" = "$build_abi" ]] \
  || fail "artifact-lock.json records ABI $lock_abi but the build is ABI $build_abi"

stage=$output.staging
rm -rf -- "$stage"
cp -a -- "$stock" "$stage"
cp -- "$build/gpgx.wbx.zst" "$stage/dll/gpgx.wbx.zst"
mkdir -p "$stage/gpgx-audio-observer-source"
cp -- "$identity" "$stage/gpgx-audio-observer-source/identity.json"
for notice in BizHawk-LICENSE GPGX-LICENSE.txt musl-COPYRIGHT zstd-LICENSE; do
  [[ -f "$build/$notice" ]] && cp -- "$build/$notice" "$stage/gpgx-audio-observer-source/$notice"
done
cp -- "$script_dir/artifact-lock.json" "$stage/gpgx-audio-observer-source/artifact-lock.json"
cp -- "$script_dir/README.md" "$stage/gpgx-audio-observer-source/README.md"
cp -- "$script_dir/0001-buffer-z80-audio-events.patch" \
      "$stage/gpgx-audio-observer-source/0001-buffer-z80-audio-events.patch"
mv -- "$stage" "$output"

printf 'install-observer: %s (ABI %s, core %s)\n' "$output" "$build_abi" "${recorded_raw:0:12}"
