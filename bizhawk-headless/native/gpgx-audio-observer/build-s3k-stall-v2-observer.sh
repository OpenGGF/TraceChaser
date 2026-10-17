#!/usr/bin/env bash
# Build one member of the declared S3K stall-counter pair from the same pinned source and toolchain as
# build-observer.sh:
#
#   --series baseline   0001 + 0002 + 0003          (the S3K parity/PCM observer core)
#   --series candidate  0001 + 0002 + 0003 + 0004   (baseline plus observation-only 68000 stall counters)
#   --series v2         0001 + ... + 0005           (candidate plus the stall ABI v2 CPU ledger)
#
# Both members are built by this one script with identical flags, so a byte comparison of their captures isolates
# 0004. Native selftests run first (s3k-pcm-run.sh for the baseline; m68k-stall-run.sh, which also runs the three
# existing observer runners, for the candidate). Provenance is an output: identity.json beside the build. This script
# never writes a lock file in the repository; the committed artifact locks stay byte-identical.
set -euo pipefail

fail() { printf 'build-s3k-stall-observer: %s\n' "$*" >&2; exit 1; }

script_dir=$(cd -P -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source_dir= toolchain_dir= output= series=
while (($#)); do
  case "$1" in
    --source) source_dir=${2-}; shift 2 ;;
    --toolchain) toolchain_dir=${2-}; shift 2 ;;
    --output) output=${2-}; shift 2 ;;
    --series) series=${2-}; shift 2 ;;
    *) fail "unknown argument: $1" ;;
  esac
done

