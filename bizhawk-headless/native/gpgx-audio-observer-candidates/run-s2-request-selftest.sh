#!/usr/bin/bash -p
set -euo pipefail

candidate_dir=${BASH_SOURCE[0]%/*}
[[ "$candidate_dir" != "${BASH_SOURCE[0]}" ]] || candidate_dir=.
candidate_dir=$(cd -P -- "$candidate_dir" && pwd)
repo_root=$(cd -P -- "$candidate_dir/../../.." && pwd)
recipe="$candidate_dir/s2-request-selftest-recipe.json"
base_patch="$repo_root/bizhawk-headless/native/gpgx-audio-observer/0001-buffer-z80-audio-events.patch"
candidate_patch="$candidate_dir/0001-s2-request-successor-ordinal.patch"
base_selftest="$repo_root/bizhawk-headless/native/gpgx-audio-observer/selftest"

fail()
{
  /usr/bin/printf 's2-request-selftest: %s\n' "$*" >&2
  exit 1
}

[[ $# = 3 ]] || fail "usage: $0 STOCK_SOURCE COMPILER ABSENT_OUTPUT"
stock_source=$1
compiler=$2
output=$3

[[ "$stock_source" = /* && -d "$stock_source" && ! -L "$stock_source" ]] \
  || fail "stock source must be an absolute, non-symlink directory"
[[ "$compiler" = /* && -f "$compiler" && -x "$compiler" && ! -L "$compiler" ]] \
  || fail "compiler must be an absolute, executable, non-symlink file"
[[ "$output" = /* && "$output" != */ && ! -e "$output" && ! -L "$output" ]] \
  || fail "output must be an absolute absent path without a trailing slash"
output_parent=$(/usr/bin/dirname -- "$output")
[[ -d "$output_parent" && ! -L "$output_parent" ]] \
  || fail "output parent must be an existing non-symlink directory"
