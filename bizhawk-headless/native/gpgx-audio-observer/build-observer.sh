#!/usr/bin/env bash
# Build the GPGX audio-observer core.
#
# What is pinned: the source commits in source-lock.json, the clang packages
# prepare-toolchain.sh unpacks, and 0001-buffer-z80-audio-events.patch. Those
# are inputs this project controls.
#
# What is not pinned: the host image. This script does not lock the identity of
# system utilities, does not reject an ambient environment, and does not verify
# a chained recipe digest. Build identity is an OUTPUT, written to
# artifact-lock.json and checked at install time, not a gate on the way in.
#
# Reproducibility is same-path: the musl sysroot wrappers carry an absolute
# path, so --reproduce builds twice at one fixed staging path and compares.
set -euo pipefail

fail() { printf 'build-observer: %s\n' "$*" >&2; exit 1; }

script_dir=$(cd -P -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source_dir= toolchain_dir= output= reproduce=0
while (($#)); do
  case "$1" in
    --source) source_dir=${2-}; shift 2 ;;
    --toolchain) toolchain_dir=${2-}; shift 2 ;;
    --output) output=${2-}; shift 2 ;;
    --reproduce) reproduce=1; shift ;;
    *) fail "unknown argument: $1" ;;
  esac
done

for pair in "source:$source_dir" "toolchain:$toolchain_dir"; do
  name=${pair%%:*}; value=${pair#*:}
  [[ "$value" = /* && -d "$value" ]] || fail "--$name must be an absolute directory"
done
[[ "$output" = /* ]] || fail "--output must be an absolute path"
[[ ! -e "$output" ]] || fail "--output already exists: $output"

patch=$script_dir/0001-buffer-z80-audio-events.patch
[[ -f "$patch" ]] || fail "native patch is missing"

zstd=$toolchain_dir/zstd
[[ -x "$zstd" ]] || zstd=$(command -v zstd) || fail "no zstd available"

sha() { local v; v=$(sha256sum "$1"); printf '%s' "${v%% *}"; }

# One build into $1 (an absent directory). Prints nothing; leaves artefacts there.
build_once() {
  local out=$1 stage
  stage=$out.stage
  rm -rf -- "$stage"
  mkdir -p "$stage"
  cp -a -- "$source_dir/." "$stage/src/" 2>/dev/null || { mkdir -p "$stage/src"; cp -a -- "$source_dir/." "$stage/src/"; }
  local src=$stage/src

  git -C "$src" -c core.hooksPath=/dev/null apply --check --whitespace=error-all "$patch" \
    || fail "native patch does not apply cleanly"
  git -C "$src" -c core.hooksPath=/dev/null apply --whitespace=error-all "$patch" \
    || fail "native patch application failed"

  # The observer must reach the chips before they mutate, and its state must
  # stay inside the Waterbox invisible heap. Both are correctness properties of
  # the observer, so they stay as checks even though provenance does not.
  local core=$src/waterbox/gpgx/Genesis-Plus-GX/core
  local fm_observe fm_mutate psg_observe psg_mutate
  fm_observe=$(grep -n 'gpgx_audio_trace_fm_write(address, data)' "$core/sound/sound.c" | cut -d: -f1)
  fm_mutate=$(grep -n 'fm_write_impl(cycles, address, data)' "$core/sound/sound.c" | cut -d: -f1)
  psg_observe=$(grep -n 'gpgx_audio_trace_psg_write(data)' "$core/sound/psg.c" | cut -d: -f1)
  psg_mutate=$(sed -n '226,245{s/^[[:space:]]*//; /psg_update(clocks)/=}' "$core/sound/psg.c")
  [[ -n "$fm_observe" && -n "$fm_mutate" && "$fm_observe" -lt "$fm_mutate" ]] \
    || fail "FM observation no longer precedes chip mutation"
  [[ -n "$psg_observe" && -n "$psg_mutate" && "$psg_observe" -lt "$psg_mutate" ]] \
    || fail "PSG observation no longer precedes chip mutation"

  bash "$script_dir/selftest/run.sh" "$src" "$toolchain_dir" "$stage" \
    > "$stage/native-selftest.log" || { tail -40 "$stage/native-selftest.log" >&2; fail "native selftests failed"; }

  rm -rf -- "$src/waterbox/sysroot" "$src/waterbox/emulibc/obj" "$src/waterbox/gpgx/obj"
  cp -a -- "$toolchain_dir/sysroot" "$src/waterbox/sysroot"
  # The prepared sysroot wrappers carry the absolute path the upstream
  # maintainer configured them at (a path that exists only on their machine).
  # Read that path out of the first wrapper and point them all at this tree
  # instead of recreating it under bwrap.
  local new=$src/waterbox/sysroot
  local old wrapper
  for wrapper in "$new"/bin/*; do
    [[ -f "$wrapper" ]] || continue
    old=$(grep -o -m1 -E '/[^"'"'"' ]*/waterbox/sysroot' "$wrapper" || true)
    [[ -n "$old" ]] || continue
    sed -i "s#$old#$new#g" "$wrapper"
  done

  if ! env -i PATH="$toolchain_dir/clang/usr/bin:/usr/bin:/bin" \
      LD_LIBRARY_PATH="$toolchain_dir/clang/usr/lib/x86_64-linux-gnu:$toolchain_dir/clang/usr/lib/llvm-16/lib" \
      LC_ALL=C TZ=UTC SOURCE_DATE_EPOCH=1758367997 MAKEFLAGS=-j1 \
      /bin/sh -eu -c "umask 0022; make -C '$src/waterbox/emulibc' -j1; make -C '$src/waterbox/gpgx' -j1" \
      > "$stage/build.log" 2>&1; then
    tail -80 "$stage/build.log" >&2
    fail "observer core build failed"
  fi

  mkdir -p "$out"
  cp -- "$src/waterbox/gpgx/obj/release/gpgx.wbx" "$out/gpgx.wbx"
  "$zstd" --stdout --ultra -22 --threads=0 --no-progress --force "$out/gpgx.wbx" > "$out/gpgx.wbx.zst"
  cp -- "$stage/build.log" "$stage/native-selftest.log" "$out/"

  # Derived ELF checks only; no hard-coded sizes or addresses.
  local escaped exports expected
  escaped=$(readelf -Ws "$out/gpgx.wbx" \
    | awk '$4 == "OBJECT" && ($8 ~ /^trace_/ || $8 == "gpgx_audio_trace_enabled") && $7 != "10" { print $8 }')
  [[ -z "$escaped" ]] || fail "observer state escaped the invisible heap: $escaped"
  exports=$(readelf -Ws "$out/gpgx.wbx" \
    | awk '$4 == "FUNC" && $5 == "GLOBAL" && $8 ~ /^gpgx_audio_trace_/ { print $8 }' | LC_ALL=C sort -u)
  expected='gpgx_audio_trace_abi_version
gpgx_audio_trace_abort_frame
gpgx_audio_trace_begin_frame
gpgx_audio_trace_begin_publication_epoch
gpgx_audio_trace_capacity
gpgx_audio_trace_configure
gpgx_audio_trace_disable
gpgx_audio_trace_drain
gpgx_audio_trace_end_frame
gpgx_audio_trace_event_count
gpgx_audio_trace_event_size
gpgx_audio_trace_first_fault'
  [[ "$exports" = "$expected" ]] || fail "observer exports differ:
$exports"

  cp -- "$src/LICENSE" "$out/BizHawk-LICENSE"
  cp -- "$src/waterbox/gpgx/Genesis-Plus-GX/LICENSE.txt" "$out/GPGX-LICENSE.txt"
  cp -- "$src/waterbox/musl/COPYRIGHT" "$out/musl-COPYRIGHT"
  cp -- "$script_dir/notices/zstd-LICENSE" "$out/zstd-LICENSE"
  rm -rf -- "$stage"
}

if ((reproduce)); then
  # Same fixed staging path for both runs, because the sysroot wrappers make
  # the build path-sensitive.
  fixed=$output.reproduce
  rm -rf -- "$fixed"
  mkdir -p "$fixed"
  build_once "$fixed/a"
  build_once "$fixed/b"
  cmp "$fixed/a/gpgx.wbx" "$fixed/b/gpgx.wbx" || fail "two builds differ"
  printf 'build-observer: two builds agree (%s)\n' "$(sha "$fixed/a/gpgx.wbx")"
  mv -- "$fixed/a" "$output"
  rm -rf -- "$fixed"
else
  build_once "$output"
fi

raw_sha=$(sha "$output/gpgx.wbx")
zst_sha=$(sha "$output/gpgx.wbx.zst")
patch_sha=$(sha "$patch")
build_id=$(readelf -n "$output/gpgx.wbx" | sed -n 's/^ *Build ID: //p')
abi=$(grep -oP '#define GPGX_AUDIO_TRACE_ABI_VERSION \K[0-9]+' \
  "$script_dir/../../../bizhawk-headless/native/gpgx-audio-observer/0001-buffer-z80-audio-events.patch" 2>/dev/null \
  || grep -oP '\+#define GPGX_AUDIO_TRACE_ABI_VERSION \K[0-9]+' "$patch")
identity=$(printf '%s\n%s\n%s\n' "$patch_sha" "$raw_sha" "$build_id" | sha256sum | cut -d' ' -f1)

cat > "$output/identity.json" <<JSON
{
  "schema": "openggf.gpgx-audio-observer-build.v2",
  "installation_id": "bizhawk-2.11-gpgx-audio-observer-abi$abi",
  "abi_version": $abi,
  "event_size": 32,
  "capacity": 65536,
  "patch_sha256": "$patch_sha",
  "decompressed_sha256": "$raw_sha",
  "compressed_sha256": "$zst_sha",
  "build_id": "$build_id",
  "observer_identity_sha256": "$identity"
}
JSON

python3 - "$script_dir/artifact-lock.json" "$output/identity.json" <<'PY'
import json, sys
lock_path, identity_path = sys.argv[1], sys.argv[2]
identity = json.load(open(identity_path))
lock = {
    "schema": "openggf.gpgx-audio-observer-artifact-lock.v2",
    "note": ("Provenance recorded by build-observer.sh as an output. "
             "install-observer.sh checks the built core against these values. "
             "Nothing here gates the build itself."),
    "abi": {"version": identity["abi_version"], "event_size": 32, "capacity": 65536,
            "byte_order": "little-endian"},
    "native_patch": {"file": "0001-buffer-z80-audio-events.patch",
                     "sha256": identity["patch_sha256"]},
    "core": {"decompressed_sha256": identity["decompressed_sha256"],
             "compressed_sha256": identity["compressed_sha256"],
             "build_id": identity["build_id"]},
    "identity": {"sha256": identity["observer_identity_sha256"]},
}
open(lock_path, "w").write(json.dumps(lock, indent=2) + "\n")
print("build-observer: recorded %s" % lock_path)
PY

printf 'build-observer: %s\n' "$output"
