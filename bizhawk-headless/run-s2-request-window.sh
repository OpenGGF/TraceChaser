#!/usr/bin/env bash
set -euo pipefail

# The bounded S2 request-window producer. Every input is an explicit argument;
# nothing is discovered from an OpenGGF checkout and no window, recording or
# emulator build is baked in. Capture mode needs --bizhawk-home, which this
# script also uses to resolve the emulator assemblies before the harness runs.
#
#   run-s2-request-window.sh --request-window-mode capture \
#     --rom <rom> --movie <bk2> --movie-sha256 <sha256> \
#     --service-manifest <manifest> --candidate-manifest <manifest> \
#     --bizhawk-home <install> --first-row <n> --exclusive-end <n> \
#     --output <absent file>
#
#   run-s2-request-window.sh --request-window-mode extract \
#     --raw <raw-v3> --service-manifest <manifest> \
#     --capability-template <template> --first-row <n> --exclusive-end <n> \
#     --output-directory <directory>

BIZHAWK_TOOL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"

if (($# < 2)) || [[ "$1" != "--request-window-mode" ]]; then
  echo 'run-s2-request-window.sh requires --request-window-mode capture|extract as its first arguments' >&2
  exit 2
fi

for ((argument = 1; argument <= $#; argument++)); do
  if [[ "${!argument}" == "--bizhawk-home" && $((argument + 1)) -le "$#" ]]; then
    next=$((argument + 1))
    BIZHAWK_HOME="${!next}"
    export BIZHAWK_HOME
  fi
done

source "$BIZHAWK_TOOL_DIR/common-env.sh"

HARNESS_EXE="$BIZHAWK_TOOL_DIR/bin/Release/BizHawk.Headless.Gpgx.exe"
if [[ ! -f "$HARNESS_EXE" ]]; then
  echo "Required built harness assembly not found: $HARNESS_EXE" >&2
  exit 1
fi

unset DISPLAY
exec mono "$HARNESS_EXE" "$@"