case "$output/" in
  "$stock_source"/*) fail "output must be outside the stock source" ;;
esac

[[ -f "$recipe" && ! -L "$recipe" ]] || fail "candidate recipe is missing"
[[ $(/usr/bin/jq -er '.schema' "$recipe") \
  = openggf.gpgx-s2-request-candidate-selftest-recipe.v1 ]] \
  || fail "wrong candidate recipe schema"
[[ $(/usr/bin/jq -er '.layers | length' "$recipe") = 2 ]] \
  || fail "candidate recipe must contain exactly two ordered layers"
[[ $(/usr/bin/jq -er '.layers[0].file' "$recipe") \
  = bizhawk-headless/native/gpgx-audio-observer/0001-buffer-z80-audio-events.patch ]] \
  || fail "candidate recipe base layer is not the authenticated observer patch"
[[ $(/usr/bin/jq -er '.layers[1].file' "$recipe") \
  = bizhawk-headless/native/gpgx-audio-observer-candidates/0001-s2-request-successor-ordinal.patch ]] \
  || fail "candidate recipe second layer is not the fixed S2 patch"

while IFS=$'\t' read -r relative expected; do
  input="$repo_root/$relative"
  [[ -f "$input" && ! -L "$input" ]] || fail "missing versioned input: $relative"
  observed=$(/usr/bin/sha256sum "$input")
  observed=${observed%% *}
  [[ "$observed" = "$expected" ]] || fail "versioned input differs: $relative"
done < <(/usr/bin/jq -er \
  '.versioned_inputs | to_entries[] | [.key, .value] | @tsv' "$recipe")

compiler_hash=$(/usr/bin/sha256sum "$compiler")
compiler_hash=${compiler_hash%% *}
[[ "$compiler_hash" = $(/usr/bin/jq -er '.compiler.sha256' "$recipe") ]] \
  || fail "compiler hash differs"
compiler_version=$("$compiler" --version)
compiler_first_line=${compiler_version%%$'\n'*}
[[ "$compiler_first_line" = $(/usr/bin/jq -er '.compiler.version' "$recipe") ]] \
  || fail "compiler version differs"

verify_git_identity()
{
  repository=$1
  expected_commit=$2
  expected_tree=$3
  [[ -d "$repository" && ! -L "$repository" ]] \
    || fail "source repository is missing: $repository"
  [[ $(/usr/bin/git -C "$repository" rev-parse HEAD) = "$expected_commit" ]] \
    || fail "source commit differs: $repository"
  [[ $(/usr/bin/git -C "$repository" rev-parse 'HEAD^{tree}') = "$expected_tree" ]] \
    || fail "source tree differs: $repository"
  [[ -z $(/usr/bin/git -C "$repository" status --short \
    --untracked-files=all --ignore-submodules=none) ]] \
    || fail "source repository is dirty: $repository"
  [[ -z $(/usr/bin/git -C "$repository" clean -ndx) ]] \
    || fail "source repository has ignored or untracked files: $repository"
}

verify_git_identity "$stock_source" \
  "$(/usr/bin/jq -er '.source.bizhawk.commit' "$recipe")" \
  "$(/usr/bin/jq -er '.source.bizhawk.tree' "$recipe")"
verify_git_identity "$stock_source/waterbox/gpgx/Genesis-Plus-GX" \
  "$(/usr/bin/jq -er '.source.gpgx.commit' "$recipe")" \
  "$(/usr/bin/jq -er '.source.gpgx.tree' "$recipe")"
verify_git_identity "$stock_source/waterbox/musl" \
  "$(/usr/bin/jq -er '.source.musl.commit' "$recipe")" \
  "$(/usr/bin/jq -er '.source.musl.tree' "$recipe")"

if /usr/bin/git -C "$stock_source" apply --check --whitespace=error-all \
  "$candidate_patch" >/dev/null 2>&1; then
  fail "candidate patch unexpectedly applies before the authenticated base layer"
fi

/usr/bin/mkdir "$output"
candidate_success=0
mark_failure()
{
  if [[ "$candidate_success" = 0 ]]; then
    /usr/bin/printf 'candidate native selftests: FAIL\n' > "$output/FAILED"
  fi
}
trap mark_failure EXIT
/usr/bin/mkdir "$output/source" "$output/build" "$output/tmp"
/usr/bin/cp -a -- "$stock_source/." "$output/source/"
source_stage="$output/source"

/usr/bin/git -c core.hooksPath=/dev/null -C "$source_stage" apply \
  --check --whitespace=error-all "$base_patch"
/usr/bin/git -c core.hooksPath=/dev/null -C "$source_stage" apply \
  --whitespace=error-all "$base_patch"
/usr/bin/git -c core.hooksPath=/dev/null -C "$source_stage" apply \
  --check --whitespace=error-all "$candidate_patch"
/usr/bin/git -c core.hooksPath=/dev/null -C "$source_stage" apply \
  --whitespace=error-all "$candidate_patch"

/usr/bin/env -i PATH=/usr/bin:/bin LC_ALL=C TMPDIR="$output/tmp" \
  "$compiler" -std=c99 -DLSB_FIRST -O2 -Wall -Wextra -Werror \
  -I"$base_selftest" -I"$source_stage/waterbox/gpgx/cinterface" \
  "$candidate_dir/s2_request_matrix_harness.c" \
  -o "$output/build/s2-request-matrix-harness"
/usr/bin/env -i PATH=/usr/bin:/bin LC_ALL=C \
  "$output/build/s2-request-matrix-harness"

/usr/bin/env -i PATH=/usr/bin:/bin LC_ALL=C TMPDIR="$output/tmp" \
  "$compiler" -std=c99 -DLSB_FIRST -DcdStream=cdStream \
  -DHOOK_CPU -fcommon -DINLINE='static __inline__' -include string.h \
  -O2 -Wall -Wextra -Werror -Wno-unused-function -Wno-sign-compare \
  -I"$source_stage/waterbox/emulibc" \
  -I"$source_stage/waterbox/gpgx/util" \
  -I"$source_stage/waterbox/gpgx/Genesis-Plus-GX/core" \
  -I"$source_stage/waterbox/gpgx/Genesis-Plus-GX/core/cart_hw" \
  -I"$source_stage/waterbox/gpgx/Genesis-Plus-GX/core/cart_hw/svp" \
  -I"$source_stage/waterbox/gpgx/Genesis-Plus-GX/core/cd_hw" \
  -I"$source_stage/waterbox/gpgx/Genesis-Plus-GX/core/debug" \
  -I"$source_stage/waterbox/gpgx/Genesis-Plus-GX/core/input_hw" \
  -I"$source_stage/waterbox/gpgx/Genesis-Plus-GX/core/m68k" \
  -I"$source_stage/waterbox/gpgx/Genesis-Plus-GX/core/ntsc" \
  -I"$source_stage/waterbox/gpgx/Genesis-Plus-GX/core/sound" \
  -I"$source_stage/waterbox/gpgx/Genesis-Plus-GX/core/z80" \
  -I"$source_stage/waterbox/gpgx/cinterface" \
  "$source_stage/waterbox/gpgx/cinterface/audio_trace.c" \
  "$source_stage/waterbox/gpgx/Genesis-Plus-GX/core/m68k/m68kcpu.c" \
  "$candidate_dir/s2_request_m68k_boundary_harness.c" \
  -o "$output/build/s2-request-m68k-boundary-harness"
/usr/bin/env -i PATH=/usr/bin:/bin LC_ALL=C \
  "$output/build/s2-request-m68k-boundary-harness"

candidate_success=1
trap - EXIT
/usr/bin/printf 'candidate native selftests: PASS\n'
