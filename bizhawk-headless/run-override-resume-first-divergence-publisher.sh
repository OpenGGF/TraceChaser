#!/usr/bin/env bash
set -euo pipefail

BIZHAWK_TOOL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "$BIZHAWK_TOOL_DIR/common-env.sh"
"$BIZHAWK_TOOL_DIR/build.sh"

unset DISPLAY
exec mono "$BIZHAWK_TOOL_DIR/bin/Release/BizHawk.Headless.Gpgx.exe" \
  --override-resume-first-divergence-publisher "$@"
