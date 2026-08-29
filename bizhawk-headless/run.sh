#!/usr/bin/env bash
set -euo pipefail

BIZHAWK_TOOL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"

output= input_root= fixture_root= tracechaser_root=
arguments=("$@")
for ((i=0; i<${#arguments[@]}; i++)); do
  case "${arguments[i]}" in
    --output) output=${arguments[i+1]-} ;;
    --input-repository-root) input_root=${arguments[i+1]-} ;;
    --fixture-root) fixture_root=${arguments[i+1]-} ;;
    --tracechaser-root) tracechaser_root=${arguments[i+1]-} ;;
  esac
done
[[ -n "$output" && -n "$input_root" && -n "$fixture_root" && -n "$tracechaser_root" ]] || {
  echo 'explicit --tracechaser-root, --input-repository-root, --fixture-root, and --output are required' >&2
  exit 2
}
[[ "$output" = /* && "$input_root" = /* && "$fixture_root" = /* && "$tracechaser_root" = /* ]] || {
  echo 'producer, consumer, fixture, and output roots must be explicit absolute paths' >&2
  exit 2
}
[[ "$(realpath "$tracechaser_root")" == "$(realpath "$BIZHAWK_TOOL_DIR/..")" ]] || {
  echo '--tracechaser-root must identify this checkout' >&2; exit 2;
}
python3 "$BIZHAWK_TOOL_DIR/../traces/output_policy.py" \
  --tracechaser-root "$tracechaser_root" --input-repository-root "$input_root" \
  --fixture-root "$fixture_root" --output-root "$output" >/dev/null

source "$BIZHAWK_TOOL_DIR/common-env.sh"

HARNESS_EXE="$BIZHAWK_TOOL_DIR/bin/Release/BizHawk.Headless.Gpgx.exe"
"$BIZHAWK_TOOL_DIR/build.sh" >&2

unset DISPLAY
exec mono "$HARNESS_EXE" "$@"