for pair in "source:$source_dir" "toolchain:$toolchain_dir"; do
  name=${pair%%:*}; value=${pair#*:}
  [[ "$value" = /* && -d "$value" ]] || fail "--$name must be an absolute directory"
done
[[ "$output" = /* ]] || fail "--output must be an absolute path"
[[ ! -e "$output" ]] || fail "--output already exists: $output"
patches=("$script_dir/0001-buffer-z80-audio-events.patch" "$script_dir/0002-s3k-audio-parity-events.patch"
  "$script_dir/0003-s3k-chip-pcm-events.patch")
case "$series" in
  baseline) selftest=$script_dir/selftest/s3k-pcm-run.sh ;;
  candidate) patches+=("$script_dir/0004-m68k-stall-counters.patch"); selftest=$script_dir/selftest/m68k-stall-run.sh ;;
  v2) patches+=("$script_dir/0004-m68k-stall-counters.patch" "$script_dir/0005-m68k-cpu-ledger-v2.patch")
    selftest=$script_dir/selftest/m68k-stall2-run.sh ;;
  *) fail "--series must be baseline, candidate or v2" ;;
esac
for patch in "${patches[@]}"; do [[ -f "$patch" ]] || fail "native patch is missing: $patch"; done
zstd=$toolchain_dir/zstd
[[ -x "$zstd" ]] || fail "toolchain has no zstd"
sha() { local v; v=$(sha256sum "$1"); printf '%s' "${v%% *}"; }

# Input identity is enforced, not just recorded: a different source that happens to apply cleanly must not be able to
# claim the declared pair. Every consumed source path must reproduce its git object id at the pinned commits
# (BizHawk 427556b5 for LICENSE and waterbox/*, GPGX 051d430d for the core tree, musl 2063abc4 for COPYRIGHT; the four
# BizHawk ids already appear in source-lock.json), the source must contain nothing else, and clang/ld.lld must be the
# binaries prepare-toolchain.sh pins. The musl sysroot and zstd have no upstream pin; their digests are recorded.
git_tree_id() {
  local tmp; tmp=$(mktemp -d "$output.treeid.XXXXXX")
  git init -q "$tmp/r"
  git -c core.autocrlf=false --git-dir="$tmp/r/.git" --work-tree="$1" add -A -f .
  git --git-dir="$tmp/r/.git" write-tree
  rm -rf -- "$tmp"
}
while read -r kind expected path; do
  if [[ "$kind" = tree ]]; then actual=$(git_tree_id "$source_dir/$path"); else actual=$(git hash-object "$source_dir/$path"); fi
  [[ "$actual" = "$expected" ]] || fail "source $path is $actual, pinned $expected"
done <<'PINNED_SOURCE'
blob 62ffad7a4d2d48bb6923ccecc5bc8ebe1392ab98 LICENSE
blob 34a36f41e708fb600e5bb86f9f27ca3566a546b8 waterbox/common.mak
blob 6a5f010d2553c159936953a62ef6408bd76946e4 waterbox/linkscript.T
tree e5311f37d0e50d50b49975b8e01891bc034d5dcc waterbox/emulibc
blob 32e2f2004749496b0d1644494a1c2b41530a18ee waterbox/gpgx/Makefile
blob e780362c2e8433963fb7a338816a48a27f59975c waterbox/gpgx/gpgx.cbp
tree 1fbef2c9031b43122e11b6b62a6aea8ea1c87e90 waterbox/gpgx/cinterface
tree 4129ab228d7d775f00b3b6eccfeb220478565a14 waterbox/gpgx/util
tree 1bb96ca74d660d383e70d9cd56b88906a0773519 waterbox/gpgx/Genesis-Plus-GX
blob e6472371462f7ce7c23a2d477dbb14445aecbf77 waterbox/musl/COPYRIGHT
PINNED_SOURCE
inventory=$(cd "$source_dir" && find . -mindepth 1 -not -path './waterbox/emulibc/*' -not -path './waterbox/gpgx/cinterface/*' \
  -not -path './waterbox/gpgx/util/*' -not -path './waterbox/gpgx/Genesis-Plus-GX/*' | LC_ALL=C sort | tr '\n' ' ')
[[ "$inventory" = './LICENSE ./waterbox ./waterbox/common.mak ./waterbox/emulibc ./waterbox/gpgx ./waterbox/gpgx/Genesis-Plus-GX ./waterbox/gpgx/Makefile ./waterbox/gpgx/cinterface ./waterbox/gpgx/gpgx.cbp ./waterbox/gpgx/util ./waterbox/linkscript.T ./waterbox/musl ./waterbox/musl/COPYRIGHT ' ]] \
  || fail "source contains paths outside the pinned set: $inventory"
[[ "$(sha "$toolchain_dir/clang/usr/lib/llvm-16/bin/clang")" = bb6556bdcdeb00dca0c758da9966a9982542a23ddcaffa784a2de9344ede3fc0 ]] \
  || fail "toolchain clang differs from the prepare-toolchain.sh pin"
[[ "$(sha "$toolchain_dir/clang/usr/lib/llvm-16/bin/ld.lld")" = f8d0601bf957a1b063e29c3c43613a5b76482f6c14664b9fcac4d596871e14df ]] \
  || fail "toolchain ld.lld differs from the prepare-toolchain.sh pin"
sysroot_digest=$(cd "$toolchain_dir/sysroot" && find . -type f -print0 | LC_ALL=C sort -z | xargs -0 sha256sum | sha256sum | cut -d' ' -f1)
zstd_sha=$(sha "$zstd")

stage=$output.stage
[[ ! -e "$stage" ]] || fail "staging path already exists: $stage"
mkdir -p "$stage/src"
cp -a -- "$source_dir/." "$stage/src/"
src=$stage/src
for patch in "${patches[@]}"; do
  git -C "$src" -c core.hooksPath=/dev/null apply --check --whitespace=error-all "$patch" \
    || fail "native patch does not apply cleanly: ${patch##*/}"
  git -C "$src" -c core.hooksPath=/dev/null apply --whitespace=error-all "$patch" \
    || fail "native patch application failed: ${patch##*/}"
done

core=$src/waterbox/gpgx/Genesis-Plus-GX/core
fm_observe=$(grep -n 'gpgx_audio_trace_fm_write(address, data)' "$core/sound/sound.c" | cut -d: -f1)
fm_mutate=$(grep -n 'fm_write_impl(cycles, address, data)' "$core/sound/sound.c" | cut -d: -f1)
[[ -n "$fm_observe" && -n "$fm_mutate" && "$fm_observe" -lt "$fm_mutate" ]] \
  || fail "FM observation no longer precedes chip mutation"

bash "$selftest" "$src" "$toolchain_dir" "$stage" > "$stage/native-selftest.log" \
  || { tail -40 "$stage/native-selftest.log" >&2; fail "native selftests failed"; }

rm -rf -- "$src/waterbox/sysroot" "$src/waterbox/emulibc/obj" "$src/waterbox/gpgx/obj"
cp -a -- "$toolchain_dir/sysroot" "$src/waterbox/sysroot"
new=$src/waterbox/sysroot
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

mkdir -p "$output"
cp -- "$src/waterbox/gpgx/obj/release/gpgx.wbx" "$output/gpgx.wbx"
"$zstd" --stdout --ultra -22 --threads=1 --no-progress --force "$output/gpgx.wbx" > "$output/gpgx.wbx.zst"
cp -- "$stage/build.log" "$stage/native-selftest.log" "$output/"

# Observer state must live in the Waterbox invisible section (never savestated). Derive its index by name.
invisible=$(readelf -SW "$output/gpgx.wbx" | sed -n 's/^ *\[ *\([0-9]*\)\] \.invis .*/\1/p')
[[ -n "$invisible" ]] || fail "no .invis section"
# The Waterbox invisible section must stay within 4 MiB with every observer buffer linked in.
invis_size=$(( 16#$(readelf -SW "$output/gpgx.wbx" | sed -n 's/^ *\[ *[0-9]*\] \.invis *[A-Z]* *[0-9a-f]* *[0-9a-f]* \([0-9a-f]*\) .*/\1/p') ))
(( invis_size > 0 && invis_size <= 4194304 )) || fail ".invis is $invis_size bytes; limit 4194304"
escaped=$(readelf -Ws "$output/gpgx.wbx" | awk -v ndx="$invisible" \
  '$4 == "OBJECT" && ($8 ~ /^trace_/ || $8 ~ /^gpgx_(audio_trace|s3k_audio_parity|s3k_pcm|m68k_stall2?)_/ || $8 ~ /^(m68k_stall|stall2)/) && $7 != ndx { print $8 }')
[[ -z "$escaped" ]] || fail "observer state escaped the invisible section: $escaped"
stall_exports=$(readelf -Ws "$output/gpgx.wbx" \
  | awk '$4 == "FUNC" && $5 == "GLOBAL" && $8 ~ /^gpgx_m68k_stall_(abi_version|snapshot_size|enable|read|disable)$/ { print $8 }' \
  | LC_ALL=C sort -u | tr '\n' ' ')
stall2_exports=$(readelf -Ws "$output/gpgx.wbx" \
  | awk '$4 == "FUNC" && $5 == "GLOBAL" && $8 ~ /^gpgx_m68k_stall2_(abi_version|layout|enable|read_frame|take_accepts|take_bank|disable)$/ { print $8 }' \
  | LC_ALL=C sort -u | tr '\n' ' ')
if [[ "$series" = v2 ]]; then
  [[ "$stall2_exports" = 'gpgx_m68k_stall2_abi_version gpgx_m68k_stall2_disable gpgx_m68k_stall2_enable gpgx_m68k_stall2_layout gpgx_m68k_stall2_read_frame gpgx_m68k_stall2_take_accepts gpgx_m68k_stall2_take_bank ' ]] \
    || fail "v2 stall2 exports differ: $stall2_exports"
else
  [[ -z "$stall2_exports" ]] || fail "only the v2 series exports stall2 controls: $stall2_exports"
fi
if [[ "$series" = candidate || "$series" = v2 ]]; then
  [[ "$stall_exports" = 'gpgx_m68k_stall_abi_version gpgx_m68k_stall_disable gpgx_m68k_stall_enable gpgx_m68k_stall_read gpgx_m68k_stall_snapshot_size ' ]] \
    || fail "candidate stall exports differ: $stall_exports"
else
  [[ -z "$stall_exports" ]] || fail "baseline must not export stall controls: $stall_exports"
fi
cp -- "$src/LICENSE" "$output/BizHawk-LICENSE"
cp -- "$src/waterbox/gpgx/Genesis-Plus-GX/LICENSE.txt" "$output/GPGX-LICENSE.txt"
cp -- "$src/waterbox/musl/COPYRIGHT" "$output/musl-COPYRIGHT"
cp -- "$script_dir/notices/zstd-LICENSE" "$output/zstd-LICENSE"
rm -rf -- "$stage"

patch_json=
for patch in "${patches[@]}"; do
  patch_json+="${patch_json:+, }{\"file\": \"${patch##*/}\", \"sha256\": \"$(sha "$patch")\"}"
done
cat > "$output/identity.json" <<JSON
{
  "schema": "openggf.gpgx-s3k-stall-observer-build.v1",
  "series": "$series",
  "patches": [$patch_json],
  "source": {"bizhawk_commit": "427556b5ef3ac437eba754d90c5e7e9096c9a8df", "gpgx_commit": "051d430d3d1b54625f9900c8f152d7f232e06daf",
    "gpgx_tree": "1bb96ca74d660d383e70d9cd56b88906a0773519", "musl_commit": "2063abc4e16c84218757b1db10d3cdf9f36ef3f8", "verified": "git object ids"},
  "toolchain": {"clang_sha256": "bb6556bdcdeb00dca0c758da9966a9982542a23ddcaffa784a2de9344ede3fc0",
    "ld_lld_sha256": "f8d0601bf957a1b063e29c3c43613a5b76482f6c14664b9fcac4d596871e14df",
    "sysroot_file_digest_sha256": "$sysroot_digest", "zstd_sha256": "$zstd_sha", "zstd_threads": 1},
  "selftest_runner": "${selftest##*/}",
  "native_selftest_log_sha256": "$(sha "$output/native-selftest.log")",
  "decompressed_size": $(stat -c %s "$output/gpgx.wbx"),
  "decompressed_sha256": "$(sha "$output/gpgx.wbx")",
  "compressed_size": $(stat -c %s "$output/gpgx.wbx.zst"),
  "compressed_sha256": "$(sha "$output/gpgx.wbx.zst")",
  "build_id": "$(readelf -n "$output/gpgx.wbx" | sed -n 's/^ *Build ID: //p')",
  "invisible_section_bytes": $invis_size,
  "stall_abi": $(case "$series" in candidate) printf '{"version": 1, "snapshot_size": 80}' ;; v2) printf '{"versions": [1, 2], "snapshot_size": 80, "v2_layout": "slot 12, accept 32, bank 176, frame 40"}' ;; *) printf 'null' ;; esac)
}
JSON
printf 'build-s3k-stall-observer: %s %s\n' "$series" "$output"
