#!/usr/bin/env bash
set -euo pipefail

BIZHAWK_TOOL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"

if (($# < 2)) || [[ "$1" != "--complete-audio-game" ]]; then
  echo 'run-complete-audio.sh requires --complete-audio-game s2|s3k as its first arguments' >&2
  exit 2
fi
for argument in "$@"; do
  case "$argument" in
    --tracechaser-root|--tracechaser-root=*|--input-repository-root|--input-repository-root=*|--fixture-root|--fixture-root=*)
      echo 'complete-audio does not accept generic producer boundary roots' >&2
      exit 2
      ;;
  esac
done

source "$BIZHAWK_TOOL_DIR/common-env.sh"

HARNESS_EXE="$BIZHAWK_TOOL_DIR/bin/Release/BizHawk.Headless.Gpgx.exe"
if [[ ! -f "$HARNESS_EXE" ]]; then
  echo "Required built harness assembly not found: $HARNESS_EXE" >&2
  exit 1
fi

unset DISPLAY
exec mono "$HARNESS_EXE" "$@"
