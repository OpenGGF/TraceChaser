#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "usage: $0 --bizhawk-home <absolute-dir> --output <absolute-new-dir>" >&2
  exit 2
}

BIZHAWK_HOME_ARG=
OUTPUT_ROOT=
while (($#)); do
  case "$1" in
    --bizhawk-home) (($# >= 2)) || usage; BIZHAWK_HOME_ARG=$2; shift 2 ;;
    --output) (($# >= 2)) || usage; OUTPUT_ROOT=$2; shift 2 ;;
    *) usage ;;
  esac
done

[[ "$BIZHAWK_HOME_ARG" = /* && -d "$BIZHAWK_HOME_ARG" ]] || usage
[[ "$OUTPUT_ROOT" = /* && ! -e "$OUTPUT_ROOT" ]] || usage

TOOL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
TRACECHASER_ROOT="$(cd "$TOOL_DIR/.." && pwd -P)"
case "$OUTPUT_ROOT" in
  "$TRACECHASER_ROOT"|"$TRACECHASER_ROOT"/*) usage ;;
esac
grep -F '"$BIZHAWK_TOOL_DIR/build.sh"' "$TOOL_DIR/run.sh" >/dev/null
grep -F '"$BIZHAWK_TOOL_DIR/build.sh"' "$TOOL_DIR/test.sh" >/dev/null
if grep -F 'if [[ ! -f "$HARNESS_EXE" ]]' "$TOOL_DIR/run.sh" >/dev/null; then
  echo "run.sh must rebuild through the deterministic compiler contract." >&2
  exit 1
fi
FIRST_ROOT="$OUTPUT_ROOT/path-a"
SECOND_ROOT="$OUTPUT_ROOT/path with spaces"
mkdir -p "$FIRST_ROOT/bizhawk-headless" \
  "$FIRST_ROOT/bizhawk" \
  "$FIRST_ROOT/dependencies" \
  "$SECOND_ROOT/bizhawk-headless" \
  "$SECOND_ROOT/bizhawk" \
  "$SECOND_ROOT/dependencies"

for name in "path-a" "path with spaces"; do
  destination="$OUTPUT_ROOT/$name/bizhawk-headless"
  cp -a "$TOOL_DIR/." "$destination/"
  cp -a \
    "$TRACECHASER_ROOT/bizhawk/preflight_bizhawk_2_11.sh" \
    "$TRACECHASER_ROOT/bizhawk/bizhawk_2_11.py" \
    "$TRACECHASER_ROOT/bizhawk/lua_source.py" \
    "$OUTPUT_ROOT/$name/bizhawk/"
  cp -a "$TRACECHASER_ROOT/dependencies/bizhawk-2.11-linux-x64.lock.json" \
    "$OUTPUT_ROOT/$name/dependencies/"
  rm -rf -- "$destination/bin" "$destination/obj" "$destination/.scratch"
  if [[ "$name" == "path with spaces" ]]; then
    CscToolPath=/hostile/compiler \
      CscToolExe=hostile-csc \
      CompilerResponseFile=/hostile/compiler.rsp \
      BIZHAWK_HOME="$BIZHAWK_HOME_ARG" "$destination/build.sh"
  else
    BIZHAWK_HOME="$BIZHAWK_HOME_ARG" "$destination/build.sh"
  fi
  if [[ -e "$destination/obj/deterministic/roslyn.rsp" ]] ||
    find "$destination/obj" -maxdepth 1 -name '.deterministic.*' -print -quit |
      grep -q .
  then
    echo "build.sh left deterministic compiler response state behind." >&2
    exit 1
  fi
done

for artifact in \
  BizHawk.Headless.Gpgx.exe \
  BizHawk.Headless.Gpgx.pdb \
  BizHawk.Headless.Gpgx.Tests.exe \
  BizHawk.Headless.Gpgx.Tests.pdb
do
  cmp "$FIRST_ROOT/bizhawk-headless/bin/Release/$artifact" \
    "$SECOND_ROOT/bizhawk-headless/bin/Release/$artifact"
done

for name in "path-a" "path with spaces"; do
  (
    cd "$OUTPUT_ROOT/$name/bizhawk-headless"
    BIZHAWK_HOME="$BIZHAWK_HOME_ARG" \
      MONO_PATH="$BIZHAWK_HOME_ARG/dll" \
      LD_LIBRARY_PATH="$BIZHAWK_HOME_ARG/dll${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
      mono bin/Release/BizHawk.Headless.Gpgx.Tests.exe \
      --filter TracePayloadCompressor --jobs 1
  )
done

DIRECT_LOG="$OUTPUT_ROOT/direct-xbuild.log"
if (
  cd "$FIRST_ROOT/bizhawk-headless"
  xbuild /nologo /verbosity:minimal /target:Rebuild \
    /property:Configuration=Release \
    /property:BizHawkDllDir="$BIZHAWK_HOME_ARG/dll" \
    BizHawk.Headless.Gpgx.csproj
) >"$DIRECT_LOG" 2>&1; then
  echo "Direct ambient xbuild unexpectedly bypassed the compiler contract." >&2
  exit 1
fi
grep -F "requires the pinned Roslyn csc.exe" "$DIRECT_LOG" >/dev/null

sha256sum \
  "$FIRST_ROOT/bizhawk-headless/bin/Release/BizHawk.Headless.Gpgx.exe" \
  "$FIRST_ROOT/bizhawk-headless/bin/Release/BizHawk.Headless.Gpgx.Tests.exe"
echo "DETERMINISTIC_BIZHAWK_HEADLESS_BUILD_OK"
